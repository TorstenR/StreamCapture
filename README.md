#Program to capture streams from live247 using .Net Core

For the longest time I was frustrated at not being able to reasonably record streams from Live247 to watch my favorite sporting events.  That frustration is what this little diddy was born out of.  

Note: please don't attempt to use unless you're fairly technically minded.  To state the obvious, if anyone wants to contribute, that'd be great!

###News:
- Feb 3, 2017: Finally added long overdue error checking.  It's not yet complete, but at least it'll catch the big configuration errors right away.
- Feb 2, 2017: I've just posted a pretty major refactor which should make the code more readable.  In additon, there is now a new .json file which defines the keywords and the like.  Please read the documentation below for more information on this.
- Feb 2, 2017: I tested on mac and it seemed to work great - after updated appconfig.json with the correct paths of course.

###Features:
- Polls the schedule on a configurable schedule searching for keywords (and other info) you've provided
- Spawns a separate thread and captures stream using ffmpeg
- Uses (limited) heuristics to determine channel quality and switches up mid-stream if necessary.  (working to improve)
- Should be able to start and "forget about it" and simply watch the results on plex (or whatever you use)

###Caveats:
- Not very well commented
- Almost zero exception or error handling.  If something goes wrong (including config), you'll have to read the stack trace
- Has limited testing I'm using it, but it's not been "in production" very long.  Read: probably has a crap ton of bugs....
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
- "numberOfRetries" - Number of time we retry after ffmpeg capture error before giving up
- "schedTimeOffset" - Schedule appears to be in EST.  This is the offset for local time.  (e.g. PST is -3)
- "logPath" - Puts the capture thread logs here
- "outputPath" - Puts the capture video file here (I go directly to my NAS)
- "ffmpegPath" - location of ffmpeg.exe
- "authURL" - URL to get authentication token for stream
- "captureCmdLine" - Cmd line for ffmpeg capture. Items in brackets should be self explanatory
- "concatCmdLine" - Cmd line for ffmpeg concat. Items in brackets should be self explanatory
- "muxCmdLine" - Cmd line for ffmpeg MUX. Items in brackets should be self explanatory

**keywords.json**
If running in Mode 2, keywords.json is how it's decided which shows to record based on the schedule.  More specifically:
- Top level node: name of whatever you want to name the "grouping".  This is arbitrary and doesn't affect anything programatically.
- "keywords": comma delimted list of keywords to use for which shows to record
- "exclude": comma delimited list of keywords to use to EXCLUDE any shows.  (for example, I exclude "tennis" for usa keywords)
- "preMinutes": number of minutes to start early by
- "postMinutes": number of minutes to record late by
- "langPref": used to order the channels by. (which one to try first, and then 2nd of there's a problem etc)  For example, I use "US" to get the english channels ahead of "DE".  (not sure the full list, see schedule)
- "qualityPref": also used to order channels.  I use "720p" so it tries to get HD first.

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
- Loops once a minute for duration 
- If ffmpeg process has exited, then based on some crappy heuristics change the channel to see if we can do better
- If we've reached duration, kill ffmpeg capture
- If we've captured to multiple files,  (this would happen if there were problem w/ the stream) using ffmpeg to concat them
- Use ffmpeg to MUX the .ts file to mp4 as well as add embedded metadata
