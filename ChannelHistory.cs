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
    public class ChannelHistory
    {
        Dictionary<string, ChannelHistoryInfo> channelHistoryDict;

        public ChannelHistory()
        {
            try
            {
                channelHistoryDict = JsonConvert.DeserializeObject<Dictionary<string, ChannelHistoryInfo>>(File.ReadAllText("channelhistory.json"));
            }
            catch(Exception)
            {
                channelHistoryDict = new Dictionary<string, ChannelHistoryInfo>();
            }
        }

        public void Save()
        {
            File.WriteAllText("channelhistory.json", JsonConvert.SerializeObject(channelHistoryDict, Formatting.Indented));
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
                channelHistoryInfo.errors = 0;
                channelHistoryInfo.lastAttempt = DateTime.Now;
                channelHistoryInfo.lastSuccess = DateTime.Now;
                channelHistoryInfo.activeFlag = true;

                channelHistoryDict.Add(channel, channelHistoryInfo);
            }   

            return channelHistoryInfo;
        }
    }
}
