using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.CommandLineUtils;


namespace StreamCapture
{
    
    public class Program
    {
        public static void Main(string[] args)
        {
            CommandLineApplication commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: false);
            CommandOption channels = commandLineApplication.Option("-c | --channels","Channels to record in the format nn+nn+nn (must be 2 digits)",CommandOptionType.SingleValue);
            CommandOption duration = commandLineApplication.Option("-d | --duration","Duration in minutes to record",CommandOptionType.SingleValue);
            CommandOption filename = commandLineApplication.Option("-f | --filename","File name (no extension)",CommandOptionType.SingleValue);
            CommandOption datetime = commandLineApplication.Option("-d | --datetime","Datetime MM/DD/YY HH:MM (optional)",CommandOptionType.SingleValue);
            CommandOption keywords = commandLineApplication.Option("-k | --keywords","Keywords to search listing by - comma delimited. "+
                "Optionally pre and post minutes can be added.  Example: LiverPool,NFL|5|30,Chelsea starts NFL 5 minuts early and ends 30 minutes late.",CommandOptionType.SingleValue);
            commandLineApplication.HelpOption("-? | -h | --help");
            commandLineApplication.Execute(args);       

            if(!channels.HasValue() || !duration.HasValue() || !filename.HasValue())
            {
                if(!keywords.HasValue())
                {
                    Console.WriteLine($"{DateTime.Now}: Incorrect command line options.  Please run with --help for more information.");
                    Environment.Exit(1);
                }                
            } 

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();

            //If we're not reading from the schedule, just go now
            if(!keywords.HasValue())
            {
                //Create new RecordInfo
                RecordInfo recordInfo = new RecordInfo();
                if(channels.HasValue())
                    recordInfo.LoadChannels(channels.Value());
                recordInfo.strDuration=duration.Value();
                recordInfo.strStartDT=datetime.Value();
                recordInfo.fileName=filename.Value();

                //Start recording
                Program p = new Program();
                p.MainAsync(recordInfo,configuration).Wait();
                Environment.Exit(0);
            }

            //Since keyword passed in, grab schedule from interwebs and loop forever, checking every 6 hours for new shows to record
            Dictionary<string,RecordInfo> recInfoDict = new Dictionary<string,RecordInfo>();
            while(true)
            {
                //Get latest schedule
                ParseSchedule(recInfoDict,keywords.Value(),configuration).Wait();

                //Spawn new process for each show found
                foreach (KeyValuePair<string, RecordInfo> kvp in recInfoDict)
                {            
                    RecordInfo recordInfo = (RecordInfo)kvp.Value;

                    //If show is not already in the past or waiting, get that done
                    int hoursInFuture=Convert.ToInt32(configuration["hoursInFuture"]);
                    if(recordInfo.GetStartDT()>DateTime.Now && recordInfo.GetStartDT()<=DateTime.Now.AddHours(hoursInFuture) && !recordInfo.processSpawnedFlag)
                    {
                        recordInfo.processSpawnedFlag=true;
                        DumpRecordInfo(Console.Out,recordInfo,"Schedule Read: "); 
        
                        Program p = new Program();
                        Task.Factory.StartNew(() => p.MainAsync(recordInfo,configuration));  //use threadpool instead of more costly os threads
                    }
                    else
                    {
                        Console.WriteLine($"{DateTime.Now}: ignoring id: {recordInfo.id}");
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
                    //build for tomorrow
                    int recHour=Convert.ToInt32(times[0]);  //grab first time
                    nextRecord=new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day+1,recHour,0,0,0,DateTime.Now.Kind);
                }

                //Wait
                TimeSpan timeToWait = nextRecord - DateTime.Now;
                Console.WriteLine($"{DateTime.Now}: Now sleeping for {timeToWait.Hours} hours before checking again at {nextRecord.ToString()}");
                Thread.Sleep(timeToWait);         
                Console.WriteLine($"{DateTime.Now}: Woke up, now checking again...");
            } 
        }

