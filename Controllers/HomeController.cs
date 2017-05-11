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
        private Recorder recorder;

        public HomeController(Recorder _recorder)
        {
            recorder = _recorder;

            //Read and build config
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            configuration = builder.Build();
        }

        [HttpGet("/api/schedule")]
        public string GetSchedule()
        {
            Console.WriteLine($"{DateTime.Now}: WebAPI: Get schedule");

            //Build schedule from recorder info (cached since last reload)
            Dictionary<string, RecordInfo> newRecordDict = BuildSchedule();

            //Wake up sleeping thread to reload the schedule and re-apply heuristics
            recorder.recordings.mre.Set();

            //Return schedule json
            return JsonConvert.SerializeObject(newRecordDict.Values.ToList());
        }

        private Dictionary<string, RecordInfo> BuildSchedule()
        {
            //Clone existing queued (selected) dictionary
            Dictionary<string, RecordInfo> newRecordDict = new Dictionary<string, RecordInfo>(recorder.recordings.GetRecordInfoDictionary());

            //Add to cloned dictionary from full schedule
            foreach (ScheduleShow scheduleShow in recorder.scheduleShowList)
            {
                //Let's see if it's already on the list - if not, we'll add it
                string key = recorder.recordings.BuildRecordInfoKeyValue(scheduleShow);
                if (!newRecordDict.ContainsKey(key))
                {
                    RecordInfo recordInfo = recorder.recordings.BuildRecordInfoFromShedule(new RecordInfo(), scheduleShow);
                    newRecordDict.Add(key, recordInfo);
                }
            }

            return newRecordDict;
        }

        [HttpPost("/api/edit")]
        public IActionResult EditSchedule()
        {
            /* 
            Console.WriteLine("API: post call");
            foreach (string key in this.Request.Form.Keys)
            {
                Console.WriteLine($"{key} : {this.Request.Form[key]}");
            }
            */

            //If Delete  (really means set ignore flag)
            if(this.Request.Form["oper"]=="cancel")
            {
               foreach(RecordInfo recordInfo in recorder.recordings.GetRecordInfoList())
               {
                   if(recordInfo.id == this.Request.Form["id"])
                   {
                        Console.WriteLine($"{DateTime.Now}: WebAPI: Cancelling {recordInfo.description}");
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
                //Build schedule from recorder info (cached since last reload)
                Dictionary<string, RecordInfo> newRecordDict = BuildSchedule();

                foreach (RecordInfo recordInfo in newRecordDict.Values.ToList())
               {
                   if(recordInfo.id == this.Request.Form["id"])
                   {
                       Console.WriteLine($"{DateTime.Now}: WebAPI: Queuing {recordInfo.description}");
                       recordInfo.cancelledFlag=false;
                       recordInfo.partialFlag=false;
                       recordInfo.completedFlag=false;
                       recordInfo.tooManyFlag=false;
                       recordInfo.manualFlag=true;
                       string recordInfoKey= recorder.recordings.BuildRecordInfoKeyValue(recordInfo);

                        //Adds to the recordings list from main process
                        recorder.recordings.AddUpdateRecordInfo(recordInfoKey,recordInfo);
                   }
               }

                //Wake up sleeping thread to reload the schedule and re-apply heuristics
                recorder.recordings.mre.Set();
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
