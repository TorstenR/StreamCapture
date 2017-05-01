using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StreamCapture;

namespace StreamCaptureWeb
{
    public class HomeController : Controller
    {
        //Holds context
        public Recordings recordings;

        public HomeController(Recordings _recordings)
        {
            recordings = _recordings;
        }

        [HttpGet("/api/schedule")]
        public IActionResult GetSchedule()
        {
            Console.WriteLine("API called!");
            List<RecordInfo> recordingsList = recordings.GetRecordInfoList();
            return Json(recordingsList);
            //return Json(new { Result = "OK", Records = recordingsList });
        }

        [HttpGet("home")]
        public IActionResult MainGrid()
        {
            //ViewBag.Message = "Hello world!";
            //ViewBag.Time = DateTime.Now;

            RecordInfo recordInfo = recordings.GetRecordInfoList()[0];
            ViewData["Message"] = recordInfo.description;            

            return View();
        }
    }
}
