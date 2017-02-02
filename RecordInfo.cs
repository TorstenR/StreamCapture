using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.CommandLineUtils;


namespace StreamCapture
{
    public class RecordInfo
    {
        public string strDuration { get; set; }
        public string strStartDT { get; set; }
        public string strEndDT { get; set; }
        public string strDTOffset  { get; set; }

        public string id { get; set; }
        public string fileName { get; set; }
        public DateTime startDT { get; set; }
        public string description { get; set; }
        public int preMinutes { get; set; }
        public int postMinutes { get; set; }
        public string qualityPref { get; set; }
        public string langPref { get; set; }

        public bool bestChannelSetFlag { get; set; }
        public bool processSpawnedFlag  { get; set; }

        public ChannelHistory channelHistory;
        public Channels channels;

        public RecordInfo(ChannelHistory _channelHistory)
        {
            channelHistory = _channelHistory;
            channels=new Channels(this,channelHistory);

            //Init certain properties 
            id=DateTime.Now.Ticks.ToString();
            bestChannelSetFlag=false;
            processSpawnedFlag=false;
            strDTOffset="0";
            qualityPref="";
            langPref="";
        }

        public DateTime GetStartDT()
        {
            if(strStartDT == null)
                return DateTime.Now;

            //Create base date time
            DateTime startDT=DateTime.Parse(strStartDT);

            //subtract pre time 
            int preMin =preMinutes*-1;
            startDT=startDT.AddMinutes(preMin);

            //Add offset 
            int timeOffset=Convert.ToInt32(strDTOffset);
            startDT=startDT.AddHours(timeOffset);

            return startDT;
        }

        //Return duration in minutes
        public int GetDuration()
        {
            int duration = Convert.ToInt32(strDuration);
            duration = duration + preMinutes + postMinutes;
            return duration;
        }
    }
}