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
                Console.WriteLine($"[channel] [minutes] [filename]");
                Environment.Exit(1);
            }

            int minutes = Convert.ToInt32(args[1]);
            SendRequest(args[0],minutes,args[2]).Wait();
        }
        
        private static async Task SendRequest(string channel,int minutes,string filename)
        {
            string hashValue=null;

            using (var client = new HttpClient())
            {
                try
                {
                    //http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php?username=mwilkie&password=123lauve&site=view247
                    Uri uri = new Uri("http://smoothstreams.tv/schedule/admin/dash_new/hash_api.php");
                    client.BaseAddress = uri;
                    var response = await client.GetAsync("?username=----&password=----&site=view247");
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

                //C:\Users\mark\Desktop\ffmpeg\bin\ffmpeg -i "http://dnaw1.smoothstreams.tv:9100/view247/ch01q1.stream/playlist.m3u8?wmsAuthSign=c2VydmVyX3RpbWU9MTIvMS8yMDE2IDM6NDA6MTcgUE0maGFzaF92YWx1ZT1xVGxaZmlzMkNYd0hFTEJlaTlzVVJ3PT0mdmFsaWRtaW51dGVzPTcyMCZpZD00MzM=" -c copy t.ts

                //Build video capture URL
                string vidURI = "http://dnaw1.smoothstreams.tv:9100/view247/ch"+ channel + "q1.stream/playlist.m3u8?wmsAuthSign=" + hashValue;

                //Build ffmpeg command line
                string exe=@"ffmpeg\bin\ffmpeg";
                string args=@"-i " + vidURI + " -c copy ";

                Console.WriteLine($"Waiting {minutes} minutes...");
                Process p=null;

                //Loop in case connection is flaky
                for(int loopNum=0;loopNum<(minutes);loopNum++)
                {
                    //start process if not started already
                    if(p==null || p.HasExited)
                    {
                        Console.WriteLine("Command Line: {0} {1}", exe, args + @filename + loopNum + ".ts");
                        p = Process.Start(exe, args + @filename + loopNum + ".ts");
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
    }
}