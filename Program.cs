using System;
using System.IO;
using Microsoft.Extensions.Configuration;
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
            commandLineApplication.HelpOption("-? | -h | --help");
            commandLineApplication.Execute(args);  
            
            //Welcome message
            Console.WriteLine($"{DateTime.Now}: StreamCapture Version 0.99  2/12/2017");

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();
            VerifyAppsettings(configuration);

            //do we have optional args passed in?
            bool optionalArgsFlag=false;
            if(channels.HasValue() || duration.HasValue() || filename.HasValue())
            {
                VerifyCommandLineParams(channels,duration,filename,datetime);
                optionalArgsFlag=true;
            }
            else
            {
                Console.WriteLine($"{DateTime.Now}: Using keywords.json to search schedule. (Please run with --help if you're confused)");
                Console.WriteLine($"=======================");                
            }

            //Use optional parameters to record are passed in
            if(optionalArgsFlag)
            {
                //Create new RecordInfo
                RecordInfo recordInfo = new RecordInfo();
                recordInfo.channels.LoadChannels(channels.Value());
                recordInfo.strDuration=duration.Value();
                recordInfo.strStartDT=datetime.Value();
                recordInfo.fileName=filename.Value();

                //Record a single show and then quit
                ChannelHistory channelHistory = new ChannelHistory();
                Recorder recorder = new Recorder(configuration);
                recorder.QueueRecording(channelHistory,recordInfo,configuration,false);
                Environment.Exit(0);
            }
            else
            {
                //Monitor schedule and spawn capture sessions as needed
                Recorder recorder = new Recorder(configuration);
                recorder.MonitorMode();
            }
        }

        static private void VerifyCommandLineParams(CommandOption channels,CommandOption duration,CommandOption filename,CommandOption datetime)
        {
            Console.WriteLine($"{DateTime.Now}: Verifying command line options passed in....");

            //Check channels
            try
            {
                string[] channelArray=channels.Value().Split('+');
                foreach(string channel in channelArray)
                {
                    ValidateInt("command line param","--channels",channel,0,1000);                     
                }
            }
            catch(Exception e)
            {
                Console.WriteLine($"ERROR: 'channels' command line param invalid.  Should be in format nn+nn+nn...  Error: {e.Message}");
                Environment.Exit(1);
            }

            //Check duration
            ValidateInt("command line param","--duration",duration.Value(),1,1440);  

            //Check filename
            string fileName=filename.Value();
            var isValid = !string.IsNullOrEmpty(fileName) &&
                        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                        !File.Exists(Path.Combine(".", fileName));     
            if(!isValid)                     
            {
                Console.WriteLine($"ERROR: 'filename' command line param is invalid.");
                Environment.Exit(1);                
            }

            //Check datetime
            if(!string.IsNullOrEmpty(datetime.Value()))
            {
                try
                {
                    DateTime dt=DateTime.Parse(datetime.Value());
                    if(dt<DateTime.Now)
                    {
                        Console.WriteLine($"ERROR: 'datetime' command line param invalid.  Should be in the future.");
                        Environment.Exit(1);                        
                    }                
                    if(dt>DateTime.Now.AddDays(1))
                    {
                        Console.WriteLine($"ERROR: 'datetime' command line param invalid.  It's more than 1 day in the future.");
                        Environment.Exit(1);                        
                    }       
                }
                catch(Exception e)
                {
                    Console.WriteLine($"ERROR: 'datetime' command line param invalid.  Error: {e.Message}");
                    Environment.Exit(1);
                }    
            }         
        }
        static private void VerifyAppsettings(IConfiguration configuration)
        {
            Console.WriteLine($"{DateTime.Now}: Verifying appsettings....");

            //Check schedule check schedule
            try
            {
                string[] times=configuration["scheduleCheck"].Split(',');
                foreach(string strTime in times)
                {
                    ValidateInt("appsettings.json","scheduleCheck",strTime,0,24);                     
                }
            }
            catch(Exception e)
            {
                Console.WriteLine($"ERROR: 'scheduleCheck' in appsettings.json is invalid.  Should be in format hh,hh,hh...  Error: {e.Message}");
                Environment.Exit(1);
            }       

            //Check servers
            string serverList=configuration["serverList"];
            string captureCmdLine=configuration["captureCmdLine"];
            if(string.IsNullOrEmpty(serverList) && captureCmdLine.Contains("[SERVER]"))
            {
                Console.WriteLine($"ERROR: 'serverList' is empty in appsettings, but 'captureCmdLine' is expecting '[SERVER]'");
                Environment.Exit(1);
            }
            if(!string.IsNullOrEmpty(serverList) && !captureCmdLine.Contains("[SERVER]"))
            {
                Console.WriteLine($"ERROR: 'serverList' has servers in appsettings, but 'captureCmdLine' does not have '[SERVER]'");
                Environment.Exit(1);
            }

            //Check concurrent captures
            ValidateInt("appsettings.json","concurrentCaptures",configuration["concurrentCaptures"],1,25);             

            //Check auth minutes
            ValidateInt("appsettings.json","authMinutes",configuration["authMinutes"],30,1440); 

            //Check hours in future
            ValidateInt("appsettings.json","hoursInFuture",configuration["hoursInFuture"],0,48);   

            //Check rention days     
            ValidateInt("appsettings.json","retentionDays",configuration["retentionDays"],1,120);        
           
            //Check retries
            ValidateInt("appsettings.json","numberOfRetries",configuration["numberOfRetries"],1,50);          

            //Check retries
            ValidateInt("appsettings.json","acceptableRate",configuration["acceptableRate"],1,6000000);                     

            //Check offset
            ValidateInt("appsettings.json","schedTimeOffset",configuration["schedTimeOffset"],-12,12);       

            //Check log directory
            ValidateDirExist("appsettings.json","logPath",configuration["logPath"]);

            //Check output directory
            ValidateDirExist("appsettings.json","outputPath",configuration["outputPath"]);

            //Check nas directory
            ValidateDirExist("appsettings.json","nasPath",configuration["nasPath"]);            

            //Check if ffmpeg exist
            ValidateFileExist("appsettings.json","ffmpegPath",configuration["ffmpegPath"]);   
        }

        static private void ValidateInt(string source,string paramName,string strInt,int lower,int upper)
        {
            try
            {
                int num=Convert.ToInt32(strInt);
                if(num < lower || num > upper)
                {
                    Console.WriteLine($"ERROR: '{paramName}' in {source} is invalid.  Should be >={lower} and <={upper}.");
                    Environment.Exit(1);                        
                }                
            }
            catch(Exception e)
            {
                Console.WriteLine($"ERROR: '{paramName}' in {source} is invalid.  Error: {e.Message}");
                Environment.Exit(1);
            }              
        }

        static private void ValidateDirExist(string source,string pathTag,string path)
        {
            //empty is fine
            if(string.IsNullOrEmpty(path))
                return;

            try
            {
                bool dirExists=Directory.Exists(path);
                if(!dirExists)
                {
                    Console.WriteLine($"ERROR: '{pathTag}' in {source} is invalid. Path '{path}' does not exist.");
                    Environment.Exit(1);                        
                }                
            }
            catch(Exception e)
            {
                Console.WriteLine($"ERROR: ERROR '{pathTag}' in {source} is invalid.  Error: {e.Message}");
                Environment.Exit(1);
            }              
        }
        static private void ValidateFileExist(string source,string fileTag,string filename)
        {
            try
            {
                bool fileExist=File.Exists(filename);
                if(!fileExist)
                {
                    Console.WriteLine($"ERROR: '{fileTag}' in {source} is invalid. File '{filename}' does not exist.");
                    Environment.Exit(1);    
                }                
            }
            catch(Exception e)
            {
                Console.WriteLine($"ERROR: ERROR '{fileTag}' in {source} is invalid.  Error: {e.Message}");
                Environment.Exit(1);
            }              
        }    
    }
}
