# DropcamDownloader

Having built some code around accessing Dropcam (and Nestcam) APIs to make a Windows Phone app, I eventually found myself with the desire to download hours or weeks worth of video data for archival purposes. This program is more or less the result.

Originally relying on code straight out of my Windows Phone apps, recently I decided to port it so that uses [my open sourced Dropcam/Nestcam client](https://github.com/BrendanGrant/BUDCC.DropcamClient).

The code here is not the most pretty, having been built in haste for a specific purpose, then modified adhoc as needs arose, it never got the love & attention of something I'd normally want to release. However I've decided the security risks of the Nest product are too great to continue to use it in my home.

Use at your own risk. I cannot be responsible should Nest decide they don't like this kind of bulk data export.

In theory, someone could have a 7-10 day CVR plan and have a bot pull down a day or two worth of video every day or two, effectively creating an unlimited CVR without paying for the higher costs of the 30 day plan(s), though I had a 30 day plan and would log in every couple of weeks to pull down the last couple of weeks.
