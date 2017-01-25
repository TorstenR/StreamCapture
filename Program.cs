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
            CommandOption keywords = commandLineApplication.Option("-k | --keywords","Keywords to search listing by - comma delimited",CommandOptionType.SingleValue);
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
                ParseSchedule(recInfoDict,keywords.Value()).Wait();

                //Spawn new process for each show found
                foreach (KeyValuePair<string, RecordInfo> kvp in recInfoDict)
                {            
                    RecordInfo recordInfo = (RecordInfo)kvp.Value;

                    //If show is not already in the past or waiting, get that done
                    int hoursInFuture=Convert.ToInt32(configuration["HoursInFuture"]);
                    if(recordInfo.GetStartDT()>DateTime.Now && recordInfo.GetStartDT()<=DateTime.Now.AddHours(8) && !recordInfo.processSpawnedFlag)
                    {
                        recordInfo.processSpawnedFlag=true;
                        DumpRecordInfo(Console.Out,recordInfo,"Schedule Read: "); 
        
                        Program p = new Program();
                        Task.Factory.StartNew(() => p.MainAsync(recordInfo,configuration));  //use threadpool instead of more costly os threads
                        //Thread newRecThread = new Thread(() => p.MainAsync(recordInfo,configuration).Wait());
                        //newRecThread.Start();
                    }
                    else
                    {
                        Console.WriteLine($"{DateTime.Now}: ignoring id: {recordInfo.id}");
                    }
                }  

                //Determine how long to sleep before next check
                string[] times=configuration["scheduleCheck"].Split(',');
                DateTime nextRecord=DateTime.Now;
                bool timeFound=false;
                for(int i=0;i<times.Length;i++)
                {
                    int recHour=Convert.ToInt32(times[i]);
                    if(DateTime.Now.Hour < recHour)
                    {
                        int hourDiff=recHour-DateTime.Now.TimeOfDay.Hours;
                        nextRecord=DateTime.Now.AddHours(hourDiff);
                        timeFound=true;
                        break;
                    }
                }

                //If nothing was found, just grab the first time
                if(!timeFound)
                {
                    int recHour=Convert.ToInt32(times[0]);  //grab first time

                    //Looks like we go to tommorrow since we're still here
                    nextRecord=new DateTime(
                        DateTime.Now.Year,
                        DateTime.Now.Month,
                        DateTime.Now.Day+1,
                        recHour,
                        0,
                        0,
                        0,
                        DateTime.Now.Kind);
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
            string hashValue = await Authenticate(configuration["user"],configuration["pass"]);

            //Capture stream
            int numFiles=CaptureStream(logWriter,hashValue,recordInfo,configuration);

            //Fixup output
            FixUp(logWriter,numFiles,recordInfo,configuration);

            //Cleanup
            logWriter.WriteLine($"Done Capturing - Cleaning up");
            logWriter.Dispose();
            fileHandle.Dispose();
        }

        private static async Task ParseSchedule(Dictionary<string,RecordInfo> recInfoDict,string keywords)
        {
            //List of record info
            //Dictionary<string,RecordInfo> recInfoDict = new Dictionary<string,RecordInfo>();

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
                    if(MatchKeywords(show["name"].ToString(),keywords))
                    {
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
                        recordInfo.fileName=recordInfo.id;
                        recordInfo.description=show["name"].ToString();
                        recordInfo.strStartDT=show["time"].ToString();
                        recordInfo.strEndDT=show["end_time"].ToString();
                        recordInfo.strDuration=show["runtime"].ToString();

                        //Update or add
                        if(recInfoDict.ContainsKey(keyValue))
                            recInfoDict[keyValue]=recordInfo;
                        else
                            recInfoDict.Add(keyValue,recordInfo);
                    }
                }
            }
        }

        static private bool MatchKeywords(string showName,string keywords)
        {
            bool matchFlag=false;

            string[] kArray = keywords.Split(',');
            for(int i=0;i<kArray.Length;i++)
            {
                if(showName.ToLower().Contains(kArray[i].ToLower()))
                    matchFlag=true;
            }

            return matchFlag;
        }

        private async Task<string> Authenticate(string user,string pass)
        {
            string hashValue=null;

            using (var client = new HttpClient())
            {
                //http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php?username=foo&password=bar&site=view247
                Uri uri = new Uri("http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php");
                client.BaseAddress = uri;
                var response = await client.GetAsync("?username="+user+"&password="+pass+"&site=view247");
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
            int currentChannelFailureCount=0;
            int lastChannelFailureTime = 0;

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(recordInfo.GetFirstChannel(),hashValue,recordInfo.fileName+currentFileNum,configuration);
            logWriter.WriteLine("Starting Capture: {0} {1}",configuration["ffmpegPath"],cmdLineArgs);
            Process p = ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,true);  //setting async flag

            //
            //Loop in case connection is flaky
            //
            for(int loopNum=0;loopNum<recordInfo.GetDuration();loopNum++)
            {
                //start process if not started already
                if(p==null || p.HasExited)
                {
                    logWriter.WriteLine(p.StandardError.ReadToEnd());
                    logWriter.WriteLine(p.StandardOutput.ReadToEnd());
                    logWriter.WriteLine("Capture Failed for channel {0} at minute {1}", recordInfo.GetCurrentChannel(),loopNum);

                    //increment failure count and file number
                    currentChannelFailureCount++;
                    currentFileNum++;

                    //Got to next channel if channel has been alive for less than 15 minutes
                    if((loopNum-lastChannelFailureTime)<=15)
                    {
                        //Set quality ratio for current channel
                        double qualityRatio=(loopNum-lastChannelFailureTime)/currentChannelFailureCount;
                        recordInfo.SetCurrentQualityRatio(qualityRatio);
                        logWriter.WriteLine("Setting quality ratio {0} for channel {1}", qualityRatio,recordInfo.GetCurrentChannel());

                        //Determine correct next channel based on number and quality
                        SetNextChannel(logWriter,recordInfo,loopNum);
                    }

                    //Now get things setup and going again
                    cmdLineArgs=BuildCaptureCmdLineArgs(recordInfo.GetCurrentChannel(),hashValue,recordInfo.fileName+currentFileNum,configuration);
                    logWriter.WriteLine("Starting Capture (again): {0} {1}",configuration["ffmpegPath"],cmdLineArgs);
                    p = ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs,true);

                    //Set the time we last failed a channel                        
                    lastChannelFailureTime = loopNum;  
                }

                //Wait
                TimeSpan interval = new TimeSpan(0, 1, 0);
                Thread.Sleep(interval);
            }
            logWriter.WriteLine($"Clearing stream and killing process now, time is up");

            // Free resources associated with process.
            if(p!=null && !p.HasExited)
            {
                p.Kill();
                logWriter.WriteLine(p.StandardError.ReadToEnd());
                logWriter.WriteLine(p.StandardOutput.ReadToEnd());            
                p.WaitForExit();
            }

            return currentFileNum;
        }

        private void SetNextChannel(TextWriter logWriter,RecordInfo recordInfo,int loopNum)
        {
            //Return if we've already selected the best channel
            if(recordInfo.bestChannelSetFlag)
                return;

            //opportunistically increment
            recordInfo.currentChannelIdx++;                

            if(recordInfo.currentChannelIdx < recordInfo.GetNumberOfChannels())  
            {
                //do we still have more channels?  If so, grab the next one
                logWriter.WriteLine("Switching to channel {0}", recordInfo.GetCurrentChannel());
            }
            else
            {
                string[] channels = recordInfo.GetChannels();

                //grab best channel by grabbing the best ratio  
                double ratio=0;
                for(int b=0;b<channels.Length;b++)
                {
                    if(recordInfo.GetQualityRatio(b)>ratio)
                    {
                        ratio=recordInfo.GetQualityRatio(b);
                        recordInfo.currentChannelIdx=b;
                        recordInfo.bestChannelSetFlag=true;                    
                    }

                    logWriter.WriteLine("Now setting channel to {0} with quality ratio of {1} for the rest of the capture session",recordInfo.GetCurrentChannel(),recordInfo.GetCurrentQualityRatio());
                }
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
            return ExecProcess(logWriter,exe,cmdLineArgs,false);
        }

        private Process ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs,bool asyncFlag)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = cmdLineArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process process = Process.Start(processInfo);
            if(!asyncFlag)
            {
                logWriter.WriteLine(process.StandardError.ReadToEnd());
                logWriter.WriteLine(process.StandardOutput.ReadToEnd());
                process.WaitForExit();
            }

            return process;
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
            logWriter.WriteLine("Num Files: {0}", numFiles+1);
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
                logWriter.WriteLine("Starting Concat:");
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
            logWriter.WriteLine("Starting Mux:");
            ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);

            //If final file exist, delete old .ts file
            if(File.Exists(outputFile))
            {
                logWriter.WriteLine("Removing ts file: {0}",inputFile);
                File.Delete(inputFile);
            }
        }
    }

    public class RecordInfo
    {
        public string strDuration { get; set; }
        public string fileName { get; set; }
        public string strStartDT { get; set; }
        public string strEndDT { get; set; }

        public string id { get; set; }
        public int duration { get; }
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
        }

        public int GetDuration()
        {
            return Convert.ToInt32(strDuration);
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

            DateTime startDT=DateTime.Parse(strStartDT);
            return startDT.AddHours(-3);
        }

        public DateTime GetEndDT()
        {
            if(strEndDT == null)
                return DateTime.Now;

            DateTime endDT=DateTime.Parse(strEndDT);
            return endDT.AddHours(-3);
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