        private static void DumpRecordInfo(TextWriter logWriter,RecordInfo recordInfo,string tag)
        {
            logWriter.WriteLine($"{tag} =====================");
            logWriter.WriteLine($"Show: {recordInfo.description} StartDT: {recordInfo.GetStartDT()}  Duration: {recordInfo.GetDuration()}");
            logWriter.WriteLine($"File: {recordInfo.fileName}");
            logWriter.WriteLine($"Channels: {recordInfo.GetChannelString()}");               
        }

        async Task MainAsync(RecordInfo recordInfo,IConfiguration configuration)
        {
            //Write to our very own log as there might be other captures going too
            string logPath=Path.Combine(configuration["logPath"],recordInfo.fileName+"Log.txt");
            FileStream fileHandle = new FileStream (logPath, FileMode.OpenOrCreate, FileAccess.Write);
            StreamWriter logWriter = new StreamWriter (fileHandle);
            logWriter.AutoFlush = true;

            //Dump
            DumpRecordInfo(logWriter,recordInfo,"MainAsync");

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
            string hashValue = await Authenticate(configuration);

            //Capture stream
            int numFiles=CaptureStream(logWriter,hashValue,recordInfo,configuration);

            //Fixup output
            FixUp(logWriter,numFiles,recordInfo,configuration);

            //Cleanup
            logWriter.WriteLine($"{DateTime.Now}: Done Capturing - Cleaning up");
            logWriter.Dispose();
            fileHandle.Dispose();
        }

        private static async Task ParseSchedule(Dictionary<string,RecordInfo> recInfoDict,string keywords,IConfiguration configuration)
        {
            string schedString;
            using (var client = new HttpClient())
            {
                Uri uri = new Uri("https://iptvguide.netlify.com/iptv.json");
                var response = await client.GetAsync(uri);
                response.EnsureSuccessStatusCode(); // Throw in not success
                schedString = await response.Content.ReadAsStringAsync();
            }

            JObject jsonObject = JObject.Parse(schedString);
            IEnumerable<JToken> channels = jsonObject.SelectTokens("$..items");
            foreach(JToken channelInfo in channels)
            {
                IEnumerable<JToken> channelContent = channelInfo.Children();
                foreach(JToken show in channelContent)
                {
                    string keyValue=show["name"].ToString()+show["time"].ToString();
                    string matchKeyword=MatchKeywords(show["name"].ToString(),keywords);
                    if(matchKeyword!=null)
                    {              
                        //Build record info 
                        RecordInfo recordInfo;
                        if(recInfoDict.ContainsKey(keyValue))
                            recordInfo=recInfoDict[keyValue];
                        else
                            recordInfo = new RecordInfo();

                        if(show["quality"].ToString().Contains("720"))                      
                            recordInfo.AddChannelAtBeginning(show["channel"].ToString(),show["quality"].ToString());
                        else
                            recordInfo.AddChannelAtEnd(show["channel"].ToString(),show["quality"].ToString());   
                        recordInfo.id=show["id"].ToString();
                        recordInfo.description=show["name"].ToString();
                        recordInfo.strStartDT=show["time"].ToString();
                        recordInfo.strEndDT=show["end_time"].ToString();
                        recordInfo.strDuration=show["runtime"].ToString();
                        recordInfo.strDTOffset=configuration["schedTimeOffset"];

                        //Get pre and post times if available
                        string[] kPartsArray = matchKeyword.Split('|');
                        if(kPartsArray.Length>1)
                        {
                            recordInfo.strPreMinutes=kPartsArray[1];
                            recordInfo.strPostMinutes=kPartsArray[2];
                        }

                        //Clean up description, and then use as filename
                        recordInfo.fileName=show["name"].ToString()+show["id"].ToString();
                        string myChars=@"|'/\ ,<>#@!+_-&^*()~`";
                        string invalidChars = myChars + new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                        foreach(char c in invalidChars)
                        {
                            recordInfo.fileName = recordInfo.fileName.Replace(c.ToString(),"");
                        }

                        //Update or add
                        if(recInfoDict.ContainsKey(keyValue))
                            recInfoDict[keyValue]=recordInfo;
                        else
                            recInfoDict.Add(keyValue,recordInfo);
                    }
                }
            }
        }

