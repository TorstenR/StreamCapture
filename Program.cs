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


namespace WebRequest
{
    
    public class Program
    {
        public static void Main(string[] args)
        {
            if(args.Length < 3)
            {
                Console.WriteLine($"[channel1+channle2+...] [minutes] [filename] [[ffmpeg args]]");
                Environment.Exit(1);
            }

            foreach(string arg in args)
                Console.WriteLine($"arg: {arg}",arg);

            string ffmpegArgs = "";
            if(args.Length>=4)
                ffmpegArgs=args[3];

            int minutes = Convert.ToInt32(args[1]);
            SendRequest(args[0],minutes,args[2],ffmpegArgs).Wait();
        }
        
        private static async Task SendRequest(string channel,int minutes,string filename,string ffmpegArgs)
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
                    
                    Console.WriteLine($"Response: {stringResponse}");

                    //Grab hash
                    JsonTextReader reader = new JsonTextReader(new StringReader(stringResponse));
                    while (reader.Read())
                    {
                        if (reader.Value != null)
                        {
                            Console.WriteLine("Token: {0}, Value: {1}", reader.TokenType, reader.Value);
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

                //Assume there's a list of channels
                string[] channels = channel.Split('+');

                //Build ffmpeg command line with first channel
                string exe=@"ffmpeg\bin\ffmpeg";
                string args=BuildCaptureArgs(channels[0],hashValue);
                Process p=null;

                //Loop in case connection is flaky

                // Keep track of failure ratio for 5 minutes, then choose best channel
                //
                int currentChannel = 0;
                int startingMinute = 0;
                int channelFailureCount = 0;  
                int lastChannelFailure = 0;
                int bestChannelIdx = -1;
                double[] qualityRatio = new double[channels.Length];
                for(int loopNum=0;loopNum<(minutes);loopNum++)
                {
                    //start process if not started already
                    if(p==null || p.HasExited)
                    {
                        lastChannelFailure = loopNum;  //know when we last failed
                        if(lastChannelFailure>0)
                            Console.WriteLine("Capture Failed for channel {0} at minute {1}", channels[currentChannel],loopNum);

                        Console.WriteLine("Starting Capture: {0} {1}", exe, args + @filename + loopNum + ".ts" + " -stimeout 30000 " + ffmpegArgs + " > out.txt 2> err.txt");
                        p = Process.Start(exe, args + @filename + loopNum + ".ts");

                        //Check for quality if more than 3 minutes, and go to next channel unless we've already selected best channel OR channel has been doing fine (stays alive for 15 minutes)
                        if( (loopNum>=(startingMinute+3)) && ((loopNum-lastChannelFailure)<=10) && bestChannelIdx < 0)
                        {
                            //increment failure count 
                            channelFailureCount++;

                            //Set quality ratio for current channel
                            qualityRatio[currentChannel]=(loopNum-startingMinute)/channelFailureCount;
                            startingMinute=loopNum; //reset
                            Console.WriteLine("Setting quality ratio {0} for channel {1}", qualityRatio[currentChannel],channels[currentChannel]);

                            //Goto next channel if exist.  Otherwise, just use best channel
                            currentChannel++;
                            if(currentChannel < channels.Length)
                            {
                                Console.WriteLine("Switching to channel {0}", channels[currentChannel]);
                                channelFailureCount=0;  
                                args=BuildCaptureArgs(channels[currentChannel],hashValue);
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
                                        bestChannelIdx=b;
                                    }
                                }

                                //Now set channel
                                args=BuildCaptureArgs(channels[bestChannelIdx],hashValue);
                                Console.WriteLine("Determined that channel {0} is best with ratio of {1}", channels[bestChannelIdx],qualityRatio[bestChannelIdx]);
                            }
                        }
                    }

                    //Wait
                    TimeSpan interval = new TimeSpan(0, 1, 0);
                    Thread.Sleep(interval);
                }
                Console.WriteLine($"Killing process now, time is up");

                // Free resources associated with process.
                //p.StandardInput.WriteLine("\x3");
                p.Kill();
                p.WaitForExit();
            }
        }

        private static string BuildCaptureArgs(string channel,string hashValue)
        {
            //C:\Users\mark\Desktop\ffmpeg\bin\ffmpeg -i "http://dnaw1.smoothstreams.tv:9100/view247/ch01q1.stream/playlist.m3u8?wmsAuthSign=c2VydmVyX3RpbWU9MTIvMS8yMDE2IDM6NDA6MTcgUE0maGFzaF92YWx1ZT1xVGxaZmlzMkNYd0hFTEJlaTlzVVJ3PT0mdmFsaWRtaW51dGVzPTcyMCZpZD00MzM=" -c copy t.ts
            
            
            //string vidURI = "http://dnaw1.smoothstreams.tv:9100/view247/ch"+ channel + "q1.stream/playlist.m3u8?wmsAuthSign=" + hashValue;  //West coast server
            string vidURI = "http://deu.uk1.SmoothStreams.tv:9100/view247/ch"+ channel + "q1.stream/playlist.m3u8?wmsAuthSign=" + hashValue;  //london1 server
            
            string args=@"-i " + vidURI + " -c copy ";

            return args;
        }
    }
}
