#Short hack to capture streams from live247 using .Net Core

For the longest time I was frustrated at not being able to reasonably record streams from Live247 to watch my favorite sporting events.  That frustration is what this little diddy was born out of.  

Note: please don't attempt to use unless you're fairly technically minded.  I don't have much time to help, but will do my best of course.  To state the obvious, if anyone wants to contribute to the documentation that'd be great!

###Features:
- Polls the schedule on a configurable schedule searching for keywords you've provided
- Spawns a separate thread and captures stream using ffmpeg
- Uses (not very good) heuristics to determine channel quality and switches up mid-stream if necessary.
- Should be able to start and "forget about it" and simply watch the results on plex (or whatever you use)

###Caveats:
- Not very well commented
- Almost zero exception or error handling.  If something goes wrong (including config), you'll have to read the stack trace
- Has limited testing I'm using it, but it's not been "in production" very long.  Read: probably has a crap ton of bugs....
- It's mostly a hack.  Seriously....the code is not production quality. 
- Still playing with ffmpeg to find the right balance on erroring out when there's a problem with the stream.
- My plex did not recognize the embedded meta-data.  Not sure why....

###Areas to help:
- Bugs....  (feel free to file them on github and submit a PR of course...)
- Testing on other platforms.  (love to hear your feedback on that)
- ffmpeg expertise, especially around erroring out if there's a problem with the feed.  (-xerror chokes immediately on 720P feeds)
- General improvements (I'm open to whatever)

###How to use:
The easiest way to use this is to type 'streamCapture --keywords="chelsea"'  This will search and schedule and fire off a thread which will "sleep" until it's time to capture the stream.

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

There are other command line options if you know specifically what you want to record.  You can see these by running streamCapture w/ --help or -?.

###Compiling:
- Go to http://www.dot.net and download the right .NET Core for your platform
- Make "hello world" to make sure your environment is correct and you understand at least the basics
- Compile streamCapture by typing 'dotnet build'

###How the program works (assumes you're using keywords):

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
