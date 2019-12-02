using BUDCC.DropcamClient;
using BUDCC.DropcamClient.DropcamJson;
using BUDCC.DropcamClient.NestJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DropcamDownloader
{
    class Program
    {
        static void Main(string[] args)
        {
            DoWork();
            Console.WriteLine("Done!");
            Console.ReadLine();
        }

        private static async Task DoWork()
        {
            var client = new DropcamClient();

            var nestInfo = await client.Login("<NEST USER NAME>", "<NEST PASSWORD");

            var cameras = await client.GetCameras();

            //Pull in roughly 30 min chucks
            var coreOffset = new TimeSpan(0, 0, 30, 0, 0);
            var fudgeFactorOffset = new TimeSpan(0, 0, 2, 0, 0); //but with about a 2 minute buffer on either side of the target time

            var absoluteStartTime = new DateTime(2019, 10, 23, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();//Set when saving should begin from
            var endTime = DateTime.Now; //set to max in a pinch
            var utcEnd = endTime.ToUniversalTime();

            var folderName = string.Format("{0} - {1}",
                absoluteStartTime.ToLocalTime().Date.ToString("yyyy - MM-dd"),
                utcEnd.ToLocalTime().Date.ToString("MM-dd")
                );

            if (!Directory.Exists(folderName))
            {
                Directory.CreateDirectory(folderName);
            }

            var nextStart = absoluteStartTime;

            foreach (var camera in cameras)
            {
                var oldestrecordingpoint = new TimeSpan((int)camera.hours_of_recording_max, 0, 0);
                Logger.Log("Looking at {0}", camera.title);

                if (camera.hours_of_recording_max == 0)
                {
                    Logger.Log("Skipping camera due to no CVR");
                }

                //TODO: specify desired camera (if more than one) to only get video from.
                //if (camera.uuid != "<INSERT CAMERA ID here>") //target camera
                //    continue;

                //Need to replace with something like this:
                //https://nexusapi-us1.camera.home.nest.com/cuepoint/934d14acf2bb4f71b65846388251180d/2?start_time=1555182249939&_=1555441437197
                var cuePoints = await GetCuePoints(camera.uuid);

                int failureCount = 0;
                int stillNotDoneProcessingCount = 0;
                while (absoluteStartTime < utcEnd.Subtract(coreOffset.Add(fudgeFactorOffset)))
                {
                    try
                    {
                        var actualStartTime = nextStart.Subtract(coreOffset.Add(fudgeFactorOffset));
                        var length = coreOffset.Add(fudgeFactorOffset).Add(fudgeFactorOffset).TotalSeconds;
                        Logger.Log("Requesting saving clip at: {0}", actualStartTime.ToLocalTime());
                        ClipInfo[] clips = await RecordClip(camera, actualStartTime, length);

                        if (clips == null)
                        {
                            nextStart = nextStart.Add(coreOffset);
                            continue;
                        }

                        Logger.Log("Got {0} clips", clips.Length);
                        var clip = clips.FirstOrDefault();
                        //todo: retry
                        if (clip == null)
                        {
                            Logger.Log("Null clip!!!");
                            failureCount++;

                            if (failureCount >= 5)
                            {
                                nextStart = nextStart.Subtract(coreOffset);
                                failureCount = 0;
                            }

                            continue;
                        }
                        else
                        {
                            Logger.Log("Received clip {0}", clip.id);
                        }
                        while (clip.is_generated == false)
                        {
                            if (clip.is_error)
                            {
                                Logger.Log("Error generating clip... deleting {0}.", clip.id);
                                await ReliableDelete(clip);

                                failureCount++;

                                if (failureCount > 5)
                                {
                                    nextStart = nextStart.Add(coreOffset);
                                    failureCount = -1;

                                    //need to delete before bugging out
                                    break;
                                }

                                Logger.Log("re-requesting saving clip at: {0}", actualStartTime.ToLocalTime());
                                clips = await RecordClip(camera, actualStartTime, length);

                                Logger.Log("Got {0} clips", clips.Length);
                                clip = clips.FirstOrDefault();
                            }

                            stillNotDoneProcessingCount++;

                            if (stillNotDoneProcessingCount > 10)
                            {
                                Logger.Log("Clip {0} - should have started at {1}. Deleting and skipping for now(?)", clip.id, clip.start_time);
                                Logger.Log("Deleting clip {0}", clip.id);
                                await ReliableDelete(clip);
                                stillNotDoneProcessingCount = 0;
                                nextStart = nextStart.Add(coreOffset);
                                break;
                            }

                            System.Threading.Thread.Sleep(15000);
                            int clipId = clip.id;
                            Logger.Log("Getting clip info");
                            do
                            {
                                clip = await GetClip(clipId);

                                if (clip == null)
                                {
                                    Logger.Log("Null clip!!!");
                                }
                            } while (clip == null);
                        }

                        if (failureCount == -1)
                        {
                            failureCount = 0;
                            continue;
                        }
                        if (clip.is_generated)
                        {
                            Logger.Log("Clip is complete!");

                            var clipStartTime = UnixTime.DateTimeFromUnixTimestampSeconds((int)clip.start_time); //Not going to care about decimial values
                            clipStartTime = clipStartTime.ToLocalTime();

                            string filename = clipStartTime.ToString("yyyy-MM-dd HH-mm-ss") + " " + new TimeSpan(0, 0, (int)clip.length_in_seconds).ToString("h'h 'm'm 's's'") + ".mp4";

                            await ReliableDownload(clip, Path.Combine(folderName, filename));
                            GC.Collect();
                            System.Threading.Thread.Sleep(1000);
                            Logger.Log("Deleting clip {0}", clip.id);
                            await ReliableDelete(clip);

                            System.Threading.Thread.Sleep(5000);
                            Logger.Log("Lets ago again!!!");

                            //Set next start time base
                            nextStart = nextStart.Add(coreOffset);
                            failureCount = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        ex = ex;
                    }
                }
            }
        }

        private static async Task<CuePoint[]> GetCuePoints(string cameraId)
        {
            using (var c = WebRequestHelper.GetClient())
            {
                try
                {

                    var startTime = DateTime.UtcNow.AddMonths(-1).GetUnixTime();
                    var nowIsh = UnixTime.GetCurrentUnixTimestampMillis();


                    c.DefaultRequestHeaders.ExpectContinue = false;
                    c.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                    c.DefaultRequestHeaders.Add("Accept", "*/*");
                    c.DefaultRequestHeaders.Add("Referer", "https://home.nest.com/");
                    c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    var responseBody = await c.GetStringAsync(string.Format(URLs.GetCuePointEx, cameraId, startTime, nowIsh));

                    Logger.Log(responseBody);

                    var item = JsonConvert.DeserializeObject<CuePoint[]>(responseBody);
                    return item;
                }
                catch (Exception ex)
                {
                    //if( ex.Message.Contains("404 (Not Found)"))
                    //{
                    //    return new ClipInfo() { id = id, is_error = true };
                    //    throw;
                    //}
                }
                return null;
            }
        }

        private static async Task ReliableDownload(ClipInfo clip, string filename)
        {
            bool gotClip = false;

            do
            {
                try
                {
                    using (var c = new HttpClient())
                    {
                        Logger.Log("Downloading {0}", clip.download_url);
                        var file = await c.GetByteArrayAsync(clip.download_url);
                        System.IO.File.WriteAllBytes(filename, file);
                        Logger.Log("Wrote {0} bytes to {1}", file.Length, filename);
                        gotClip = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Error: " + ex.ToString());
                }
            } while (gotClip == false);
        }

        private static async Task ReliableDelete(ClipInfo clip)
        {
            await DeleteFile(clip.id);

            var tmpClip = await GetClip(clip.id);
            while (tmpClip != null)
            {
                if (tmpClip != null)
                {
                    Logger.Log("Re-attempt of Deleting clip {0}", clip.id);
                    System.Threading.Thread.Sleep(5000);
                    await DeleteFile(clip.id);
                }

                tmpClip = await GetClip(clip.id);
            }
        }

        private static async Task DeleteFile(int id)
        {
            using (var c = WebRequestHelper.GetClient())
            {
                try
                {
                    c.DefaultRequestHeaders.ExpectContinue = false;
                    c.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                    c.DefaultRequestHeaders.Add("Accept", "*/*");
                    c.DefaultRequestHeaders.Add("Referer", "https://home.nest.com/");
                    c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    var responseBody = await c.DeleteAsync(URLs.GetClip(id));
                }
                catch (Exception ex)
                {
                    ex = ex;
                }
            }
        }

        private static async Task<ClipInfo> GetClip(int id)
        {
            using (var c = WebRequestHelper.GetClient())
            {
                try
                {
                    c.DefaultRequestHeaders.ExpectContinue = false;
                    c.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                    c.DefaultRequestHeaders.Add("Accept", "*/*");
                    c.DefaultRequestHeaders.Add("Referer", "https://home.nest.com/");
                    c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    var responseBody = await c.GetStringAsync(string.Format(URLs.GetClip(id)));
                    
                    var item = JsonConvert.DeserializeObject<ClipInfo[]>(responseBody);
                    return item.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    //if( ex.Message.Contains("404 (Not Found)"))
                    //{
                    //    return new ClipInfo() { id = id, is_error = true };
                    //    throw;
                    //}
                }
                return null;
            }
        }

        private static async Task<ClipInfo[]> RecordClip(CameraInformation camera, DateTime startTime, double length)
        {
            someEvilLabel:
            using (var c = WebRequestHelper.GetClient())
            {
                var content = new FormUrlEncodedContent(new[]
                {
                       //new KeyValuePair<string, string>("uuid", camera.uuid),
                       new KeyValuePair<string, string>("uuid", camera.uuid),
                       new KeyValuePair<string, string>("start_date", startTime.GetUnixTime().ToString()),
                       new KeyValuePair<string, string>("length", length.ToString()),
                       new KeyValuePair<string, string>("is_time_lapse", "false"),
                   });

                c.DefaultRequestHeaders.ExpectContinue = false;
                //c.DefaultRequestHeaders.Add("Content-Type", "application/x-www-form-urlencoded; charset=utf-8");
                c.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                c.DefaultRequestHeaders.Add("Accept", "*/*");
                c.DefaultRequestHeaders.Add("Referer", "https://home.nest.com/");
                c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                var videoSubmission = await c.PostAsync(URLs.RequestVideo, content);

                if (videoSubmission.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Logger.Log("ErrorCode: {0}-{1}", (int)videoSubmission.StatusCode, videoSubmission.ToString());
                    if (videoSubmission.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        //This usually means you've gone over how much stored data is in your CVR
                        Logger.Log("Conflict found, may abort.");
                    }
                    else if (videoSubmission.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Logger.Log("File not found, lets try again.");
                    }
                    else if (videoSubmission.StatusCode == System.Net.HttpStatusCode.BadGateway)
                    {
                        //This status code happens sometimes, not sure why.
                        goto someEvilLabel; //this is the second time in my career I've written a goto... and I still feel dirty/lazy.
                    }
                }
                else
                {
                    var responseBody = await videoSubmission.Content.ReadAsStringAsync();

                    var item = JsonConvert.DeserializeObject<ClipInfo[]>(responseBody);
                    return item;
                }

                return null;
            }
        }
    }
}
