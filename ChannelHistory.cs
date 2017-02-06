using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;

namespace StreamCapture
{
    public class ChannelHistory
    {
        Dictionary<string, ChannelHistoryInfo> channelHistoryDict;
        static readonly object _lock = new object();  //used to lock the json load and save portion

        public ChannelHistory()
        {
            try
            {
                lock (_lock)
                {
                    channelHistoryDict = JsonConvert.DeserializeObject<Dictionary<string, ChannelHistoryInfo>>(File.ReadAllText("channelhistory.json"));
                }
            }
            catch(Exception)
            {
                channelHistoryDict = new Dictionary<string, ChannelHistoryInfo>();
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                File.WriteAllText("channelhistory.json", JsonConvert.SerializeObject(channelHistoryDict, Formatting.Indented));
            }
        }

        public ChannelHistoryInfo GetChannelHistoryInfo(string channel)
        {
            ChannelHistoryInfo channelHistoryInfo;

            if(!channelHistoryDict.TryGetValue(channel, out channelHistoryInfo))
            {
                channelHistoryInfo=new ChannelHistoryInfo();
                channelHistoryInfo.channel = channel;
                channelHistoryInfo.hoursRecorded = 0;
                channelHistoryInfo.recordingsAttempted = 0;
                channelHistoryInfo.lastAttempt = DateTime.Now;
                channelHistoryInfo.lastSuccess = DateTime.Now;
                channelHistoryInfo.activeFlag = true;
                channelHistoryInfo.serverSpeed = new Dictionary<string,long>();

                channelHistoryDict.Add(channel, channelHistoryInfo);
            }   

            return channelHistoryInfo;
        }

        public void SetServerAvgKBytesSec(string channel,string server,long avgKBytesSec)
        {
            ChannelHistoryInfo channelHistoryInfo=GetChannelHistoryInfo(channel);

            //For backwards compat, make sure serverSpeed is init'd
            if(channelHistoryInfo.serverSpeed==null)
                channelHistoryInfo.serverSpeed = new Dictionary<string,long>();

            long origAvgKBytesSec=0;
            if(channelHistoryInfo.serverSpeed.TryGetValue(server,out origAvgKBytesSec))
            {
                avgKBytesSec=(avgKBytesSec+origAvgKBytesSec)/2;
                channelHistoryInfo.serverSpeed[server]=avgKBytesSec;
            }
            else
            {
                channelHistoryInfo.serverSpeed.Add(server,avgKBytesSec);
            }
        }

        public long GetAvgKBytesSec(string server,string channel)
        {
            ChannelHistoryInfo channelHistoryInfo=GetChannelHistoryInfo(channel);
            
            //Make sure serverSpeed dict exists
            if(channelHistoryInfo.serverSpeed==null)
                return 0;
         
            //See if there's an entry for this server
            long avgKBytesSec;
            if(channelHistoryInfo.serverSpeed.TryGetValue(server,out avgKBytesSec))
                return avgKBytesSec;

            //Oh well, just return 0 ifn ot found
            return 0;
        }
    }
}
