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


namespace WebRequest
{
    
    public class Program
    {
        private bool bestChannelSelected;

        public static void Main(string[] args)
        {
            Program p = new Program();
            p.MainAsync(args).Wait();
        }

        async Task MainAsync(string[] args)
        {
            if(args.Length < 3)
            {
                Console.WriteLine($"[channel1+channle2+...] [minutes] [filename] [[ffmpeg args]]");
                Environment.Exit(1);
            }

            //Dump args
            foreach(string arg in args)
                Console.WriteLine($"arg: {arg}");

            string ffmpegArgs = "";
            if(args.Length>=4)
                ffmpegArgs=args[3];

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();

            //Authenticate
            string hashValue = await Authenticate();

            //Capture stream
            int numFiles=CaptureStream(hashValue,args,configuration);

            //Fixup output
            FixUp(numFiles,args[2],configuration);
        }

        private async Task<string> Authenticate()
        {
            string hashValue=null;

            using (var client = new HttpClient())
            {
                try
                {
                    //http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php?username=mwilkie&password=123lauve&site=view247
                    Uri uri = new Uri("http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php");
                    client.BaseAddress = uri;
                    var response = await client.GetAsync("?username=mwilkie&password=123lauve&site=view247");
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
        
        private int CaptureStream(string hashValue,string[] args,IConfiguration configuration)
        {
            //Parse list of channels and get minute
            string[] channels = args[0].Split('+');
            int minutes = Convert.ToInt32(args[1]);

            int currentFileNum = 0;
            int currentChannel = 0;
            int currentChannelFailureCount=0;
            int lastChannelFailureTime = 0;
            double[] qualityRatio = new double[channels.Length];
            Process p=null;

            //initialize
            bestChannelSelected=false;

            //Build ffmpeg capture command line with first channel and get things rolling
            string cmdLineArgs=BuildCaptureCmdLineArgs(channels[0],hashValue,args[2]+currentFileNum,configuration);
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
                    cmdLineArgs=BuildCaptureCmdLineArgs(channels[currentChannel],hashValue,args[2]+currentFileNum,configuration);
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
        }
    }
}
