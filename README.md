#Program to capture streams from live247 (and apparently other Smoothstream offerings) using .Net Core

For the longest time I was frustrated at not being able to reasonably record streams from Live247 to watch my favorite sporting events.  That frustration is what this project was born out of.  

This program is intended to run largely unattended, recording shows based on keywords, recovering gracefully from network glitches, and alerting via email about what's going on.  

Note: please don't attempt to use unless you're fairly technically minded.  To state the obvious, if anyone wants to contribute, that'd be great!

###News:
- Feb 15, 2017: Improved scheduling so it works more as expected even when there's a lot of shows
- Feb 11, 2017: More bug fixes and better emails.
- Feb 9, 2017: Lots of bug fixes and better stability.  Files are now cleaned up according to retention days.
- Feb 7, 2017: Email alerting now for schedule and completed shows via SMTP
- Feb 5, 2017: Added the notion of network speed to determine best servers and channels.  There's even an up front general network speed test.  Yes, this means that you can put in multiple servers now in appconfig.  (in fact, it's a requirement)
- Feb 3, 2017: Finally added long overdue error checking.  It's not yet complete, but at least it'll catch the big configuration errors right away.
- Feb 2, 2017: I've just posted a pretty major refactor which should make the code more readable.  In additon, there is now a new .json file which defines the keywords and the like.  Please read the documentation below for more information on this.
- Feb 2, 2017: I tested on mac and it seemed to work great - after updated appconfig.json with the correct paths of course.

