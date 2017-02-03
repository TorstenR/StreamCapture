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
        public string qualityPref { get; set; }
        public string langPref { get; set; }

        public bool bestChannelSetFlag { get; set; }
        public bool processSpawnedFlag  { get; set; }

        public Channels channels;

        public RecordInfo()
        {
            channels=new Channels();

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
        
        //Returns array of channels which are in order of preference to use
        public ChannelInfo[] GetSortedChannels()
        {
            List<ChannelInfo> hdChannelsList = new List<ChannelInfo>();
            List<ChannelInfo> usChannelsList = new List<ChannelInfo>();
            List<ChannelInfo> otherChannelsList = new List<ChannelInfo>();
            List<ChannelInfo> sortedList = new List<ChannelInfo>();

            List<ChannelInfo> channelInfoLIst = channels.GetChannels();
            foreach (ChannelInfo channelInfo in channelInfoLIst)
            {
                if (channelInfo.qualityTag.Length == 0 || channelInfo.qualityTag.ToLower().Contains(qualityPref.ToLower()))
                    hdChannelsList.Add(channelInfo);
                else if (channelInfo.lang.Length == 0 || channelInfo.lang.ToLower().Contains(langPref.ToLower()))
                    usChannelsList.Add(channelInfo);
                else
                {
                    otherChannelsList.Add(channelInfo);
                }
            }

            //Start the list we're returning by a sort  (in the future put cool heuristics here)
            //sortedList = hdChannelsList.OrderBy(o => o.number).ToList();
            sortedList.AddRange(hdChannelsList);
            sortedList.AddRange(usChannelsList);
            sortedList.AddRange(otherChannelsList);

            return sortedList.ToArray();
        }

        //Returns a human readable string listing the channels associated w/ this recording
        public string GetChannelString()
        {
            string channelStr = "";

            ChannelInfo[] sortedChannels = GetSortedChannels();
            foreach (ChannelInfo channelInfo in sortedChannels)
                channelStr = channelStr + channelInfo.description + " ";

            return channelStr;
        }
    }
}