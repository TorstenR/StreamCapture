using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net​.NetworkInformation;
using Newtonsoft.Json;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StreamCaptureWeb;


namespace StreamCapture
{
    public class Recorder
    {
        IConfiguration configuration;
        public Recordings recordings { get; set; }
        public ManualResetEvent mre { get; set; }
        public List<ScheduleShow> scheduleShowList { get; set; }

        public Recorder(IConfiguration _configuration)
        {
            configuration=_configuration;
            mre = new ManualResetEvent(false);

            //Instanciate recordings
            recordings = new Recordings(configuration,mre);

            //Test Authentication
            Task<string> authTask = Authenticate();
            string hashValue=authTask.Result;  
            if(string.IsNullOrEmpty(hashValue))                     
            {
                Console.WriteLine($"ERROR: Unable to authenticate.  Check username and password?  Bad auth URL?");
                Environment.Exit(1);                
            }
        }

        private long TestInternet(TextWriter logWriter)
        {
            long bytesPerSecond=0;
            long fileSize=10000000;

            logWriter.WriteLine($"{DateTime.Now}: Peforming speed test to calibrate your internet connection....please wait");            

            using (var client = new HttpClient())
            {            
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                client.GetAsync("http://download.thinkbroadband.com/10MB.zip").ContinueWith((requestTask) =>
                {
                    HttpResponseMessage response = requestTask.Result;
                    response.EnsureSuccessStatusCode();
                    response.Content.LoadIntoBufferAsync();
                }).Wait();
                sw.Stop();

                //Calc baseline speed
                bytesPerSecond = fileSize/sw.Elapsed.Seconds;
                double MBsec=Math.Round(((double)bytesPerSecond/1000000), 1);
                logWriter.WriteLine($"{DateTime.Now}: Baseline speed: {MBsec} MBytes per second.  ({fileSize/1000000}MB / {sw.Elapsed.Seconds} seconds)");

                if(bytesPerSecond < 700000)
                    logWriter.WriteLine($"{DateTime.Now}: WARNING: Your internet connection speed may be a limiting factor in your ability to capture streams");
            }

            return bytesPerSecond;
        }    

        //Does a dryrun using keywords.json - showing what it *would* do, but not actually doing it
        public void DryRun()
        {
            //Create channel history object
            ChannelHistory channelHistory = new ChannelHistory();

            //Grab schedule
            Schedule schedule = new Schedule();
            schedule.LoadSchedule(configuration["scheduleURL"], configuration["debug"]).Wait();
            scheduleShowList = schedule.GetScheduledShows();

            //Grabs schedule and builds a recording list based on keywords
            List<RecordInfo> recordInfoList = recordings.BuildRecordSchedule(scheduleShowList);

            //Send digest
            new Mailer().SendDailyDigest(configuration,recordings);      

            //Go through record list and display
            foreach (RecordInfo recordInfo in recordInfoList)
            {
                //Create servers object
                Servers servers=new Servers(configuration["ServerList"]);

                //Create the server/channel selector object
                ServerChannelSelector scs=new ServerChannelSelector(new StreamWriter(Console.OpenStandardOutput()),channelHistory,servers,recordInfo);                
            }      
            Thread.Sleep(3000);
        }

        private void StartWebServer()
        {
            Console.WriteLine($"{DateTime.Now}: Starting Kestrel...");
            var webHostBuilder = new WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseKestrel()
                .UseStartup<Startup>()
                .ConfigureServices(services => services.AddSingleton<Recorder>(this))
                .UseUrls("http://*:5000");
            var host = webHostBuilder.Build();
            host.Start();
        }

