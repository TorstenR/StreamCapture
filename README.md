# Program to capture streams from live247 (and other Smoothstream offerings) using .Net Core.

For the longest time I was frustrated at not being able to reasonably record streams from Live247 to watch my favorite sporting events.  That frustration is what this project was born out of.  

This program is intended to run largely unattended, recording shows based on keywords (with many options to help it decide which channel to use, and which show has precedence), recovering gracefully from network glitches, and alerting via email about what's going on.  

Note: please don't attempt to use unless you're fairly technically minded.  To state the obvious, if anyone wants to contribute, that'd be great!

### News:
- Apr 17, 2017: Now creating poster and fan art for plex so that the score is not given away.  Also, moved the schedule URL out to appsettings.  Finally, am updating channel list right before recording start to accomodate for last minute changes.
- Feb 27, 2017: Program is now feature complete.  I don't plan on doing much more except to fix any bugs that crop up.
- Feb 22, 2017: Big upgrade to keywords.  Please see below for more info.  (regex, scoring, etc)
- Feb 15, 2017: Improved scheduling so it works more as expected even when there's a lot of shows
- Feb 11, 2017: More bug fixes and better emails.
- Feb 9, 2017: Lots of bug fixes and better stability.  Files are now cleaned up according to retention days.
- Feb 7, 2017: Email alerting now for schedule and completed shows via SMTP
- Feb 5, 2017: Added the notion of network speed to determine best servers and channels.  There's even an up front general network speed test.  Yes, this means that you can put in multiple servers now in appconfig.  (in fact, it's a requirement)
- Feb 3, 2017: Finally added long overdue error checking.  It's not yet complete, but at least it'll catch the big configuration errors right away.
- Feb 2, 2017: I've just posted a pretty major refactor which should make the code more readable.  In additon, there is now a new .json file which defines the keywords and the like.  Please read the documentation below for more information on this.
- Feb 2, 2017: I tested on mac and it seemed to work great - after updated appconfig.json with the correct paths of course.

