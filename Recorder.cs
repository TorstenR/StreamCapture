using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;


namespace StreamCapture
{
    public class Recorder
    {
        IConfiguration configuration;

        public Recorder(IConfiguration _configuration)
        {
            configuration=_configuration;

            //Test Authentication
            Task<string> authTask = Authenticate();
            string hashValue=authTask.Result;  
            if(string.IsNullOrEmpty(hashValue))                     
            {
                Console.WriteLine($"ERROR: Unable to authenticate.  Check username and password?  Bad auth URL?");
                Environment.Exit(1);                
            }
        }
        private void DumpRecordInfo(TextWriter logWriter,RecordInfo recordInfo)
        {
            logWriter.WriteLine($"{DateTime.Now}: Queuing show: {recordInfo.description}");
            logWriter.WriteLine($"                      Starting on {recordInfo.GetStartDT()} for {recordInfo.GetDuration()} minutes ({recordInfo.GetDuration()/60}hrs ish)");
            logWriter.WriteLine($"                      Channel list is: {recordInfo.GetChannelString()}");           
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
        public void MonitorMode()
        {
            //Create new recordings object to manage our recordings
            Recordings recordings = new Recordings(configuration);

            //Create channel history object
            ChannelHistory channelHistory = new ChannelHistory();

            //Grab schedule from interwebs and loop forever, checking every n hours for new shows to record
            while(true)
            {
                //List for items we need to remove from the recordInfoList (we can't while in the loop)
                List<RecordInfo> toDeleteList = new List<RecordInfo>();

                //Grabs schedule and builds a recording list based on keywords
                List<RecordInfo> recordInfoList = recordings.BuildRecordSchedule();

                //Build mail to send out
                Mailer mailer = new Mailer();
                string mailText = "";

                //Go through record list, spawn a new process for each show found
                foreach (RecordInfo recordInfo in recordInfoList)
                {            
                    //If show is not already in the past and not already queued, let's go!
                    int hoursInFuture=Convert.ToInt32(configuration["hoursInFuture"]);
                    bool showInFuture=recordInfo.GetEndDT()>DateTime.Now;
                    bool showClose=recordInfo.GetStartDT()<=DateTime.Now.AddHours(hoursInFuture);
                    bool showQueued=recordInfo.processSpawnedFlag;
                    if(showInFuture && showClose && !showQueued)
                    {
                        recordInfo.processSpawnedFlag=true;
                        DumpRecordInfo(Console.Out,recordInfo); 

                        //Add to mailer
                        mailText=mailer.AddNewShowToString(mailText,recordInfo);

                        // Queue show to be recorded now
                        Task.Factory.StartNew(() => QueueRecording(channelHistory,recordInfo,configuration,true)); 
                    }
                    else
                    {
                        if(!showInFuture)
                        {
                            Console.WriteLine($"{DateTime.Now}: Show already finished: {recordInfo.description} at {recordInfo.GetStartDT()}");
                            toDeleteList.Add(recordInfo); //So we don't leak
                        }
                        else if(!showClose)
                            Console.WriteLine($"{DateTime.Now}: Show too far away: {recordInfo.description} at {recordInfo.GetStartDT()}");
                        else if(showQueued)
                            Console.WriteLine($"{DateTime.Now}: Show already queued: {recordInfo.description} at {recordInfo.GetStartDT()}");                    
                    }
                }  

                //Send mail if we have something
                if(!string.IsNullOrEmpty(mailText))
                    mailer.SendNewShowMail(configuration,mailText);

                //Determine how long to sleep before next check
                string[] times=configuration["scheduleCheck"].Split(',');
                DateTime nextRecord=DateTime.Now;
                
                //find out if schedule time is still today
                if(DateTime.Now.Hour < Convert.ToInt32(times[times.Length-1]))
                {
                    for(int i=0;i<times.Length;i++)
                    {
                        int recHour=Convert.ToInt32(times[i]);
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
                    int recHour=Convert.ToInt32(times[0]);  //grab first time in the list
                    nextRecord=new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,recHour,0,0,0,DateTime.Now.Kind);
                    nextRecord=nextRecord.AddDays(1);
                }

                //Let's clean up show list now
                foreach (RecordInfo recordInfo in toDeleteList)
                {
                    recordInfoList.Remove(recordInfo);
                }

                //Since we're awake, let's see if there are any files needing cleaning up
                VideoFileManager.CleanOldFiles(configuration);

                //Wait
                TimeSpan timeToWait = nextRecord - DateTime.Now;
                Console.WriteLine($"{DateTime.Now}: Now sleeping for {timeToWait.Hours+1} hours before checking again at {nextRecord.ToString()}");
                Thread.Sleep(timeToWait);         
                Console.WriteLine($"{DateTime.Now}: Woke up, now checking again...");
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
                DumpRecordInfo(logWriter,recordInfo);
                
                //Wait here until we're ready to start recording
                if(recordInfo.strStartDT != null)
                {
                    DateTime recStart = recordInfo.GetStartDT();
                    TimeSpan timeToWait = recStart - DateTime.Now;
                    logWriter.WriteLine($"{DateTime.Now}: Starting recording at {recStart} - Waiting for {timeToWait.Days} Days, {timeToWait.Hours} Hours, and {timeToWait.Minutes} minutes.");
                    if(timeToWait.Seconds>=0)
                        Thread.Sleep(timeToWait);
                }       

                //Authenticate
                Task<string> authTask = Authenticate();
                string hashValue=authTask.Result;
                if(string.IsNullOrEmpty(hashValue))                     
                {
                    Console.WriteLine($"ERROR: Unable to authenticate.  Check username and password?");
                    Environment.Exit(1);               
                }

                //We need to manage our resulting files
                VideoFileManager videoFileManager = new VideoFileManager(configuration,logWriter,recordInfo.fileName);            

                //Capture stream
                CaptureStream(logWriter,hashValue,channelHistory,recordInfo,videoFileManager);

                //Let's take care of processing and publishing the video files
                videoFileManager.ConcatFiles();
                videoFileManager.MuxFile(recordInfo.description);
                videoFileManager.PublishAndCleanUpAfterCapture();

                //Cleanup
                logWriter.WriteLine($"{DateTime.Now}: Done Capturing");
                logWriter.Dispose();

                //Send alert mail
                new Mailer().SendShowReadyMail(configuration,recordInfo);
            }
            catch(Exception e)
            {
                logWriter.WriteLine("======================");
                logWriter.WriteLine($"{DateTime.Now}: ERROR - Exception!");
                logWriter.WriteLine("======================");
                logWriter.WriteLine($"{e.StackTrace}");

                //Send alert mail
                string body=recordInfo.description+" failed with Exception "+e.Message;
                body=body+"\n"+e.StackTrace;
                new Mailer().SendErrorMail(configuration,"StreamCapture Exception! ("+e.Message+")",body);
            }
        }
        private async Task<string> Authenticate()
        {
            string hashValue=null;

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

            return hashValue;
        }
     
        private void CaptureStream(TextWriter logWriter,string hashValue,ChannelHistory channelHistory,RecordInfo recordInfo,VideoFileManager videoFileManager)
        {
            //Process manager for ffmpeg
            ProcessManager processManager = new ProcessManager(configuration);
                        
            //Test internet connection and get a baseline
            long internetSpeed = TestInternet(logWriter); 

            //Create servers object
            Servers servers=new Servers(configuration["ServerList"]);

            //Create the server/channel selector object
            ServerChannelSelector scs=new ServerChannelSelector(logWriter,channelHistory,servers,recordInfo);

            //Marking time we started and when we should be done
            DateTime captureStarted = DateTime.Now;
            DateTime captureTargetEnd = recordInfo.GetStartDT().AddMinutes(recordInfo.GetDuration());
            if(!string.IsNullOrEmpty(configuration["debug"]))
                captureTargetEnd = DateTime.Now.AddMinutes(1);
            DateTime lastStartedTime = captureStarted;
            TimeSpan duration=captureTargetEnd-captureStarted;

            //Update capture history
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).recordingsAttempted+=1;
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).lastAttempt=DateTime.Now;