###Features:
- Polls the schedule on a configurable schedule searching for keywords (and other info) you've provided
- Allows "pre" and "post" minutes to be specified per show.  (e.g. some events potentially have overtime, some don't...)
- Spawns a separate thread and captures stream using ffmpeg, comlete with seperate log file
- Works cross platform.  (confirmed on Windows and Mac.  If you're using on nix...let me know please)
- When multiple channels are available, it orders them based on some heuristics (e.g. higher quality first)
- Email alerting for what's scheduled and what's done
- Cycles through multiple servers if supplied to find the fastest one
- Uses (limited) heuristics to determine channel quality and switches to better channels if necessary.  (working to improve)
- Detects "stalls" by monitoring the file size every 10 seconds
- Should be able to start and "forget about it" and simply watch the results on plex (or whatever you use)

###Caveats:
- Not very well commented
- My plex did not recognize the embedded meta-data.  Not sure why....

###Areas to help:
- Bugs....  (feel free to file them on github and submit a PR of course...)
- More testing on other platforms.  (I've done some testing on Mac with good results)
- General improvements (I'm open to whatever)

###How to use:
There are 2 "modes" to run.  They are:

**Mode 1: Single execution and exit**
Simply pass in --duration, --channels, --filename, and optionaly --datetime to record a single show.  Use --help for more specifics.

**Mode 2: Infinite loop which scans schedule for wanted shows to capture  (this is the intended primary mode)**
Simply run StreamCapture with no parameters.  It will read keywords.json every n hours and queue up shows to record as appropriate.

**appsettings.json**
There are multiple config values in appsettings.json.  By looking at these you'll get a better idea what's happening.
- "user" - Yes, username and password
- "pass"
- "scheduleCheck" - Comma separated hours (24 hour format) for when you want the scheduled checked.
- "hoursInFuture" - Don't schedule anything farther out than this number of hours
- "numberOfRetries" - Number of time we retry after ffmpeg capture error before giving up inside of a 15 window.  
- "schedTimeOffset" - Schedule appears to be in EST.  This is the offset for local time.  (e.g. PST is -3)
- "acceptableRate" - KB/s, below which it will error out and retry.  Meant to catch "dead" or "hung" streams.  I use 50...
- "concurrentCaptures" - Max captures that can be happening at one time.  I use 3
- "retentionDays" - Beyond this, log and video files are removed
- "logPath" - Puts the capture thread logs here
- "outputPath" - Puts the capture video file here (I capture locally, and then move to my NAS - see next param)
- "nasPath" - Optional parameter will will copy the final .mp4 file to this location (in my case, my NAS)
- "ffmpegPath" - location of ffmpeg.exe
- "authURL" - URL to get authentication token for stream
- "authMinutes" - For long captures, the auth might have to be refreshed is retrying.  This is the number of minutes for that.  I use 220.
- "captureCmdLine" - Cmd line for ffmpeg capture. Items in brackets should be self explanatory
- "concatCmdLine" - Cmd line for ffmpeg concat. Items in brackets should be self explanatory
- "muxCmdLine" - Cmd line for ffmpeg MUX. Items in brackets should be self explanatory
- "serverList" - Servers which are available.  I use "dNAw2,dNAw1,dEU.UK2,dEU.NL2"
- "mailAddress" - Mail address that alerts are sent to
- "smtpServer" - smtp server used to send alert emails
- "smtpPort" - port
- "smtpUser"- smtp username
- "smtpPass"- smtp password

**keywords.json**
If running in Mode 2, keywords.json is how it's decided which shows to record based on the schedule.  More specifically:
- Top level node: name of whatever you want to name the "grouping".  This is arbitrary and doesn't affect anything programatically.
- "keywords": comma delimted list of keywords to use for which shows to record
- "exclude": comma delimited list of keywords to use to EXCLUDE any shows.  (for example, I exclude "tennis" for usa keywords)
- "preMinutes": number of minutes to start early by
- "postMinutes": number of minutes to record late by
- "langPref": used to order the channels by. (which one to try first, and then 2nd of there's a problem etc)  For example, I use "US" to get the english channels ahead of "DE".  (not sure the full list, see schedule)
- "qualityPref": also used to order channels.  I use "720p" so it tries to get HD first.

Please note that the order in which you put the groups is important as this is the order in which the shows will be scheduled.  This means that you want to put the stuff you care about the most first, so that if there are too many concurrent shows, it'll be the ones with lower priority.  For exampple, I put keywords for my favorite EPL teams first, and then put a general "EPL" towards the bottom.  That way, it'll make sure my favorite teams get the open slots, but if there is "room", it'll fit other EPL games in opportunistically.

###Troubleshooting###
- First thing is to check your log file/s for what might have gone wrong.  Most often, this will lead you in the right direction.
- Double check that .Net Core is working right by compiling and running "hello world" or whatever.
- Make sure ffmpeg is installed and working correctly
- Make sure you have disk space and that your internet connection is good.  This is especially true when capturing multiple streams at once.
- If all else fails, use your debugger (VS Code?) and see what's going on.

###Compiling:
- Go to http://www.dot.net and download the right .NET Core for your platform
- Make "hello world" to make sure your environment is correct and you understand at least the basics
- Compile streamCapture by typing 'dotnet build'

###How the program works
This explains how "Mode 2" works.  "Mode 1" is similar, but without the loop.  (go figure)

**Main thread goes into an infinite loop and does the following:**
- Grabs schedule on the configured hours
- Searches for keywords
- For each match, spawns a capture thread IF there's not already one going, and it's not more than n hours in the future
- Goes to sleep until it's time to repeat...

**In each child thread:**
- Puts the output in the log file using the schedule ID as the name
- Sleeps until it's time to record
- Grabs an authentication token
- Wakes up and spawns ffmpeg to capture the stream in a separate process
- Timer is set for capture stop time 
- Monitor file size every 10 seconds and kill capture process if the rate falls below a threshold.
- If ffmpeg process has exited early, then based speed change the server to see if we can do better
- If still having trouble, after going through all the servers, switch channels if multiple to find the fastest one.
- If we've reached duration, kill ffmpeg capture
- If we've captured to multiple files,  (this would happen if there were problem w/ the stream) using ffmpeg to concat them
- Use ffmpeg to MUX the .ts file to mp4 as well as add embedded metadata
