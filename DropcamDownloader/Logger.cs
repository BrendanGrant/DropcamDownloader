using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DropcamDownloader
{
    public static class Logger
    {
        static Logger()
        {
#if DEBUG
            //TODO: Fix something for Android tests
            fileStream = new FileStream($"!!!Output{DateTime.Now.ToString().Replace("/", "-").Replace(":", "-")}.txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            streamWriter = new StreamWriter(fileStream);
            streamWriter.AutoFlush = true;
            Trace.Listeners.Add(new TextWriterTraceListener(streamWriter));
#endif
        }

        static Stream fileStream;
        static StreamWriter streamWriter;

        public static void Log(string s)
        {
            string outputLine = string.Format("{0} {1}: {2}", GetTimestamp(), GetThreadInfo(), s);
            Debug.WriteLine(outputLine);
#if DEBUG
            if (streamWriter != null)
            {
                streamWriter.WriteLine(outputLine);
                streamWriter.Flush();
            }
#endif
        }

        private static string GetThreadInfo()
        {
#if WINDOWS_PHONE
          return System.Threading.Thread.CurrentThread.ManagedThreadId.ToString();
#else
            return Environment.CurrentManagedThreadId.ToString();
#endif
        }

        private static string GetTimestamp()
        {
            return "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]";
        }

        public static void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }

    }
}