        public void MonitorMode()
        {
            //Create new recordings object to manage our recordings
            //Recordings recordings = new Recordings(configuration,mre);

            //Start web server
            StartWebServer();

            //Create channel history object
            ChannelHistory channelHistory = new ChannelHistory();

            try
            {
                //Grab schedule from interwebs and loop forever, checking every n hours for new shows to record
                while(true)
                {
                    //Refresh schedule from website
                    //
                    // This goes and grabs the online .json file which is the current schedule from Live247
                    // This list is *all* the shows currently posted.  Usually, live247 only posts a day or two at a time.
                    Schedule schedule = new Schedule();
                    schedule.LoadSchedule(configuration["scheduleURL"], configuration["debug"]).Wait();
                    scheduleShowList = schedule.GetScheduledShows();

                    //Grabs schedule and builds a recording list based on keywords
                    List<RecordInfo> recordInfoList = recordings.BuildRecordSchedule(scheduleShowList);

                    //Time to mail the daily digest and clean up master list (but only if it's the first hour on the hour list)
                    string[] times = configuration["scheduleCheck"].Split(',');
                    if (DateTime.Now.Hour == Convert.ToInt16(times[0]))
                    {
                        new Mailer().SendDailyDigest(configuration, recordings);
                        recordings.CleanupOldShows();
                    }

                    //Go through record list, spawn a new process for each show found
                    foreach (RecordInfo recordInfo in recordInfoList)
                    {
                        //If show is not already spawend and cancelled, let's go!
                        if(!recordInfo.processSpawnedFlag && !recordInfo.cancelledFlag)
                        {
                            recordInfo.processSpawnedFlag=true;   
                            
                            recordInfo.mre = new ManualResetEvent(false);
                            recordInfo.cancellationTokenSource=new CancellationTokenSource();
                            recordInfo.cancellationToken=recordInfo.cancellationTokenSource.Token;

                            // Queue show to be recorded now
                            Task.Factory.StartNew(() => QueueRecording(channelHistory,recordInfo,configuration,true),recordInfo.cancellationToken); 
                        }
                    }  

                    //Determine how long to sleep before next check
                    DateTime nextRecord=DateTime.Now;
                    
                    //find out if schedule time is still today
                    if(DateTime.Now.Hour < Convert.ToInt16(times[times.Length-1]))
                    {
                        for(int i=0;i<times.Length;i++)
                        {
                            int recHour=Convert.ToInt16(times[i]);
                            if(DateTime.Now.Hour < recHour) 
                            {
                                nextRecord=new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,recHour,0,0,0,DateTime.Now.Kind);
                                break;
                            }
                        }
                    }
                    else
                    {
                        //build date tomorrow
                        int recHour=Convert.ToInt16(times[0]);  //grab first time in the list
                        nextRecord=new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,recHour,0,0,0,DateTime.Now.Kind);
                        nextRecord=nextRecord.AddDays(1);
                    }  

                    //Since we're awake, let's see if there are any files needing cleaning up
                    VideoFileManager.CleanOldFiles(configuration);

                    //Wait
                    TimeSpan timeToWait = new TimeSpan(0,1,0);
                    if(nextRecord>DateTime.Now)
                        timeToWait = nextRecord - DateTime.Now;
                    Console.WriteLine($"{DateTime.Now}: Now sleeping for {timeToWait.Hours+1} hours before checking again at {nextRecord.ToString()}");
                    mre.WaitOne(timeToWait);
                    mre.Reset();      
                    Console.WriteLine($"{DateTime.Now}: Woke up, now checking again...");
                } 
            }
            catch(Exception e)
            {
                Console.WriteLine("======================");
                Console.WriteLine($"{DateTime.Now}: ERROR - Exception!");
                Console.WriteLine("======================");
                Console.WriteLine($"{e.Message}\n{e.StackTrace}");

                //Send alert mail
                string body="NO LONGER RECORDING!  Main loop failed with Exception "+e.Message;
                body=body+"\n"+e.StackTrace;
                new Mailer().SendErrorMail(configuration,"NO LONGER RECORDING!  StreamCapture Exception: ("+e.Message+")",body);
            }
        }

        public void QueueRecording(ChannelHistory channelHistory,RecordInfo recordInfo,IConfiguration configuration,bool useLogFlag)
        {
            //Write to our very own log as there might be other captures going too
            StreamWriter logWriter=new StreamWriter(Console.OpenStandardOutput());
            if(useLogFlag)
            {
                string logPath=Path.Combine(configuration["logPath"],recordInfo.fileName+"Log.txt");
                FileStream fileHandle = new FileStream (logPath, FileMode.OpenOrCreate, FileAccess.Write);
                logWriter = new StreamWriter (fileHandle);
            }
            logWriter.AutoFlush = true;

            //try-catch so we don't crash the whole thing
            try
            {
                //Dump
                logWriter.WriteLine($"{DateTime.Now}: Queuing show: {recordInfo.description} Starting on {recordInfo.GetStartDT()} for {recordInfo.GetDuration()} minutes ({recordInfo.GetDuration() / 60}hrs ish)");
                
                //Wait here until we're ready to start recording
                if(recordInfo.strStartDT != null)
                {
                    TimeSpan oneHour = new TimeSpan(1,0,0);
                    DateTime recStart = recordInfo.GetStartDT();
                    TimeSpan timeToWait = recStart - DateTime.Now;
                    logWriter.WriteLine($"{DateTime.Now}: Starting recording at {recStart} - Waiting for {timeToWait.Days} Days, {timeToWait.Hours} Hours, and {timeToWait.Minutes} minutes.");
                    
                    while(timeToWait.Seconds>=0 && DateTime.Now < recStart)
                    {
                        timeToWait = recStart - DateTime.Now;
                        if(timeToWait > oneHour) 
                            timeToWait = oneHour;  

                        if(timeToWait.Seconds>=0)
                        {
                            recordInfo.mre.WaitOne(timeToWait); 
                            mre.Reset();     
                            logWriter.WriteLine($"{DateTime.Now}: Waking up to check..."); 
                        }                
                    }

                    if(recordInfo.cancelledFlag)
                    {
                        logWriter.WriteLine($"{DateTime.Now}: Cancelling due to request");
                        return;
                    }
                }       

                //Authenticate
                Task<string> authTask = Authenticate();
                string hashValue=authTask.Result;
                if(string.IsNullOrEmpty(hashValue))                     
                {
                    Console.WriteLine($"ERROR: Unable to authenticate.  Check username and password?");
                    Environment.Exit(1);               
                }

                //Get latest channels (Channels may have changed since the show was queued.  Exception is thrown if time has changed, or no longer there)  
                logWriter.WriteLine($"{DateTime.Now}: Grabbing latest channels");
                new Schedule().RefreshChannelList(configuration, recordInfo);

                //We need to manage our resulting files
                VideoFileManager videoFileManager = new VideoFileManager(configuration,logWriter,recordInfo.fileName);

                //Set capture started flag
                recordInfo.captureStartedFlag=true;            

                //Capture stream
                CaptureStream(logWriter,hashValue,channelHistory,recordInfo,videoFileManager);

                //Let's take care of processing and publishing the video files
                videoFileManager.ConcatFiles();
                videoFileManager.MuxFile(recordInfo.description);
                videoFileManager.PublishAndCleanUpAfterCapture(recordInfo.category,recordInfo.preMinutes);

                //Cleanup
                logWriter.WriteLine($"{DateTime.Now}: Done Capturing");
                logWriter.Dispose();

                //Send alert mail
                if(recordInfo.emailFlag)
                    new Mailer().SendShowReadyMail(configuration,recordInfo);
            }
            catch(Exception e)
            {
                logWriter.WriteLine("======================");
                logWriter.WriteLine($"{DateTime.Now}: ERROR - Exception!");
                logWriter.WriteLine("======================");
                logWriter.WriteLine($"{e.Message}\n{e.StackTrace}");

                //Send alert mail
                string body=recordInfo.description+" failed with Exception "+e.Message;
                body=body+"\n"+e.StackTrace;
                new Mailer().SendErrorMail(configuration,"StreamCapture Exception! ("+e.Message+")",body);
            }
        }
        private async Task<string> Authenticate()
        {
            string hashValue=null;

            try
            {
                using (var client = new HttpClient())
                {
                    //http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php?username=foo&password=bar&site=view247

                    //Build URL
                    string strURL=configuration["authURL"];
                    strURL=strURL.Replace("[USERNAME]",configuration["user"]);
                    strURL=strURL.Replace("[PASSWORD]",configuration["pass"]);

                    var response = await client.GetAsync(strURL);
                    response.EnsureSuccessStatusCode(); // Throw in not success

                    string stringResponse = await response.Content.ReadAsStringAsync();
                    
                    //Console.WriteLine($"Response: {stringResponse}");

                    //Grab hash
                    JsonTextReader reader = new JsonTextReader(new StringReader(stringResponse));
                    while (reader.Read())
                    {
                        if (reader.Value != null)
                        {
                            //Console.WriteLine("Token: {0}, Value: {1}", reader.TokenType, reader.Value);
                            if(reader.TokenType.ToString() == "PropertyName" && reader.Value.ToString() == "hash")
                            {
                                reader.Read();
                                hashValue=reader.Value.ToString();
                                break;
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                //Send alert mail
                string body="Unable to authenticate. Exception "+e.Message;
                body=body+"\n"+e.StackTrace;
                new Mailer().SendErrorMail(configuration,"Authentication Exception! ("+e.Message+")",body);

                throw new Exception("Unable to authenticate.  Unexpected result from service",e);
            }

            return hashValue;
        }
     
        //A key member function (along with BuildRecordSchedule) which does the bulk of the work
        //
        //I'll be a bit more verbose then usual so I can remember later what I was thinking
        private void CaptureStream(TextWriter logWriter,string hashValue,ChannelHistory channelHistory,RecordInfo recordInfo,VideoFileManager videoFileManager)
        {
            //Process manager for ffmpeg
            //
            //This is there ffmpeg is called, and a watchdog timer created to make sure things are going ok
            ProcessManager processManager = new ProcessManager(configuration);
                        
            //Test internet connection and get a baseline
            //Right now, the baseline is not used for anything other than a number in the logs.
            //This could be taken out...
            long internetSpeed = TestInternet(logWriter); 

            //Create servers object
            //This is the list of live247 servers we'll use to cycle through, finding the best quality stream
            Servers servers=new Servers(configuration["ServerList"]);

            //Create the server/channel selector object
            //
            //This object is what uses the heurstics to determine the right server/channel combo to use.  
            //Factors like language, quality, and rate factor in.
            ServerChannelSelector scs=new ServerChannelSelector(logWriter,channelHistory,servers,recordInfo);

            //Marking time we started and when we should be done
            DateTime captureStarted = DateTime.Now;
            DateTime captureTargetEnd = recordInfo.GetStartDT().AddMinutes(recordInfo.GetDuration());
            if(!string.IsNullOrEmpty(configuration["debug"]))
                captureTargetEnd = DateTime.Now.AddMinutes(1);
            DateTime lastStartedTime = captureStarted;
            TimeSpan duration=(captureTargetEnd.AddMinutes(1))-captureStarted;  //the 1 minute takes care of alignment slop

            //Update capture history
            //This saves to channelhistory.json file and is used to help determine the initial order of server/channel combos
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).recordingsAttempted+=1;
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).lastAttempt=DateTime.Now;

            //Build output file - the resulting capture file.
            //See VideoFileManager for specifics around file management (e.g. there can be multiple if errors are encountered etc)
            VideoFileInfo videoFileInfo=videoFileManager.AddCaptureFile(configuration["outputPath"]);

            //Email that show started
            if(recordInfo.emailFlag)
                new Mailer().SendShowStartedMail(configuration,recordInfo);

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(scs.GetServerName(),scs.GetChannelNumber(),hashValue,videoFileInfo.GetFullFile());
            logWriter.WriteLine($"=========================================");
            logWriter.WriteLine($"{DateTime.Now}: Starting {captureStarted} on server/channel {scs.GetServerName()}/{scs.GetChannelNumber()}.  Expect to be done by {captureTargetEnd}.");
            logWriter.WriteLine($"                      {configuration["ffmpegPath"]} {cmdLineArgs}");
            CaptureProcessInfo captureProcessInfo = processManager.ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,(int)duration.TotalMinutes,videoFileInfo.GetFullFile(),recordInfo.cancellationToken);  
            logWriter.WriteLine($"{DateTime.Now}: Exited Capture.  Exit Code: {captureProcessInfo.process.ExitCode}");

            //Main loop to capture
            //
            //This loop is never entered if the first capture section completes without incident.  However, it is not uncommon
            //for errors to occur, and this loop takes care of determining the right next server/channel combo
            //as well as making sure that we don't just try forever.
            int numRetries=Convert.ToInt32(configuration["numberOfRetries"]);
            int retryNum=0;
            for(retryNum=0;DateTime.Now<=captureTargetEnd && retryNum<numRetries && !recordInfo.cancelledFlag;retryNum++)
            {           
                logWriter.WriteLine($"{DateTime.Now}: Capture Failed for server/channel {scs.GetServerName()}/{scs.GetChannelNumber()}. Retry {retryNum+1} of {configuration["numberOfRetries"]}");

                //Let's make sure the interwebs are still there.  If not, let's loop until they come back or the show ends.
                while(!IsInternetOk() && DateTime.Now<=captureTargetEnd)
                {
                    bool logFlag=false;
                    if(!logFlag)
                        logWriter.WriteLine($"{DateTime.Now}: Interwebs are down.  Checking every minute until back or show ends");
                    TimeSpan oneMinute = new TimeSpan(0, 1, 0);
                    Thread.Sleep(oneMinute);
                }

                //Check to see if we need to re-authenticate  (most tokens have a lifespan)
                int authMinutes=Convert.ToInt16(configuration["authMinutes"]);
                if(DateTime.Now>captureStarted.AddMinutes(authMinutes))
                {
                    logWriter.WriteLine($"{DateTime.Now}: It's been more than {authMinutes} authMinutes.  Time to re-authenticate");
                    Task<string> authTask = Authenticate();
                    hashValue=authTask.Result;
                    if(string.IsNullOrEmpty(hashValue))
                    {
                        Console.WriteLine($"{DateTime.Now}: ERROR: Unable to authenticate.  Check username and password?");
                        throw new Exception("Unable to authenticate during a retry");
                    }
                }

                //Log avg streaming rate for channel history 
                logWriter.WriteLine($"{DateTime.Now}: Avg rate is {captureProcessInfo.avgKBytesSec}KB/s for {scs.GetServerName()}/{scs.GetChannelNumber()}");                

                //Set new avg streaming rate for channel history    
                channelHistory.SetServerAvgKBytesSec(scs.GetChannelNumber(),scs.GetServerName(),captureProcessInfo.avgKBytesSec);

                //Go to next channel if channel has been alive for less than 15 minutes
                //The idea is that if a server/channel has been stable for at least 15 minutes, no sense trying to find another.  (could make it worse)
                TimeSpan fifteenMin=new TimeSpan(0,15,0);
                if((DateTime.Now-lastStartedTime) < fifteenMin)
                {
                    //Set rate for current server/channel pair
                    scs.SetAvgKBytesSec(captureProcessInfo.avgKBytesSec);

                    //Get correct server and channel (determined by heuristics)
                    if(!scs.IsBestSelected())
                    {
                        scs.GetNextServerChannel();
                        retryNum=-1; //reset retries since we haven't got through the server/channel list yet
                    }
                }
                else
                {
                    retryNum=-1; //reset retries since it's been more than 15 minutes
                }

                //Set new started time and calc new timer     
                TimeSpan timeJustRecorded=DateTime.Now-lastStartedTime;
                lastStartedTime = DateTime.Now;
                TimeSpan timeLeft=captureTargetEnd-DateTime.Now;

                //Update channel history
                channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).hoursRecorded+=timeJustRecorded.TotalHours;
                channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).recordingsAttempted+=1;
                channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).lastAttempt=DateTime.Now;

                //Build output file
                videoFileInfo=videoFileManager.AddCaptureFile(configuration["outputPath"]);                

                //Now get capture setup and going again
                cmdLineArgs=BuildCaptureCmdLineArgs(scs.GetServerName(),scs.GetChannelNumber(),hashValue,videoFileInfo.GetFullFile());
                logWriter.WriteLine($"{DateTime.Now}: Starting Capture (again) on server/channel {scs.GetServerName()}/{scs.GetChannelNumber()}");
                logWriter.WriteLine($"                      {configuration["ffmpegPath"]} {cmdLineArgs}");
                captureProcessInfo = processManager.ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,(int)timeLeft.TotalMinutes+1,videoFileInfo.GetFullFile(),recordInfo.cancellationToken);
            }
            recordInfo.completedFlag=true;
            logWriter.WriteLine($"{DateTime.Now}: Done Capturing Stream.");         

            //Update capture history and save
            TimeSpan finalTimeJustRecorded=DateTime.Now-lastStartedTime;
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).hoursRecorded+=finalTimeJustRecorded.TotalHours;
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).lastSuccess=DateTime.Now;
            channelHistory.SetServerAvgKBytesSec(scs.GetChannelNumber(),scs.GetServerName(),captureProcessInfo.avgKBytesSec);      
            channelHistory.Save();

            //check if actually done and didn't error out early
            //We assume too many retries as that's really the only way out of the loop (outside of an exception which is caught elsewhere)
            if(DateTime.Now<captureTargetEnd)
            {
                if(recordInfo.cancelledFlag)
                    logWriter.WriteLine($"{DateTime.Now}: Cancelled {recordInfo.description}"); 
                else
                    logWriter.WriteLine($"{DateTime.Now}: ERROR!  Too many retries - {recordInfo.description}"); 

                //set partial flag
                recordInfo.partialFlag=true;

                //Send alert mail
                string body=recordInfo.description+" partially recorded due to too many retries or cancellation.  Time actually recorded is "+finalTimeJustRecorded.TotalHours;
                new Mailer().SendErrorMail(configuration,"Partial: "+recordInfo.description,body);
            }
        }

        private bool IsInternetOk()
        {
            bool retval = true;

/* 
//Ping does not work on Mac
            try
            {
                Ping pingSender = new Ping();
                Task<PingReply> pingTask = new Ping().SendPingAsync("www.google.com",2);
                PingReply reply= pingTask.Result;
                if(reply.Status != IPStatus.Success)
                    retval = false;
            }
            catch(Exception)
            {
                retval = false;
            }
            */

            return retval;
        }

        private string BuildCaptureCmdLineArgs(string server,string channel,string hashValue,string outputPath)
        {
            //C:\Users\mark\Desktop\ffmpeg\bin\ffmpeg -i "http://dnaw1.smoothstreams.tv:9100/view247/ch01q1.stream/playlist.m3u8?wmsAuthSign=c2VydmVyX3RpbWU9MTIvMS8yMDE2IDM6NDA6MTcgUE0maGFzaF92YWx1ZT1xVGxaZmlzMkNYd0hFTEJlaTlzVVJ3PT0mdmFsaWRtaW51dGVzPTcyMCZpZD00MzM=" -c copy t.ts
            
            string cmdLineArgs = configuration["captureCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputPath);
            cmdLineArgs=cmdLineArgs.Replace("[SERVER]",server);
            cmdLineArgs=cmdLineArgs.Replace("[CHANNEL]",channel);
            cmdLineArgs=cmdLineArgs.Replace("[AUTHTOKEN]",hashValue);

            return cmdLineArgs;
        }                    
    }
}