### Features:
- Polls the schedule on a configurable schedule searching for keywords (and other info) you've provided
- Allows "pre" and "post" minutes to be specified per show.  (e.g. some events potentially have overtime, some don't...)
- Spawns a separate thread and captures stream using ffmpeg, comlete with seperate log file
- Works cross platform.  (confirmed on Windows and Mac.  If you're using on nix...let me know please)
- When multiple channels are available, it orders them based on heuristics (e.g. language preference, quality, etc)
- Email alerting for what's scheduled and what's done.  Includes a daily digest, and any changes that happen during the day.
- Cycles through multiple servers if supplied to find the fastest one
- Switches servers and channels if problems are encountered to find a better one
- Detects "stalls" by monitoring the file size every 10 seconds
- Cleans files up after a user defined period of time.
- Should be able to start and "forget about it" and simply watch the results on plex (or whatever you use)

### More about Email:
There are 4 kinds of email: 
- Daily digests
- Schedule updates
- Program updates
- Alerts/problems. 

**Daily Digests**: These are summary emails sent out once a day on the first hour listed in 'scheduleCheck' (appsettings).  Please note that 'hoursInFuture' now supports the keyword 'today' so that the digest includes only a 24 hour period.  However, it will include whatever window you've defined.  For me, I get this digest at 1am every day and it gives me a good summary and what just happened, and what is planned to happen today capture/recording wise.

**Schedule Updates**: Everytime the schedule is checked ('scheduleCheck'), any schedule changes are sent out in an update email.  I usually only get one of these a day (1am) since I've got my 'hoursInFuture' set to 'today'.  If you have the window set to say 12 hours, then everytime something new shows up inside 12 hours, you'll get an email.  The idea is that you're alerted of changes - and setting the window to 'today' means I pretty much don't get them.

**Program Updates**: If the 'starredFlag' is set to 'true' the the keyword block that matched the program you're capturing, you'll get an email saying when that specific program has started recording, and when it's published.  In practice, I set this flag for keyword blocks that I really care about so I'm alerted as soon as it's ready.

**Alerts/Problems**: If there's a problem, you'll always get an email.  The most common one is that a starred ('starredFlag') program will not record because there's already too many going at once ('concurrentCaptures').  Another somewhat common failure is that the program did not complete, usually due to too many retries. (network errors)   However, if there's any kind of failure, an email is send out.  (this includes things like unhandled exceptions, no internet access, etc) 

### Caveats:
- My plex did not recognize the embedded meta-data.  Not sure why....

### Areas to help:
- Bugs....  (feel free to file them on github and submit a PR of course...)
- More testing on other platforms.  (I've done some testing on Mac with good results)
- General improvements (I'm open to whatever)

### How to use:
There are 2 "modes" to run.  They are:

**Mode 1: Single execution and exit**
Simply pass in --duration, --channels, --filename, and optionaly --datetime to record a single show.  Use --help for more specifics.

**Mode 2: Infinite loop which scans schedule for wanted shows to capture  (this is the intended primary mode)**
Simply run StreamCapture with no parameters.  It will read keywords.json every n hours and queue up shows to record as appropriate.

**appsettings.json**
There are multiple config values in appsettings.json.  By looking at these you'll get a better idea what's happening.
- "user" - Yes, username and password for smooth streams
- "pass"
- "scheduleCheck" - Comma separated hours (24 hour format) for when you want the scheduled checked.
- "hoursInFuture" - Don't schedule anything farther out than this number of hours.  Use 'today' for same day only.
- "numberOfRetries" - Number of time we retry after ffmpeg capture error before giving up inside of a 15 window.  
- "schedTimeOffset" - Schedule appears to be in EST.  This is the offset for local time.  (e.g. PST is -3)
- "acceptableRate" - KB/s, below which it will error out and retry.  Meant to catch "dead" or "hung" streams.  I use 50...
- "concurrentCaptures" - Max captures that can be happening at one time.  I use 3
- "retentionDays" - Beyond this, log and video files are removed
- "logPath" - Puts the capture thread logs here
- "outputPath" - Puts the capture video file here (I capture locally, and then move to my NAS - see next param)
- "nasPath" - Optional parameter will will copy the final .mp4 file to this location (in my case, my NAS)
- "scheduleURL" - URL to get the schedule JSON feed
- "ffmpegPath" - location of ffmpeg.exe
- "authURL" - URL to get authentication token for stream
- "authMinutes" - For long captures, the auth might have to be refreshed is retrying.  This is the number of minutes for that.  I use 220.
- "captureCmdLine" - Cmd line for ffmpeg capture. Items in brackets should be self explanatory
- "concatCmdLine" - Cmd line for ffmpeg concat. Items in brackets should be self explanatory
- "muxCmdLine" - Cmd line for ffmpeg MUX. Items in brackets should be self explanatory
- "artCmdLine" - ffmpeg cmd line used to create a poster and fan art jpg (used by plex)
- "serverList" - Servers which are available.  I use "dNAw2,dNAw1,dEU.UK2,dEU.NL2"
- "mailAddress" - Mail address that alerts are sent to
- "smtpServer" - smtp server used to send alert emails
- "smtpPort" - port
- "smtpUser"- smtp username
- "smtpPass"- smtp password

**keywords.json**
If running in Mode 2, keywords.json is how it's decided which shows to record based on the schedule.  More specifically:
- Top level node: name of whatever you want to name the "grouping".  This is arbitrary and doesn't affect anything programatically.
- "starredFlag": when 'true', this will annotate the file so you can see it better
- "emailFlag": when 'true', an email is sent when capture is started and published for this keyword group
- "keywords": array of keywords.  Each string in the array is comma delimmted and is ANDed together, and each seperate string in the array is OR'd together.  In addition, Regular expressions are supported.
- "exclude": same as with keywords, but are exclusions.
- "categories": same as with keywords, but matching against the smoothstream categories.  These are AND'd with keywords. Use double quotes or empty to include "everything".  
- "preMinutes": number of minutes to start early by
- "postMinutes": number of minutes to record late by
- "langPref": used to order the channels by. (which one to try first, and then 2nd of there's a problem etc)  For example, I use "US" to get the english channels ahead of "DE". 
- "qualityPref": also used to order channels.  I use "720p" so it tries to get HD first.
- "channelPref": support channel number preference

Please note that the order in which you put the groups is important as this is the order in which the shows will be scheduled.  This means that you want to put the stuff you care about the most first for when there are too many concurrent shows For example, I put keywords for my favorite EPL teams first, and then put a general "EPL" towards the bottom.  That way, it'll make sure my favorite teams get priority, but if there is "room", it'll fit other EPL games in opportunistically.

Scoring: If you put one or more '+' and '-' signs in any of your preferences, it will affect which channel is chosen. (there are preferences for language, quality, and channel)  For example, if you put '+US' for language, then when caculating the "score" for a channel, US will be worth +1 higher.  Same is true for '-'.  You can put multiple + or -.  Basically, a "score" is determined for each channel.  This score is what is used to determine which order the channels are tried in.  This should allow you to configure things such that your preferences are respected, but the show will get recorded regardless.  In other words, preferences are just that - preferences for what to grab first. 

### Troubleshooting
- First thing is to check your log file/s for what might have gone wrong.  Most often, this will lead you in the right direction.
- Double check that .Net Core is working right by compiling and running "hello world" or whatever.
- Make sure ffmpeg is installed and working correctly
- Make sure you have disk space and that your internet connection is good.  This is especially true when capturing multiple streams at once.
- If all else fails, use your debugger (VS Code?) and see what's going on.

### Compiling:
- Go to http://www.dot.net and download the right .NET Core for your platform
- Make "hello world" to make sure your environment is correct and you understand at least the basics
- Compile streamCapture by typing 'dotnet restore' and then 'dotnet build'
- You can type 'dotnet run' to test/run
- To create a stand-alone (deployable) app, use something like 'dotnet publish -c release -r win10-x64'.  This will create a 'release' directory with main .exe and it's dependencies.  It'll be fully self-contained.

### How the program works
This explains how "Mode 2" works.  "Mode 1" is similar, but without the loop.  (go figure)

**Main thread goes into an infinite loop and does the following:**
- Grabs schedule on the configured hours
- Searches for keywords
- For each match, spawns a capture thread IF there's not already one going, and it's not more than n hours in the future
- Send update email if appropriate
- Cleans up older files
- Goes to sleep until it's time to repeat...

**In each child thread:**
- Puts the output in the log file in the log directory specified
- Sleeps until it's time to record
- Grabs an authentication token
- Wakes up and spawns ffmpeg to capture the stream in a separate process
- Timer is set for capture stop time 
- Monitors file size every 10 seconds and kills capture process if the rate falls below a user defined threshold.
- If ffmpeg process has exited early, then change the server to see if we can do better
- If still having trouble, after going through all the servers, switch channels if multiple to find the fastest one.
- If we've reached intended duration (or we've retried too many times), kill ffmpeg capture
- If we've captured to multiple files,  (this would happen if there were problem w/ the stream) using ffmpeg to concat them
- Use ffmpeg to MUX the .ts file to mp4 as well as add embedded metadata
- Copy mp4 file to NAS if defined
