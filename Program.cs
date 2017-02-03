using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.CommandLineUtils;

//
// TODO
//
// split single recording and loop out
// add verification steps for each class, including main startup
// make sure authentication is error checked


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
            
            //do we have optional args passed in?
            bool optionalArgsFlag=false;
            if(channels.HasValue() && duration.HasValue() && filename.HasValue())
                optionalArgsFlag=true;
            else
            {
                Console.WriteLine($"{DateTime.Now}: Using keywords.json to search schedule. (Please run with --help if you're confused)");
                Console.WriteLine($"=======================");                
            }

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();

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
                Recorder recorder = new Recorder();
                recorder.QueueRecording(recordInfo,configuration,false);
                Environment.Exit(0);
            }
            else
            {
                //Monitor schedule and spawn capture sessions as needed
                Recorder recorder = new Recorder();
                recorder.MonitorMode(configuration);
            }
        }
    }
}
