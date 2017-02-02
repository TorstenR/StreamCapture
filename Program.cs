using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics;
using System.Threading;
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
            
            //do we have optional args passed in?
            bool optionalArgsFlag=false;
            if(channels.HasValue() && duration.HasValue() && filename.HasValue())
                optionalArgsFlag=true;
            else
                Console.WriteLine($"{DateTime.Now}: No usable command line options passed in - Using keywords to search schedule. (Please run with --help if you'ref confused)");

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var configuration = builder.Build();

            //Use optional parameters to record if passed in
            if(optionalArgsFlag)
            {
                //Create new RecordInfo
                RecordInfo recordInfo = new RecordInfo();
                recordInfo.channels.LoadChannels(channels.Value());
                recordInfo.strDuration=duration.Value();
                recordInfo.strStartDT=datetime.Value();
                recordInfo.fileName=filename.Value();

                //Start recording
                Recorder recorder = new Recorder();
                recorder.MainAsync(recordInfo,configuration).Wait();
                Environment.Exit(0);
            }

            //Create new recordings object to manage our recordings
            Recordings recordings = new Recordings(configuration);

            //Since we're using keywords, grab schedule from interwebs and loop forever, checking every n hours for new shows to record
            while(true)
            {
                //Grabs schedule and builds a recording list based on keywords
                List<RecordInfo> recordInfoList = recordings.BuildRecordSchedule();

                //Go through record list, spawn a new process for each show found
                foreach (RecordInfo recordInfo in recordInfoList)
                {            
                    //If show is not already in the past or waiting, let's go!
                    int hoursInFuture=Convert.ToInt32(configuration["hoursInFuture"]);
                    if(recordInfo.GetStartDT()>DateTime.Now && recordInfo.GetStartDT()<=DateTime.Now.AddHours(hoursInFuture) && !recordInfo.processSpawnedFlag)
                    {
                        recordInfo.processSpawnedFlag=true;
                        DumpRecordInfo(Console.Out,recordInfo,"Schedule Read: "); 
        
                        Recorder recorder = new Recorder();
                        Task.Factory.StartNew(() => recorder.MainAsync(recordInfo,configuration));  //use threadpool instead of more costly os threads
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
                Console.WriteLine($"{DateTime.Now}: Now sleeping for {timeToWait.Hours+1} hours before checking again at {nextRecord.ToString()}");
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
    }
}
