using System.Linq;
using System.Collections.Generic;

namespace StreamCapture
{
    public class Channels
    {
        private Dictionary<string, ChannelInfo> channelDict;

        public Channels()
        {
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

        public ChannelInfo GetChannel(string channel)
        {
            ChannelInfo channelInfo=null;
            channelDict.TryGetValue(channel, out channelInfo);
            return channelInfo;
        }

        public List<ChannelInfo> GetChannels()
        {
            return channelDict.Values.ToList();
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