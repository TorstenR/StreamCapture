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
    public class ChannelInfo
    {
        public string number { get; set; }
        public string description { get; set; }
        public string qualityTag { get; set; }
        public double ratio { get; set; }
    }

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
        public int currentChannelIdx { get; set; }
        public bool bestChannelSetFlag { get; set; }
        public bool processSpawnedFlag  { get; set; }

        private Dictionary<string, ChannelInfo> channelDict;

        public RecordInfo()
        {
            channelDict = new Dictionary<string, ChannelInfo>();

            //Init certain properties 
            id=DateTime.Now.Ticks.ToString();
            currentChannelIdx=0;
            bestChannelSetFlag=false;
            processSpawnedFlag=false;
            strDTOffset="0";
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

        public int GetDuration()
        {
            int duration = Convert.ToInt32(strDuration);
            duration = duration + preMinutes + postMinutes;
            return duration;
        }

        public void LoadChannels(string strChannels)
        {
            string[] channelArray = strChannels.Split('+');
            foreach(string channel in channelArray)
            {
                ChannelInfo channelInfo = new ChannelInfo();
                channelInfo.number=channel;
                channelInfo.description=channel;
                channelInfo.ratio=0;
                channelDict.Add(channel,channelInfo);
            }
        }

        public string GetChannelString()
        {
            string channelStr="";

            foreach (KeyValuePair<string, ChannelInfo> kvp in channelDict)
                channelStr=channelStr+kvp.Value.description+" ";

            return channelStr;
        }

        public string GetChannelArgs()
        {
            string channelStr="";

            foreach (KeyValuePair<string, ChannelInfo> kvp in channelDict)
                channelStr=channelStr+kvp.Value.number+"+";
            channelStr=channelStr.Trim('+');

            return channelStr;
        }

        public string GetFirstChannel()
        {
            KeyValuePair<string, ChannelInfo> kvp = channelDict.First();
            return kvp.Value.number;
        }

        public string GetCurrentChannel()
        {
            KeyValuePair<string, ChannelInfo> kvp = channelDict.ElementAt(currentChannelIdx);
            return kvp.Value.number;
        }

        public string GetChannel(int channelIdx)
        {
            KeyValuePair<string, ChannelInfo> kvp = channelDict.ElementAt(channelIdx);
            return kvp.Value.number;
        }

        public void AddChannel(string channel,string qualityTag)
        {
             if(channel.Length == 1)
                channel="0"+channel;

            if(channelDict.ContainsKey(channel))
                return;

            ChannelInfo channelInfo = new ChannelInfo();
            channelInfo.number=channel;
            channelInfo.description=channel;
            channelInfo.ratio=0;
            channelInfo.qualityTag=qualityTag;
            channelDict.Add(channel,channelInfo);
        }

        public int GetNumberOfChannels()
        {
            return channelDict.Count;
        }

        /*
        public void SetCurrentQualityRatio(double ratio)
        {
            channelRatioList.Insert(currentChannelIdx,ratio);
        }

        public double GetCurrentQualityRatio()
        {
            return channelRatioList[currentChannelIdx];
        }

        public double GetQualityRatio(int channelIdx)
        {
            return channelRatioList[channelIdx];
        }
        */
    }
}