            //Build output file
            VideoFileInfo videoFileInfo=videoFileManager.AddCaptureFile(configuration["outputPath"]);

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(scs.GetServerName(),scs.GetChannelNumber(),hashValue,videoFileInfo.GetFullFile());
            logWriter.WriteLine($"=========================================");
            logWriter.WriteLine($"{DateTime.Now}: Starting {captureStarted} on server/channel {scs.GetServerName()}/{scs.GetChannelNumber()}.  Expect to be done by {captureTargetEnd}.");
            logWriter.WriteLine($"                      {configuration["ffmpegPath"]} {cmdLineArgs}");
            CaptureProcessInfo captureProcessInfo = processManager.ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,(int)duration.TotalMinutes,videoFileInfo.GetFullFile());  
            logWriter.WriteLine($"{DateTime.Now}: Exited Capture.  Exit Code: {captureProcessInfo.process.ExitCode}");

            //
            //retry loop if we're not done yet
            //
            int numRetries=Convert.ToInt32(configuration["numberOfRetries"]);
            for(int retryNum=0;DateTime.Now<=captureTargetEnd.AddSeconds(-10) && retryNum<numRetries;retryNum++)
            {           
                logWriter.WriteLine($"{DateTime.Now}: Capture Failed for server/channel {scs.GetServerName()}/{scs.GetChannelNumber()}. Retry {retryNum+1} of {configuration["numberOfRetries"]}");

                //Check to see if we need to re-authenticate
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

                //Set new avg streaming rate for channel history    
                channelHistory.SetServerAvgKBytesSec(scs.GetChannelNumber(),scs.GetServerName(),captureProcessInfo.avgKBytesSec);

                //Go to next channel if channel has been alive for less than 15 minutes
                TimeSpan fifteenMin=new TimeSpan(0,15,0);
                if((DateTime.Now-lastStartedTime) < fifteenMin)
                {
                    //Set rate for current server/channel pair
                    scs.SetAvgKBytesSec(captureProcessInfo.avgKBytesSec);

                    //Get correct server and channel (determined by heuristics)
                    scs.GetNextServerChannel();
                }
                else
                {
                    retryNum=0; //reset retries since it's been more than 15 minutes
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
                captureProcessInfo = processManager.ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,(int)timeLeft.TotalMinutes+1,videoFileInfo.GetFullFile());
            }
            logWriter.WriteLine($"{DateTime.Now}: Finished Capturing Stream.");             

            //Update capture history and save
            TimeSpan finalTimeJustRecorded=DateTime.Now-lastStartedTime;
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).hoursRecorded+=finalTimeJustRecorded.TotalHours;
            channelHistory.GetChannelHistoryInfo(scs.GetChannelNumber()).lastSuccess=DateTime.Now;
            channelHistory.SetServerAvgKBytesSec(scs.GetChannelNumber(),scs.GetServerName(),captureProcessInfo.avgKBytesSec);      
            channelHistory.Save();
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