        static private string MatchKeywords(string showName,string keywords)
        {
            string matchKeyword=null;

            string[] kArray = keywords.Split(',');
            for(int i=0;i<kArray.Length;i++)
            {
                string[] kPartsArray = kArray[i].Split('|');
                if(showName.ToLower().Contains(kPartsArray[0].ToLower()))
                    matchKeyword=kArray[i];
            }

            return matchKeyword;
        }

        private async Task<string> Authenticate(IConfiguration configuration)
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
     
        private int CaptureStream(TextWriter logWriter,string hashValue,RecordInfo recordInfo,IConfiguration configuration)
        {
            int currentFileNum = 0;
            int currentChannelFailureCount = 0;

            //Marking time we started and when we should be done
            DateTime captureStarted = DateTime.Now;
            DateTime captureTargetEnd = captureStarted.AddMinutes(recordInfo.GetDuration());
            DateTime lastStartedTime = captureStarted;

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(recordInfo.GetFirstChannel(),hashValue,recordInfo.fileName+currentFileNum,configuration);
            logWriter.WriteLine($"{DateTime.Now}: Starting {captureStarted} and expect to be done {captureTargetEnd}.  Cmd: {configuration["ffmpegPath"]} {cmdLineArgs}");
            Process p = ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,recordInfo.GetDuration());  //setting async flag
            logWriter.WriteLine($"{DateTime.Now}: After execution.  Exit Code: {p.ExitCode}");

