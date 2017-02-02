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
    public class Channels
    {
        private Dictionary<string, ChannelInfo> channelDict;
        private ChannelHistory channelHistory;
        private RecordInfo recordInfo;

        public Channels(RecordInfo _recordInfo, ChannelHistory _channelHistory)
        {
            recordInfo = _recordInfo;
            channelHistory = _channelHistory;
            channelDict = new Dictionary<string, ChannelInfo>();
        }

        //Load channels using a string with + delimted channels (comes from command line)
        public void LoadChannels(string strChannels)
        {
            string[] channelArray = strChannels.Split('+');
            foreach (string channel in channelArray)
            {
                BuildChannelInfo(channel, "", "");
            }
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

        //Returns array of channels which are in order of preference to use
        public ChannelInfo[] GetSortedChannels()
        {
            List<ChannelInfo> hdChannelsList = new List<ChannelInfo>();
            List<ChannelInfo> usChannelsList = new List<ChannelInfo>();
            List<ChannelInfo> otherChannelsList = new List<ChannelInfo>();
            List<ChannelInfo> sortedList = new List<ChannelInfo>();

            string qualityPref = recordInfo.qualityPref;
            string langPref = recordInfo.langPref;

            foreach (KeyValuePair<string, ChannelInfo> kvp in channelDict)
            {
                if (kvp.Value.qualityTag.Length == 0 || kvp.Value.qualityTag.ToLower().Contains(qualityPref.ToLower()))
                    hdChannelsList.Add(kvp.Value);
                else if (kvp.Value.lang.Length == 0 || kvp.Value.lang.ToLower().Contains(langPref.ToLower()))
                    usChannelsList.Add(kvp.Value);
                else
                    otherChannelsList.Add(kvp.Value);
            }

            //Start the list we're returning by a sort  (in the future put cool heuristics here)
            sortedList = hdChannelsList.OrderBy(o => o.number).ToList();
            sortedList.AddRange(usChannelsList);
            sortedList.AddRange(otherChannelsList);

            return sortedList.ToArray();
        }

        public ChannelInfo GetChannel(string channel)
        {
            ChannelInfo channelInfo=null;
            channelDict.TryGetValue(channel, out channelInfo);
            return channelInfo;
        }

        public void AddUpdateChannel(string channel, string channelQuality, string lang)
        {
            ChannelInfo channelInfo = BuildChannelInfo(channel, channelQuality, lang);
            AddUpdateChannel(channel, channelInfo);
        }

        public void AddUpdateChannel(string channel, ChannelInfo channelInfo)
        {
            //Must be 2 digits
            if (channel.Length == 1)
                channel = "0" + channel;

            //If already exists, update
            if (channelDict.ContainsKey(channel))
            {
                channelDict[channel] = channelInfo;
            }
            else
            {
                //Add new
                channelDict.Add(channel, channelInfo);
            }
        }

        private ChannelInfo BuildChannelInfo(string channel, string quality,string lang)
        {
            if (channel.Length == 1)
                channel = "0" + channel;

            ChannelInfo channelInfo = new ChannelInfo();
            channelInfo.number = channel;
            channelInfo.description = channel + " (" + quality + "/" + lang + ") ";
            channelInfo.ratio = 0;
            channelInfo.qualityTag = quality;
            channelInfo.lang = lang;

            return channelInfo;
        }

        public int GetNumberOfChannels()
        {
            return channelDict.Count;
        }
    }
}