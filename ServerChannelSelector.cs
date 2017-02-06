using System;
using System.IO;
using System.Collections.Generic;

namespace StreamCapture
{
    public class ServerChannelSelector
    {
        private TextWriter logWriter;
        private ChannelHistory channeHistory;
        private Servers servers;
        private RecordInfo recordInfo;
        private List<Tuple<ServerInfo,ChannelInfo,long>> sortedTupleList;


        private int tupleIdx=0;
        private bool bestTupleSelectedFlag = false;

        public ServerChannelSelector(TextWriter _l,ChannelHistory _ch,Servers _s,RecordInfo _ri)
        {
            logWriter=_l;
            channeHistory=_ch;
            servers=_s;
            recordInfo=_ri;

            //Let's build our sorted list now
            BuildSortedTupleList();
        }

        public string GetServerName()
        {
            return sortedTupleList[tupleIdx].Item1.server;
        }

        public string GetChannelNumber()
        {
            return sortedTupleList[tupleIdx].Item2.number;
        }

        public void SetAvgKBytesSec(long avgKBytesSec)
        {
            ChannelInfo currentChannel=sortedTupleList[tupleIdx].Item2;
            ServerInfo currentServer=sortedTupleList[tupleIdx].Item1;

            currentChannel.avgKBytesSec = (currentChannel.avgKBytesSec+avgKBytesSec)/2;
            currentServer.avgKBytesSec = (currentServer.avgKBytesSec+avgKBytesSec)/2;

            logWriter.WriteLine($"{DateTime.Now}: Setting {currentChannel.avgKBytesSec}KB/s for channel {currentChannel.number} and {currentServer.avgKBytesSec}KB/s for server {currentServer.server}");

        }

        public Tuple<ServerInfo,ChannelInfo,long>  GetNextServerChannel()
        {
                //Make sure we don't have one already selected
                if(bestTupleSelectedFlag)
                    return sortedTupleList[tupleIdx];

                //more pairs exist?
                tupleIdx++;
                if(tupleIdx < sortedTupleList.Count)
                {
                    logWriter.WriteLine($"{DateTime.Now}: Switching to Server: {sortedTupleList[tupleIdx].Item1.server} Channel: {sortedTupleList[tupleIdx].Item2.number} Historical Rate: {sortedTupleList[tupleIdx].Item3}KB/s");
                    return sortedTupleList[tupleIdx];
                }

                //We've been through the whole list, let's grab the one w/ the best rate
                long bestAvgKB=0;
                for(int idx=0;idx<sortedTupleList.Count;idx++)
                {
                    Tuple<ServerInfo,ChannelInfo,long> tuple=sortedTupleList[idx];
                    if(tuple.Item3>bestAvgKB)
                    {
                        bestAvgKB=tuple.Item3;
                        tupleIdx=idx;
                    }
                }

                logWriter.WriteLine($"{DateTime.Now}: Now using Server: {sortedTupleList[tupleIdx].Item1.server} Channel: {sortedTupleList[tupleIdx].Item2.number} Historical Rate: {sortedTupleList[tupleIdx].Item3}KB/s for the rest of the capture");

                bestTupleSelectedFlag=true;
                return sortedTupleList[tupleIdx];;

        }
        private void BuildSortedTupleList()
        {
            //We'll sort inside of two major categories
            List<Tuple<ServerInfo,ChannelInfo,long>> preferredChannelsList = new List<Tuple<ServerInfo,ChannelInfo,long>>();
            List<Tuple<ServerInfo,ChannelInfo,long>> otherChannelsList = new List<Tuple<ServerInfo,ChannelInfo,long>>();

            //Let's start by looping through the channels and updating the lists.  We'
            List<ChannelInfo> channelInfoLIst = recordInfo.channels.GetChannels();
            foreach (ChannelInfo channelInfo in channelInfoLIst)
            {
                 //Insert based on channel history of bytes per second
                if(IsPreferred(channelInfo))
                {
                    InsertChannelInfo(preferredChannelsList,channelInfo);
                }
                else
                {
                    InsertChannelInfo(otherChannelsList,channelInfo);
                }
            }

            //Final sorted list
            sortedTupleList = new List<Tuple<ServerInfo,ChannelInfo,long>>();
            sortedTupleList.AddRange(preferredChannelsList);
            sortedTupleList.AddRange(otherChannelsList);

            //Let's dump this
            logWriter.WriteLine($"{DateTime.Now}: Using the following order of server/channel Pairs:");
            foreach(Tuple<ServerInfo,ChannelInfo,long> tuple in sortedTupleList)
            {
                logWriter.WriteLine($"                    Server: {tuple.Item1.server} Channel: {tuple.Item2.description} Historical Rate: {tuple.Item3}KB/s");
            }
        }

        private void InsertChannelInfo(List<Tuple<ServerInfo,ChannelInfo,long>> tupleList,ChannelInfo channelInfo)
        {
            List<ServerInfo> serverList=servers.GetServerList();
            foreach(ServerInfo serverInfo in serverList)
            {
                long avgKBytesSec=channeHistory.GetAvgKBytesSec(serverInfo.server,channelInfo.number);

                //Let's insert appropriately
                for(int index=0;index < tupleList.Count;index++)
                {
                    Tuple<ServerInfo,ChannelInfo,long> tuple = tupleList[index];
                    if(avgKBytesSec > tuple.Item3)
                    {
                        tupleList.Insert(index,new Tuple<ServerInfo,ChannelInfo,long>(serverInfo,channelInfo,avgKBytesSec));
                        return;
                    }
                }

                //For cases where this is the first one or avg was zero
                tupleList.Add(new Tuple<ServerInfo,ChannelInfo,long>(serverInfo,channelInfo,avgKBytesSec));
            }
        }

        private bool IsPreferred(ChannelInfo channelInfo)
        {
            //Get quality and lang preferences
            string[] qualityPrefArray = recordInfo.qualityPref.Split(',');
            string[] langPrefArray = recordInfo.langPref.Split(',');

            foreach(string qp in qualityPrefArray)
            {
                foreach(string lp in langPrefArray)
                {
                    if(channelInfo.qualityTag.ToLower().Contains(qp.ToLower()) &&
                        channelInfo.lang.ToLower().Contains(lp.ToLower()))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}