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
        private List<Tuple<string,ChannelInfo,long>> sortedTupleList;


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

        public bool IsBestSelected()
        {
            return bestTupleSelectedFlag;
        }

        public string GetServerName()
        {
            return sortedTupleList[tupleIdx].Item1;
        }

        public string GetChannelNumber()
        {
            return sortedTupleList[tupleIdx].Item2.number;
        }

        public void SetAvgKBytesSec(long avgKBytesSec)
        {
            ChannelInfo currentChannel=sortedTupleList[tupleIdx].Item2;
            string currentServer=sortedTupleList[tupleIdx].Item1;

            //Replace avgKB w/ the updated value we just observed
            sortedTupleList[tupleIdx]=new Tuple<string,ChannelInfo,long>(currentServer,currentChannel,avgKBytesSec);
        }

        public Tuple<string,ChannelInfo,long>  GetNextServerChannel()
        {
                //Make sure we don't have one already selected
                if(bestTupleSelectedFlag)
                    return sortedTupleList[tupleIdx];

                //more pairs exist?
                tupleIdx++;
                if(tupleIdx < sortedTupleList.Count)
                {
                    logWriter.WriteLine($"{DateTime.Now}: Switching Server/Channel Pairs: {sortedTupleList[tupleIdx].Item1}/{sortedTupleList[tupleIdx].Item2.number} Historical Rate: {sortedTupleList[tupleIdx].Item3}KB/s");
                    return sortedTupleList[tupleIdx];
                }

                //We've been through the whole list, let's grab the one w/ the best rate
                long bestAvgKB=0;
                for(int idx=0;idx<sortedTupleList.Count;idx++)
                {
                    Tuple<string,ChannelInfo,long> tuple=sortedTupleList[idx];
                    if(tuple.Item3>=bestAvgKB)
                    {
                        bestAvgKB=tuple.Item3;
                        tupleIdx=idx;
                    }
                }

                logWriter.WriteLine($"{DateTime.Now}: Now using Server/Channel Pair: {sortedTupleList[tupleIdx].Item1}/{sortedTupleList[tupleIdx].Item2.number} Historical Rate: {sortedTupleList[tupleIdx].Item3}KB/s for the rest of the capture");

                bestTupleSelectedFlag=true;
                return sortedTupleList[tupleIdx];;

        }
        private void BuildSortedTupleList()
        {
            //We'll sort inside of two major categories
            List<Tuple<string,ChannelInfo,long>> preferredChannelsList = new List<Tuple<string,ChannelInfo,long>>();
            List<Tuple<string,ChannelInfo,long>> otherChannelsList = new List<Tuple<string,ChannelInfo,long>>();

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
            sortedTupleList = new List<Tuple<string,ChannelInfo,long>>();
            sortedTupleList.AddRange(preferredChannelsList);
            sortedTupleList.AddRange(otherChannelsList);

            //Let's dump this
            logWriter.WriteLine($"{DateTime.Now}: Using the following order of server/channel Pairs:");
            foreach(Tuple<string,ChannelInfo,long> tuple in sortedTupleList)
            {
                logWriter.WriteLine($"                   Server: {tuple.Item1} Channel: {tuple.Item2.description} Historical Rate: {tuple.Item3}KB/s");
            }
        }

        private void InsertChannelInfo(List<Tuple<string,ChannelInfo,long>> tupleList,ChannelInfo channelInfo)
        {
            List<string> serverList=servers.GetServerList();
            foreach(string server in serverList)
            {
                long avgKBytesSec=channeHistory.GetAvgKBytesSec(server,channelInfo.number);

                //Let's insert appropriately
                for(int index=0;index < tupleList.Count;index++)
                {
                    Tuple<string,ChannelInfo,long> tuple = tupleList[index];
                    if(avgKBytesSec > tuple.Item3)
                    {
                        tupleList.Insert(index,new Tuple<string,ChannelInfo,long>(server,channelInfo,avgKBytesSec));
                        break;
                    }
                }

                //For cases where this is the first one or avg was zero
                tupleList.Add(new Tuple<string,ChannelInfo,long>(server,channelInfo,avgKBytesSec));
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