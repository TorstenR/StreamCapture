using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.CommandLineUtils;


namespace WebRequest
{
    
    public class Program
    {
        private bool bestChannelSelected;

        public static void Main(string[] args)
        {
            CommandLineApplication commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: false);
            CommandOption channels = commandLineApplication.Option("-c | --channels","Channels to record in the format nn+nn+nn",CommandOptionType.SingleValue);
            CommandOption duration = commandLineApplication.Option("-d | --duration","Duration in minutes to record",CommandOptionType.SingleValue);
            CommandOption filename = commandLineApplication.Option("-f | --filename","File name (no extension)",CommandOptionType.SingleValue);
            CommandOption datetime = commandLineApplication.Option("-d | --datetime","Datetime MM/DD/YY HH:MM",CommandOptionType.SingleValue);
            commandLineApplication.HelpOption("-? | -h | --help");
            commandLineApplication.Execute(args);       

            if(!channels.HasValue() || !duration.HasValue() || !filename.HasValue())
            {
                Console.WriteLine($"{DateTime.Now}: Incorrect command line options.  Please run with --help for more information.");
                Environment.Exit(1);                
            } 

            //Convert options passed in as necessary
            string[] strChannels = channels.Value().Split('+');
            int minutes = Convert.ToInt32(duration.Value());

            //Wait here until we're ready to start recording
            if(datetime.HasValue())
            {
                DateTime recStart = DateTime.Parse(datetime.Value());
                TimeSpan timeToWait = recStart - DateTime.Now;
                Console.WriteLine($"{DateTime.Now}: Waiting for {timeToWait.Days} Days, {timeToWait.Hours} Hours, and {timeToWait.Minutes} minutes.");
                Console.WriteLine($"Recording channel/s {channels.Value()} starting at {recStart} for {duration.Value()} minutes to {filename.Value()} file.");
                Thread.Sleep(timeToWait);
            }
            else
            {
                Console.WriteLine($"Recording channel/s {channels.Value()} now for {duration.Value()} minutes to {filename.Value()} file.");
            }

            Program p = new Program();
            p.MainAsync(strChannels,minutes,filename.Value()).Wait();
        }

        async Task MainAsync(string[] channels,int minutes,string fileName)
        {
            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();

            //Authenticate
            string hashValue = await Authenticate(configuration["user"],configuration["pass"]);

            //Capture stream
            int numFiles=CaptureStream(hashValue,channels,minutes,fileName,configuration);

            //Fixup output
            FixUp(numFiles,fileName,configuration);
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
        
        private int CaptureStream(string hashValue,string[] channels,int minutes,string fileName,IConfiguration configuration)
        {
            int currentFileNum = 0;
            int currentChannel = 0;
            int currentChannelFailureCount=0;
            int lastChannelFailureTime = 0;
            double[] qualityRatio = new double[channels.Length];
            Process p=null;

            //initialize
            bestChannelSelected=false;

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(channels[0],hashValue,fileName+currentFileNum,configuration);
            Console.WriteLine("Starting Capture: {0} {1}",configuration["ffmpegPath"],cmdLineArgs);
            p = Process.Start(configuration["ffmpegPath"],cmdLineArgs);

            //
            //Loop in case connection is flaky
            //
            for(int loopNum=0;loopNum<minutes;loopNum++)
            {
                //start process if not started already
                if(p==null || p.HasExited)
                {
                    Console.WriteLine("Capture Failed for channel {0} at minute {1}", channels[currentChannel],loopNum);

                    //increment failure count and file number
                    currentChannelFailureCount++;
                    currentFileNum++;

                    //Got to next channel if channel has been alive for less than 15 minutes
                    if((loopNum-lastChannelFailureTime)<=15)
                    {
                        //Set quality ratio for current channel
                        qualityRatio[currentChannel]=(loopNum-lastChannelFailureTime)/currentChannelFailureCount;
                        Console.WriteLine("Setting quality ratio {0} for channel {1}", qualityRatio[currentChannel],channels[currentChannel]);

                        //Determine correct next channel based on number and quality
                        currentChannel = GetNextChannel(currentChannel,loopNum,channels,qualityRatio);
                    }

                    //Now get things setup and going again
                    cmdLineArgs=BuildCaptureCmdLineArgs(channels[currentChannel],hashValue,fileName+currentFileNum,configuration);
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

        private int GetNextChannel(int currentChannel,int loopNum,string[] channels,double[] qualityRatio)
        {
            //Return if we've already selected the best channel
            if(bestChannelSelected)
                return currentChannel;

            //assume we have a next channel
            currentChannel++; 

            if(currentChannel < channels.Length)  
            {
                //do we still have more channels?  If so, grab the next one
                Console.WriteLine("Switching to channel {0}", channels[currentChannel]);
            }
            else
            {
                //grab best channel by grabbing the best ratio  
                double ratio=0;
                for(int b=0;b<channels.Length;b++)
                {
                    if(qualityRatio[b]>ratio)
                    {
                        ratio=qualityRatio[b];
                        currentChannel=b;
                        bestChannelSelected=true;
                    }

                    Console.WriteLine("Now setting channel to {0} with quality ratio of {1} for the rest of the capture session", channels[currentChannel],qualityRatio[currentChannel]);
                }
            }

            return currentChannel;
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

        private void FixUp(int numFiles,string fileName,IConfiguration configuration)
        {
            string cmdLineArgs;
            string outputFile;
            string videoFileName=fileName+"0.ts";
            string ffmpegPath = configuration["ffmpegPath"];
            string outputPath = configuration["outputPath"];

            //Concat if more than one file
            Console.WriteLine("Num Files: {0}", numFiles+1);
            if(numFiles > 0)
            {
                //make fileist
                string fileList = Path.Combine(outputPath,fileName+"0.ts");
                for(int i=1;i<=numFiles;i++)
                    fileList=fileList+"|"+Path.Combine(outputPath,fileName+i+".ts");

                //Create output file path
                outputFile=Path.Combine(outputPath,fileName+".ts");

                //"concatCmdLine": "[FULLFFMPEGPATH] -i \"concat:[FILELIST]\" -c copy [FULLOUTPUTPATH]",
                cmdLineArgs = configuration["concatCmdLine"];
                cmdLineArgs=cmdLineArgs.Replace("[FILELIST]",fileList);
                cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputFile);

                //Run command
                Console.WriteLine("Starting Concat:");
                ExecProcess(configuration["ffmpegPath"],cmdLineArgs);

                videoFileName=fileName+".ts";
            }

            //Mux file to mp4 from ts (transport stream)
            string inputFile=Path.Combine(outputPath,videoFileName);
            outputFile=Path.Combine(outputPath,fileName+".mp4");

            // "muxCmdLine": "[FULLFFMPEGPATH] -i [VIDEOFILE] -acodec copy -vcodec copy [FULLOUTPUTPATH]"
            cmdLineArgs = configuration["muxCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[VIDEOFILE]",inputFile);
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputFile);

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
}
