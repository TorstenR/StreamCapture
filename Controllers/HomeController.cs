using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using StreamCapture;

namespace StreamCaptureWeb
{
    public class HomeController : Controller
    {
        //Holds context
        private IConfiguration configuration;
        private Recordings recordings;

        public HomeController(Recordings _recordings)
        {
            recordings = _recordings;

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            configuration = builder.Build();
        }

        [HttpGet("/api/reload")]
        public IActionResult ReloadSchedule()
        {
            Console.WriteLine("API: reload");

            //Wake up sleeping thread to reload the schedule and re-apply heuristics
            recordings.mre.Set();

            return Json(new { Result = "OK"});
        }

        [HttpGet("/api/schedule")]
        public string GetSchedule()
        {
            Console.WriteLine("API: get schedule");
            
            //Load selected recordings            
            Dictionary<string,RecordInfo> recordDict = recordings.GetRecordInfoDictionary();

            //Load schedule
            Schedule schedule = new Schedule();
            schedule.LoadSchedule(configuration["scheduleURL"],configuration["debug"]).Wait();
            List<ScheduleShow> scheduleShowList = schedule.GetScheduledShows();
            foreach(ScheduleShow scheduleShow in scheduleShowList)
            {
                //Let's see if it's already on the list - if not, we'll add it
                string key=recordings.BuildRecordInfoKeyValue(scheduleShow);
                if(!recordDict.ContainsKey(key))
                {
                    RecordInfo recordInfo = recordings.BuildRecordInfoFromShedule(new RecordInfo(),scheduleShow);
                    recordDict.Add(key,recordInfo);
                }
            }

            return JsonConvert.SerializeObject(recordDict.Values.ToList());
        }

        [HttpPost("/api/edit")]
        public IActionResult EditSchedule()
        {
            Console.WriteLine("API: post call");
            foreach (string key in this.Request.Form.Keys)
            {
                Console.WriteLine($"{key} : {this.Request.Form[key]}");
            }

            //If Delete  (really means set ignore flag)
            if(this.Request.Form["oper"]=="cancel")
            {
               foreach(RecordInfo recordInfo in recordings.GetRecordInfoList())
               {
                   if(recordInfo.id == this.Request.Form["id"])
                   {
                        Console.WriteLine($"Cancelling {recordInfo.description}");
                        recordInfo.cancelledFlag=true;

                        //Do the right thing to cancel depending on state
                        if(recordInfo.captureStartedFlag) //If we've started capturing, kill entire process
                            recordInfo.cancellationTokenSource.Cancel();      
                        else if(recordInfo.processSpawnedFlag) //If thread spawned, but not started, then wake it up to kill it
                            recordInfo.mre.Set();
                   }
               }
            }

            //If Queue new show 
            if(this.Request.Form["oper"]=="queue")
            {
               foreach(RecordInfo recordInfo in recordings.GetRecordInfoList())
               {
                   if(recordInfo.id == this.Request.Form["id"])
                   {
                       Console.WriteLine($"Found {recordInfo.description}");
                       recordInfo.cancelledFlag=false;
                       recordInfo.partialFlag=false;
                       recordInfo.completedFlag=false;
                       recordInfo.tooManyFlag=false;
                       string recordInfoKey=recordings.BuildRecordInfoKeyValue(recordInfo);
                       recordings.AddUpdateRecordInfo(recordInfoKey,recordInfo);
                   }
               }

                //Wake up sleeping thread to reload the schedule and re-apply heuristics
                recordings.mre.Set();
            }            


            return Json(new { Result = "OK"});
        }

        [HttpGet("home")]
        public IActionResult MainGrid()
        {
            return View();
        }
    }
}
