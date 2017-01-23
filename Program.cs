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
            CommandOption keyword = commandLineApplication.Option("-k | --keyword","Keyword to search listing by (all that's required)",CommandOptionType.SingleValue);
            commandLineApplication.HelpOption("-? | -h | --help");
            commandLineApplication.Execute(args);       

            if(!channels.HasValue() || !duration.HasValue() || !filename.HasValue())
            {
                if(!keyword.HasValue())
                {
                    Console.WriteLine($"{DateTime.Now}: Incorrect command line options.  Please run with --help for more information.");
                    Environment.Exit(1);
                }                
            } 

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();

            //If we're not reading from the schedule, just start waiting for recording now
            if(!keyword.HasValue())
            {
                //Create new RecordInfo
                RecordInfo recordInfo = new RecordInfo();
                if(channels.HasValue())
                    recordInfo.LoadChannels(channels.Value());
                recordInfo.strDuration=duration.Value();
                recordInfo.strStartDT=datetime.Value();
                recordInfo.keyword=keyword.Value();
                recordInfo.fileName=filename.Value();

                //Dump
                DumpRecordInfo(recordInfo);

                //Start recording
                Program p = new Program();
                p.MainAsync(recordInfo,configuration).Wait();
                Environment.Exit(0);
            }

            //Since keyword passed in, grab schedule from interwebs and loop forever, checking every 6 hours for new shows to record
            while(true)
            {
                //Get latest schedule
                Task<Dictionary<string,RecordInfo>> parseTask = ParseSchedule(keyword.Value());
                Dictionary<string,RecordInfo> recInfoDict = parseTask.Result;

                //Spawn new process for each show found
                foreach (KeyValuePair<string, RecordInfo> kvp in recInfoDict)
                {            
                    RecordInfo recordInfo = (RecordInfo)kvp.Value;
                    recordInfo.processSpawnedFlag=true;
                    DumpRecordInfo(recordInfo); 
                    SpawnRecordProcess(recordInfo);
                }  

                //Wait
                Console.WriteLine($"{DateTime.Now}: Now sleeping for 6 hours before checking again...");
                TimeSpan interval = new TimeSpan(0, 1, 0);
                Thread.Sleep(interval);         
                Console.WriteLine($"{DateTime.Now}: Woke up, now checking again...");
            } 
        }

        private static void DumpRecordInfo(RecordInfo recordInfo)
        {
            Console.WriteLine($"=====================");
            Console.WriteLine($"Show: {recordInfo.description} StartDT: {recordInfo.GetStartDT()}  Duration: {recordInfo.GetDuration()}");
            Console.WriteLine($"File: {recordInfo.fileName}");
            Console.WriteLine($"Channels: {recordInfo.GetChannelString()}");               
        }

        private static void SpawnRecordProcess(RecordInfo recordInfo)
        {
            //Build command line args
            string args = String.Format($"--channels={recordInfo.GetChannelArgs()} --duration={recordInfo.duration} --datetime={recordInfo.GetStartDT().ToString("MM/dd/yyyy")} --filename={recordInfo.fileName}");
            Console.WriteLine($"{DateTime.Now}: Starting new streamCapture.exe with args: {args}");

            //Process.Start(new ProcessStartInfo("cmd", $"foo.txt"));

            ProcessStartInfo info = new ProcessStartInfo("cmd.exe", "foo.txt"); 
            //info.UseShellExecute = false;  
            info.RedirectStandardOutput = true;
            Process processChild = Process.Start(info); // separate window

            //ProcessStartInfo info = new ProcessStartInfo("cmd.exe", "foo.txt"); 
            //ProcessStartInfo info = new ProcessStartInfo("streamCapture.exe", args);
            //info.UseShellExecute = false;  
            //info.RedirectStandardOutput = true;
            //var recProcess = new Process {StartInfo = info};
            //recProcess.Start();
        }

        async Task MainAsync(RecordInfo recordInfo,IConfiguration configuration)
        {       
            //Wait here until we're ready to start recording
            if(recordInfo.strStartDT != null)
            {
                DateTime recStart = recordInfo.GetStartDT();
                TimeSpan timeToWait = recStart - DateTime.Now;
                Console.WriteLine($"{DateTime.Now}: Starting recording at {recStart} - Waiting for {timeToWait.Days} Days, {timeToWait.Hours} Hours, and {timeToWait.Minutes} minutes.");
                if(timeToWait.Seconds>0)
                    Thread.Sleep(timeToWait);
            }       

            //Authenticate
            string hashValue = await Authenticate(configuration["user"],configuration["pass"]);

            //Capture stream
            int numFiles=CaptureStream(hashValue,recordInfo,configuration);

            //Fixup output
            FixUp(numFiles,recordInfo,configuration);
        }

        private static async Task<Dictionary<string,RecordInfo>> ParseSchedule(string keyword)
        {
            //List of record info
            Dictionary<string,RecordInfo> recInfoDict = new Dictionary<string,RecordInfo>();

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
                    string name=show["name"].ToString();
                    if(name.Contains(keyword))
                    {
                        RecordInfo recordInfo = new RecordInfo();

                        //Check in list
                        if(recInfoDict.ContainsKey(name))
                            recordInfo=recInfoDict[name];

                        if(show["quality"].ToString().Contains("720"))                      
                            recordInfo.AddChannelAtBeginning(show["channel"].ToString(),show["quality"].ToString());
                        else
                            recordInfo.AddChannelAtEnd(show["channel"].ToString(),show["quality"].ToString());
                        
                        recordInfo.id=show["id"].ToString();
                        recordInfo.fileName=recordInfo.id;
                        recordInfo.description=show["name"].ToString();
                        recordInfo.strStartDT=show["time"].ToString();
                        recordInfo.strDuration=show["runtime"].ToString();
                        //recordInfo.strDuration="1";

                        if(recInfoDict.ContainsKey(name))
                            recInfoDict[name]=recordInfo;
                        else
                            recInfoDict.Add(name,recordInfo);
                        
                        //Console.WriteLine($"{show["channel"]}:{show["quality"]} {show["name"]} starting at {show["time"]} for {show["runtime"]} minutes");
                    }
                }
            }

            return recInfoDict;
        }

        private async Task<string> Authenticate(string user,string pass)
        {
            string hashValue=null;

            using (var client = new HttpClient())
            {
                try
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
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Request exception: {e.Message}");
                }
            }

            return hashValue;
        }
        
        private int CaptureStream(string hashValue,RecordInfo recordInfo,IConfiguration configuration)
        {
            int currentFileNum = 0;
            int currentChannelFailureCount=0;
            int lastChannelFailureTime = 0;
            Process p=null;

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(recordInfo.GetFirstChannel(),hashValue,recordInfo.fileName+currentFileNum,configuration);
            Console.WriteLine("Starting Capture: {0} {1}",configuration["ffmpegPath"],cmdLineArgs);
            p = Process.Start(configuration["ffmpegPath"],cmdLineArgs);

            //
            //Loop in case connection is flaky
            //
            for(int loopNum=0;loopNum<recordInfo.GetDuration();loopNum++)
            {
                //start process if not started already
                if(p==null || p.HasExited)
                {
                    Console.WriteLine("Capture Failed for channel {0} at minute {1}", recordInfo.GetCurrentChannel(),loopNum);

                    //increment failure count and file number
                    currentChannelFailureCount++;
                    currentFileNum++;

                    //Got to next channel if channel has been alive for less than 15 minutes
                    if((loopNum-lastChannelFailureTime)<=15)
                    {
                        //Set quality ratio for current channel
                        double qualityRatio=(loopNum-lastChannelFailureTime)/currentChannelFailureCount;
                        recordInfo.SetCurrentQualityRatio(qualityRatio);
                        Console.WriteLine("Setting quality ratio {0} for channel {1}", qualityRatio,recordInfo.GetCurrentChannel());

                        //Determine correct next channel based on number and quality
                        SetNextChannel(recordInfo,loopNum);
                    }

                    //Now get things setup and going again
                    cmdLineArgs=BuildCaptureCmdLineArgs(recordInfo.GetCurrentChannel(),hashValue,recordInfo.fileName+currentFileNum,configuration);
                    Console.WriteLine("Starting Capture (again): {0} {1}",configuration["ffmpegPath"],cmdLineArgs);
                    p = Process.Start(configuration["ffmpegPath"],cmdLineArgs);

                    //Set the time we last failed a channel                        
                    lastChannelFailureTime = loopNum;  
                }

                //Wait
                TimeSpan interval = new TimeSpan(0, 1, 0);
                Thread.Sleep(interval);
            }
            Console.WriteLine($"Killing process now, time is up");

            // Free resources associated with process.
            p.Kill();
            p.WaitForExit();

            return currentFileNum;
        }

        private void SetNextChannel(RecordInfo recordInfo,int loopNum)
        {
            //Return if we've already selected the best channel
            if(recordInfo.bestChannelSetFlag)
                return;

            //opportunistically increment
            recordInfo.currentChannelIdx++;                

            if(recordInfo.currentChannelIdx < recordInfo.GetNumberOfChannels())  
            {
                //do we still have more channels?  If so, grab the next one
                Console.WriteLine("Switching to channel {0}", recordInfo.GetCurrentChannel());
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

                    Console.WriteLine("Now setting channel to {0} with quality ratio of {1} for the rest of the capture session",recordInfo.GetCurrentChannel(),recordInfo.GetCurrentQualityRatio());
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

        private void ExecProcess(string exe,string cmdLineArgs)
        {
            Console.WriteLine("Exe: {0} {1}", exe,cmdLineArgs);
            Process p = Process.Start(exe,cmdLineArgs);
            p.WaitForExit();
        }

        private void FixUp(int numFiles,RecordInfo recordInfo,IConfiguration configuration)
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
            Console.WriteLine("Num Files: {0}", numFiles+1);
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
                Console.WriteLine("Starting Concat:");
                ExecProcess(configuration["ffmpegPath"],cmdLineArgs);

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
            Console.WriteLine("Starting Mux:");
            ExecProcess(configuration["ffmpegPath"],cmdLineArgs);

            //If final file exist, delete old .ts file
            if(File.Exists(outputFile))
            {
                Console.WriteLine("Removing ts file: {0}",inputFile);
                File.Delete(inputFile);
            }
        }
    }

    public class RecordInfo
    {
        public string strDuration { get; set; }
        public string fileName { get; set; }
        public string strStartDT { get; set; }

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
            DateTime startDT=DateTime.Parse(strStartDT);
            return startDT.AddHours(-3);
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
