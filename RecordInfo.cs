using System;
using System.Collections.Generic;

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
        public bool starredFlag { get; set; }
        public bool emailFlag {get; set; }
        public string qualityPref { get; set; }
        public string langPref { get; set; }
        public string channelPref { get; set; }
        public string category { get; set; }
        public int keywordPos { get; set;}

        public bool processSpawnedFlag  { get; set; }
        public bool partialFlag { get; set; }
        public bool completedFlag { get; set; }

        public Channels channels;

        public RecordInfo()
        {
            channels=new Channels();

            //Init certain properties 
            id=DateTime.Now.Ticks.ToString();
            processSpawnedFlag=false;
            completedFlag=false;
            partialFlag=false;
            strDTOffset="0";
            qualityPref="";
            langPref="";
            category="";
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

        public DateTime GetEndDT()
        {
            if(strEndDT == null)
                return DateTime.Now;

            //Create base date time
            DateTime endDT=DateTime.Parse(strEndDT);

            //Add offset 
            int timeOffset=Convert.ToInt32(strDTOffset);
            endDT=endDT.AddHours(timeOffset);

            return endDT;
        }

        //Return duration in minutes
        public int GetDuration()
        {
            int duration = Convert.ToInt32(strDuration);
            duration = duration + preMinutes + postMinutes;
            return duration;
        }

        //Returns a human readable string listing the channels associated w/ this recording
        public string GetChannelString()
        {
            string channelStr = "";

            List<ChannelInfo> channelList = channels.GetChannels();
            foreach (ChannelInfo channelInfo in channelList)
                channelStr = channelStr + channelInfo.description + " ";

            return channelStr;
        }
    }
}