            //
            //retry loop if we're not done yet
            //
            int numRetries=Convert.ToInt32(configuration["numberOfRetries"]);
            for(int retryNum=0;DateTime.Now<captureTargetEnd && retryNum<numRetries;retryNum++)
            {           
                logWriter.WriteLine($"{DateTime.Now}: Capture Failed for channel {recordInfo.GetCurrentChannel()}. Last failure {lastStartedTime}  Retry {retryNum+1} of {configuration["numberOfRetries"]}");

                //increment failure count and file number
                currentChannelFailureCount++;
                currentFileNum++;

                //Got to next channel if channel has been alive for less than 15 minutes
                TimeSpan fifteenMin=new TimeSpan(0,15,0);
                if((DateTime.Now-lastStartedTime) < fifteenMin && !recordInfo.bestChannelSetFlag)
                {
                    //Set quality ratio for current channel
                    int minutes = (DateTime.Now-lastStartedTime).Minutes;
                    double qualityRatio=minutes/currentChannelFailureCount;
                    recordInfo.SetCurrentQualityRatio(qualityRatio);
                    logWriter.WriteLine($"{DateTime.Now}: Setting quality ratio {qualityRatio} for channel {recordInfo.GetCurrentChannel()}");

                    //Determine correct next channel based on number and quality
                    SetNextChannel(logWriter,recordInfo);
                    currentChannelFailureCount=0;
                }

                //Set new started time and calc new timer                  
                lastStartedTime = DateTime.Now;
                TimeSpan timeLeft=captureTargetEnd-DateTime.Now;

                //Now get things setup and going again
                cmdLineArgs=BuildCaptureCmdLineArgs(recordInfo.GetCurrentChannel(),hashValue,recordInfo.fileName+currentFileNum,configuration);
                logWriter.WriteLine($"{DateTime.Now}: Starting Capture (again): {configuration["ffmpegPath"]} {cmdLineArgs}");
                p = ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,(int)timeLeft.TotalMinutes+1);
            }
            logWriter.WriteLine($"{DateTime.Now}: Finished Capturing Stream.");

            return currentFileNum;
        }

        private void SetNextChannel(TextWriter logWriter,RecordInfo recordInfo)
        {
            //Return if we've already selected the best channel
            if(recordInfo.bestChannelSetFlag)
                return;

            //opportunistically increment
            recordInfo.currentChannelIdx++;                

            if(recordInfo.currentChannelIdx < recordInfo.GetNumberOfChannels())  
            {
                //do we still have more channels?  If so, grab the next one
                logWriter.WriteLine($"{DateTime.Now}: Switching to channel {recordInfo.GetCurrentChannel()}");
            }
            else
            {
                string[] channels = recordInfo.GetChannels();

                //grab best channel by grabbing the best ratio  
                double ratio=0;
                for(int b=0;b<channels.Length;b++)
                {
                    if(recordInfo.GetQualityRatio(b)>=ratio)
                    {
                        ratio=recordInfo.GetQualityRatio(b);
                        recordInfo.currentChannelIdx=b;
                        recordInfo.bestChannelSetFlag=true;                    
                    }
                }
                logWriter.WriteLine($"{DateTime.Now}: Now setting channel to {recordInfo.GetCurrentChannel()} with quality ratio of {recordInfo.GetCurrentQualityRatio()} for the rest of the capture session");
            }
        }

        private string BuildCaptureCmdLineArgs(string channel,string hashValue,string fileName,IConfiguration configuration)
        {
            //C:\Users\mark\Desktop\ffmpeg\bin\ffmpeg -i "http://dnaw1.smoothstreams.tv:9100/view247/ch01q1.stream/playlist.m3u8?wmsAuthSign=c2VydmVyX3RpbWU9MTIvMS8yMDE2IDM6NDA6MTcgUE0maGFzaF92YWx1ZT1xVGxaZmlzMkNYd0hFTEJlaTlzVVJ3PT0mdmFsaWRtaW51dGVzPTcyMCZpZD00MzM=" -c copy t.ts
            
            string cmdLineArgs = configuration["captureCmdLine"];
            string outputPath = Path.Combine(configuration["outputPath"],fileName+".ts");

            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputPath);
            cmdLineArgs=cmdLineArgs.Replace("[CHANNEL]",channel);
            cmdLineArgs=cmdLineArgs.Replace("[AUTHTOKEN]",hashValue);
            
            //Console.WriteLine($"Cmd Line Args:  {cmdLineArgs}");

            return cmdLineArgs;
        }

        private Process ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs)
        {
            return ExecProcess(logWriter,exe,cmdLineArgs,0);
        }

        private Process ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs,int timeout)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = cmdLineArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Timer captureTimer=null;
            Process process = Process.Start(processInfo);

            //Let's build a timer to kill the process when done
            if(timeout>0)
            {
                TimeSpan delayTime = new TimeSpan(0, timeout, 0);
                TimeSpan intervalTime = new TimeSpan(0, 0, 0, 0, -1); //turn off interval timer
                logWriter.WriteLine($"{DateTime.Now}: Settting Timer for {timeout} minutes in the future to kill process.");
                captureTimer = new Timer(OnCaptureTimer, process, delayTime, intervalTime);
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
            Process p = obj as Process;
            if(p!=null && !p.HasExited)
            {
                p.Kill();
                p.WaitForExit();
            }                
        }

        private void FixUp(TextWriter logWriter,int numFiles,RecordInfo recordInfo,IConfiguration configuration)
        {
            string cmdLineArgs;
            string outputFile;
            string videoFileName=recordInfo.fileName+"0.ts";
            string ffmpegPath = configuration["ffmpegPath"];
            string outputPath = configuration["outputPath"];

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

                for(int i=0;i<=numFiles;i++)
                {
                    inputFile=Path.Combine(outputPath,recordInfo.fileName+i+".ts");
                    logWriter.WriteLine($"{DateTime.Now}: Removing ts file: {inputFile}");
                    File.Delete(inputFile);
                }
            }
        }
    }

    public class RecordInfo
    {
        public string strDuration { get; set; }
        public string fileName { get; set; }
        public string strStartDT { get; set; }
        public string strEndDT { get; set; }
        public string strDTOffset  { get; set; }
        public string strPreMinutes { get; set; }
        public string strPostMinutes { get; set; }

        public string id { get; set; }
        public DateTime startDT { get; set; }
        public string description { get; set; }
        public string keyword { get; set; }
        public int currentChannelIdx { get; set; }
        public bool bestChannelSetFlag { get; set; }
        public bool processSpawnedFlag  { get; set; }

        private List<string> channelList;
        private List<double> channelRatioList;
        private List<string> channelAnnotatedList;

        public RecordInfo()
        {
            channelList = new List<string>();  
            channelRatioList = new List<double>();
            channelAnnotatedList = new List<string>();

            //Init certain properties that might be overwritten later
            id=DateTime.Now.Ticks.ToString();

            currentChannelIdx=0;
            bestChannelSetFlag=false;
            processSpawnedFlag=false;
            strPreMinutes="0";
            strPostMinutes="0";
            strDTOffset="0";
        }

        public int GetDuration()
        {
            int duration = Convert.ToInt32(strDuration);
            duration = duration + Convert.ToInt32(strPreMinutes) + Convert.ToInt32(strPostMinutes);
            return duration;
        }

        public void LoadChannels(string strChannels)
        {
            string[] channelArray = strChannels.Split('+');
            channelList = new List<string>(channelArray);     
            channelAnnotatedList = new List<string>(channelArray);  
        }

        public string GetChannelString()
        {
            string channelStr="";

            foreach(string channel in channelAnnotatedList)
                channelStr=channelStr+channel+" ";

            return channelStr;
        }

        public string GetChannelArgs()
        {
            string channelStr="";

            foreach(string channel in channelList)
                channelStr=channelStr+channel+"+";
            channelStr=channelStr.Trim('+');

            return channelStr;
        }

        public string GetFirstChannel()
        {
            return channelList.First<string>();
        }

        public DateTime GetStartDT()
        {
            if(strStartDT == null)
                return DateTime.Now;

            //Create base date time
            DateTime startDT=DateTime.Parse(strStartDT);

            //subtract pre time if appropriate
            int preMin = Convert.ToInt32(strPreMinutes)*-1;
            startDT=startDT.AddMinutes(preMin);

            //Add offset 
            int timeOffset=Convert.ToInt32(strDTOffset);
            startDT=startDT.AddHours(timeOffset);

            return startDT;
        }

        public string GetCurrentChannel()
        {
            return channelList[currentChannelIdx];
        }

        public string GetChannel(int channelIdx)
        {
            return channelList[channelIdx];
        }

        public string[] GetChannels()
        {
            return channelList.ToArray<string>();
        }

        public void AddChannelAtEnd(string channel,string qualityTag)
        {
            if(channel.Length == 1)
                channel="0"+channel;
            channelList.Add(channel);
            channelAnnotatedList.Add(channel+" ("+qualityTag+")");
        }

        public void AddChannelAtBeginning(string channel,string qualityTag)
        {
            if(channel.Length == 1)
                channel="0"+channel;
            channelList.Insert(0,channel);
            channelAnnotatedList.Insert(0,channel+" ("+qualityTag+")");
        }

        public int GetNumberOfChannels()
        {
            return channelList.Count;
        }

        public void SetCurrentQualityRatio(double ratio)
        {
            channelRatioList.Insert(currentChannelIdx,ratio);
        }

        public double GetCurrentQualityRatio()
        {
            return channelRatioList[currentChannelIdx];
        }

        public double GetQualityRatio(int channelIdx)
        {
            return channelRatioList[channelIdx];
        }
    }
}
