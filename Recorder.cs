using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics;
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

        public void MonitorMode()
        {
            //Create new recordings object to manage our recordings
            Recordings recordings = new Recordings(configuration);

            //Grab schedule from interwebs and loop forever, checking every n hours for new shows to record
            while(true)
            {
                //Grabs schedule and builds a recording list based on keywords
                List<RecordInfo> recordInfoList = recordings.BuildRecordSchedule();

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

                        // Queue show to be recorded now
                        Task.Factory.StartNew(() => QueueRecording(recordInfo,configuration,true)); 
                    }
                    else
                    {
                        if(!showInFuture)
                            Console.WriteLine($"{DateTime.Now}: Show already finished: {recordInfo.description} at {recordInfo.GetStartDT()}");
                        if(!showClose)
                            Console.WriteLine($"{DateTime.Now}: Show too far away: {recordInfo.description} at {recordInfo.GetStartDT()}");
                        if(showQueued)
                            Console.WriteLine($"{DateTime.Now}: Show already queued: {recordInfo.description} at {recordInfo.GetStartDT()}");
                    }
                }  

                //Determine how long to sleep before next check
                string[] times=configuration["scheduleCheck"].Split(',');
                DateTime nextRecord=DateTime.Now;
                
                //find out if today
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

                //Wait
                TimeSpan timeToWait = nextRecord - DateTime.Now;
                Console.WriteLine($"{DateTime.Now}: Now sleeping for {timeToWait.Hours+1} hours before checking again at {nextRecord.ToString()}");
                Thread.Sleep(timeToWait);         
                Console.WriteLine($"{DateTime.Now}: Woke up, now checking again...");
            } 
        }

        public void QueueRecording(RecordInfo recordInfo,IConfiguration configuration,bool useLogFlag)
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

            //Dump
            DumpRecordInfo(logWriter,recordInfo);

            //Wait here until we're ready to start recording
            if(recordInfo.strStartDT != null)
            {
                DateTime recStart = recordInfo.GetStartDT();
                TimeSpan timeToWait = recStart - DateTime.Now;
                logWriter.WriteLine($"{DateTime.Now}: Starting recording at {recStart} - Waiting for {timeToWait.Days} Days, {timeToWait.Hours} Hours, and {timeToWait.Minutes} minutes.");
                if(timeToWait.Seconds>0)
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

            //Capture stream
            int numFiles=CaptureStream(logWriter,hashValue,recordInfo);

            //Fixup output
            FixUp(logWriter,numFiles,recordInfo);

            //Cleanup
            logWriter.WriteLine($"{DateTime.Now}: Done Capturing - Cleaning up");
            logWriter.Dispose();
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
     
        private int CaptureStream(TextWriter logWriter,string hashValue,RecordInfo recordInfo)
        {
            int currentFileNum = 0;
            int channelIdx = 0;
            int currentChannelFailureCount = 0;

            //Create channel history object
            ChannelHistory channelHistory = new ChannelHistory();

            //Marking time we started and when we should be done
            DateTime captureStarted = DateTime.Now;
            DateTime captureTargetEnd = captureStarted.AddMinutes(recordInfo.GetDuration());
            DateTime lastStartedTime = captureStarted;

            //Getting channel list
            ChannelInfo[] channelInfoArray=recordInfo.GetSortedChannels();
            ChannelInfo currentChannel=channelInfoArray[channelIdx];

            //Update capture history
            channelHistory.GetChannelHistoryInfo(currentChannel.number).recordingsAttempted+=1;
            channelHistory.GetChannelHistoryInfo(currentChannel.number).lastAttempt=DateTime.Now;

            //Build ffmpeg capture command line with first channel and get things rolling
            string outputPath=BuildOutputPath(recordInfo.fileName+currentFileNum);
            string cmdLineArgs=BuildCaptureCmdLineArgs(currentChannel.number,hashValue,outputPath);
            logWriter.WriteLine($"=========================================");
            logWriter.WriteLine($"{DateTime.Now}: Starting {captureStarted} on channel {currentChannel.number}.  Expect to be done by {captureTargetEnd}.");
            logWriter.WriteLine($"                      {configuration["ffmpegPath"]} {cmdLineArgs}");
            Process p = ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,recordInfo.GetDuration(),outputPath);  
            logWriter.WriteLine($"{DateTime.Now}: Exited Capture.  Exit Code: {p.ExitCode}");

            //
            //retry loop if we're not done yet
            //
            int numRetries=Convert.ToInt32(configuration["numberOfRetries"]);
            for(int retryNum=0;DateTime.Now<captureTargetEnd && retryNum<numRetries;retryNum++)
            {           
                logWriter.WriteLine($"{DateTime.Now}: Capture Failed for channel {currentChannel.number}. Retry {retryNum+1} of {configuration["numberOfRetries"]}");

                //Update channel history
                channelHistory.GetChannelHistoryInfo(currentChannel.number).errors+=1;

                //increment failure count and file number
                currentChannelFailureCount++;
                currentFileNum++;

                //Got to next channel if channel has been alive for less than 15 minutes
                TimeSpan fifteenMin=new TimeSpan(0,15,0);
                if((DateTime.Now-lastStartedTime) < fifteenMin && !recordInfo.bestChannelSetFlag)
                {
                    //Set quality ratio for current channel
                    double minutes = (DateTime.Now-lastStartedTime).TotalMinutes;
                    double qualityRatio=minutes/currentChannelFailureCount;
                    currentChannel.ratio=qualityRatio;
                    logWriter.WriteLine($"{DateTime.Now}: Setting quality ratio {qualityRatio} for channel {currentChannel.number}");

                    //Determine correct next channel based on number and quality
                    if(!recordInfo.bestChannelSetFlag)
                    {
                        channelIdx=GetNextChannel(logWriter,recordInfo,channelInfoArray,channelIdx);
                        currentChannel=channelInfoArray[channelIdx];
                    }

                    currentChannelFailureCount=0;
                }

                //Set new started time and calc new timer     
                TimeSpan timeJustRecorded=DateTime.Now-lastStartedTime;
                lastStartedTime = DateTime.Now;
                TimeSpan timeLeft=captureTargetEnd-DateTime.Now;

                //Update capture history
                channelHistory.GetChannelHistoryInfo(currentChannel.number).hoursRecorded+=timeJustRecorded.TotalHours;
                channelHistory.GetChannelHistoryInfo(currentChannel.number).recordingsAttempted+=1;
                channelHistory.GetChannelHistoryInfo(currentChannel.number).lastAttempt=DateTime.Now;
                channelHistory.Save();

                //Now get things setup and going again
                outputPath=BuildOutputPath(recordInfo.fileName+currentFileNum);
                cmdLineArgs=BuildCaptureCmdLineArgs(currentChannel.number,hashValue,outputPath);
                logWriter.WriteLine($"{DateTime.Now}: Starting Capture (again) on channel {currentChannel.number}");
                logWriter.WriteLine($"                      {configuration["ffmpegPath"]} {cmdLineArgs}");
                p = ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,(int)timeLeft.TotalMinutes+1,outputPath);
            }
            logWriter.WriteLine($"{DateTime.Now}: Finished Capturing Stream.");

            //Update capture history
            TimeSpan finalTimeJustRecorded=DateTime.Now-lastStartedTime;
            channelHistory.GetChannelHistoryInfo(currentChannel.number).hoursRecorded+=finalTimeJustRecorded.TotalHours;
            channelHistory.GetChannelHistoryInfo(currentChannel.number).lastSuccess=DateTime.Now;
            channelHistory.Save();

            return currentFileNum;
        }

        private string BuildOutputPath(string fileName)
        {
            string outputPath = Path.Combine(configuration["outputPath"],fileName+".ts");

            //Make sure file does not already exist.  If so, rename it.
            if(File.Exists(outputPath))
            {
                string newFileName=Path.Combine(configuration["outputPath"],fileName+"_"+Path.GetRandomFileName()+".ts");
                File.Move(outputPath,newFileName);
            }

            return outputPath;      
        }

        private string BuildCaptureCmdLineArgs(string channel,string hashValue,string outputPath)
        {
            //C:\Users\mark\Desktop\ffmpeg\bin\ffmpeg -i "http://dnaw1.smoothstreams.tv:9100/view247/ch01q1.stream/playlist.m3u8?wmsAuthSign=c2VydmVyX3RpbWU9MTIvMS8yMDE2IDM6NDA6MTcgUE0maGFzaF92YWx1ZT1xVGxaZmlzMkNYd0hFTEJlaTlzVVJ3PT0mdmFsaWRtaW51dGVzPTcyMCZpZD00MzM=" -c copy t.ts
            
            string cmdLineArgs = configuration["captureCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputPath);
            cmdLineArgs=cmdLineArgs.Replace("[CHANNEL]",channel);
            cmdLineArgs=cmdLineArgs.Replace("[AUTHTOKEN]",hashValue);

            return cmdLineArgs;
        }        

        private int GetNextChannel(TextWriter logWriter,RecordInfo recordInfo,ChannelInfo[] channelInfoArray,int channelIdx)
        {   
            //Oportunistically get next channel
            channelIdx++;

            if(channelIdx < channelInfoArray.Length)  
            {
                //do we still have more channels?  If so, grab the next one
                logWriter.WriteLine($"{DateTime.Now}: Switching to channel {channelInfoArray[channelIdx].number}");
            }
            else
            {
                //grab best channel by grabbing the best ratio  
                double ratio=0;
                for(int b=0;b<channelInfoArray.Length;b++)
                {
                    if(channelInfoArray[b].ratio>=ratio)
                    {
                        ratio=channelInfoArray[b].ratio;
                        channelIdx=b;         
                        recordInfo.bestChannelSetFlag=true;
                    }
                }
                logWriter.WriteLine($"{DateTime.Now}: Now setting channel to {channelInfoArray[channelIdx].number} with quality ratio of {channelInfoArray[channelIdx].ratio} for the rest of the capture session");
            }

            return channelIdx;
        }

        private Process ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs)
        {
            return ExecProcess(logWriter,exe,cmdLineArgs,0,null);
        }

        private Process ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs,int timeout,string outputPath)
        {
            //Create our process
            var processInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = cmdLineArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process process = Process.Start(processInfo);

            //Let's build a timer to kill the process when done
            Timer captureTimer=null;
            CaptureProcessInfo captureProcessInfo = null;
            if(timeout>0)
            {
                //create capture process info
                DateTime timerDone=DateTime.Now.AddMinutes(timeout);
                captureProcessInfo = new CaptureProcessInfo(process,90000000,timerDone,outputPath,logWriter);
                //2000000

                //create timer
                TimeSpan intervalTime = new TimeSpan(0, 0, 10); 
                logWriter.WriteLine($"{DateTime.Now}: Settting Timer for {timeout} minutes in the future to kill process.");
                captureTimer = new Timer(OnCaptureTimer, captureProcessInfo, intervalTime, intervalTime);
            }

            //Now, let's wait for the thing to exit
            logWriter.WriteLine(process.StandardError.ReadToEnd());
            logWriter.WriteLine(process.StandardOutput.ReadToEnd());
            process.WaitForExit();

            //Clean up timer
            if(timeout>0 && captureTimer != null)
                captureTimer.Dispose();

            return process;
        }

        //Handler for ffmpeg timer to kill the process
        static void OnCaptureTimer(object obj)
        {    
            bool killProcess=false;
            CaptureProcessInfo captureProcessInfo = obj as CaptureProcessInfo;

            //Are we done?
            if(DateTime.Now >= captureProcessInfo.timerDone)
            {
                killProcess=true;
                captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: Timer is up.  Killing capture process");
            }

            //Make sure file is still growing at a reasonable pace.  Otherwise, kill the process
            if(!killProcess)
            {
                //Grab file info
                FileInfo fileInfo=new FileInfo(captureProcessInfo.outputPath);

                //Make sure file even exists!
                if(!fileInfo.Exists)
                {
                    killProcess=true;
                    captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: ERROR: File {captureProcessInfo.outputPath} doesn't exist.  Feed is bad.");
                }
                else
                {
                    //Make sure file size (rate) is fine
                    long fileSize = fileInfo.Length;
                    if(fileSize <= (captureProcessInfo.fileSize + captureProcessInfo.acceptableRate))
                    {
                        killProcess=true;
                        captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: ERROR: File size no longer growing. (Rate: {(fileSize-captureProcessInfo.fileSize)/10} bytes/s)  Killing capture process.");
                    }
                    captureProcessInfo.fileSize=fileSize;
                }
            }

            //Kill process if needed
            Process p = captureProcessInfo.process;
            if(killProcess && p!=null && !p.HasExited)
            {
                p.Kill();
                p.WaitForExit();
            }
        }

        private void FixUp(TextWriter logWriter,int numFiles,RecordInfo recordInfo)
        {
            string cmdLineArgs;
            string outputFile;
            string videoFileName=recordInfo.fileName+"0.ts";
            string ffmpegPath = configuration["ffmpegPath"];
            string outputPath = configuration["outputPath"];
            string nasPath = configuration["nasPath"];

            //metadata
            string metadata;
            if(recordInfo.description!=null)
                metadata=recordInfo.description;
            else
                metadata="File Name: "+recordInfo.fileName;


            //Concat if more than one file
            logWriter.WriteLine($"{DateTime.Now}: Num Files: {(numFiles+1)}");
            if(numFiles > 0)
            {
                //make fileist
                string fileList = Path.Combine(outputPath,recordInfo.fileName+"0.ts");
                for(int i=1;i<=numFiles;i++)
                    fileList=fileList+"|"+Path.Combine(outputPath,recordInfo.fileName+i+".ts");

                //Create output file path
                outputFile=Path.Combine(outputPath,recordInfo.fileName+".ts");          

                //"concatCmdLine": "[FULLFFMPEGPATH] -i \"concat:[FILELIST]\" -c copy [FULLOUTPUTPATH]",
                cmdLineArgs = configuration["concatCmdLine"];
                cmdLineArgs=cmdLineArgs.Replace("[FILELIST]",fileList);
                cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputFile);

                //Run command
                logWriter.WriteLine($"{DateTime.Now}: Starting Concat: {configuration["ffmpegPath"]} {cmdLineArgs}");
                ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);

                videoFileName=recordInfo.fileName+".ts";
            }

            //Mux file to mp4 from ts (transport stream)
            string inputFile=Path.Combine(outputPath,videoFileName);
            outputFile=Path.Combine(outputPath,recordInfo.fileName+".mp4");

            //Make sure file doesn't already exist
            if(File.Exists(outputFile))
            {
                string newFileName=Path.Combine(outputPath,recordInfo.fileName+"_"+Path.GetRandomFileName()+".mp4");
                File.Move(outputFile,newFileName);                 
            }

            // "muxCmdLine": "[FULLFFMPEGPATH] -i [VIDEOFILE] -acodec copy -vcodec copy [FULLOUTPUTPATH]"
            cmdLineArgs = configuration["muxCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[VIDEOFILE]",inputFile);
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputFile);
            cmdLineArgs=cmdLineArgs.Replace("[DESCRIPTION]",recordInfo.description);            

            //Run command
            logWriter.WriteLine($"{DateTime.Now}: Starting Mux: {configuration["ffmpegPath"]} {cmdLineArgs}");
            ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);

            //If final file exist, delete old .ts file/s
            if(File.Exists(outputFile))
            {
                inputFile=Path.Combine(outputPath,videoFileName);
                logWriter.WriteLine($"{DateTime.Now}: Removing ts file: {inputFile}");
                File.Delete(inputFile);

                if(numFiles>0)
                {
                    for(int i=0;i<=numFiles;i++)
                    {
                        inputFile=Path.Combine(outputPath,recordInfo.fileName+i+".ts");
                        logWriter.WriteLine($"{DateTime.Now}: Removing ts file: {inputFile}");
                        File.Delete(inputFile);
                    }
                }
            }

            //If NAS path exists, move file mp4 file there
            if(nasPath != null)
            {
                string nasFile=Path.Combine(nasPath,recordInfo.fileName+".mp4");
                if(File.Exists(nasFile))
                {
                    string newFileName=Path.Combine(configuration["outputPath"],recordInfo.fileName+"_"+Path.GetRandomFileName()+".mp4");
                    File.Move(outputPath,newFileName);
                }

                logWriter.WriteLine($"{DateTime.Now}: Moving {outputFile} to {nasFile}");
                File.Move(outputFile,nasFile);
            }
        }
    }
}
