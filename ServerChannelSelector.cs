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
            //Final sorted list
            sortedTupleList = new List<Tuple<string,ChannelInfo,long>>();

            //Let's start by creating a sorted (by score) channel list, with highest scoring first
            List<ChannelInfo> sortedChannelList = new List<ChannelInfo>();
            List<ChannelInfo> channelInfoLIst = recordInfo.channels.GetChannels();
            foreach (ChannelInfo channelInfo in channelInfoLIst)
            {
                //Determine score and create sorted channel list
                int channelScore=DetermineChannelScore(channelInfo);
                channelInfo.score=channelScore;
                sortedChannelList=AddToSortedList(channelInfo,sortedChannelList);
            }

            //Now let's add servers based on speed to for each channel          
            foreach (ChannelInfo channelInfoScore in sortedChannelList)
            {
                //new list
                List<Tuple<string,ChannelInfo,long>> tempSortedTupleList = new List<Tuple<string,ChannelInfo,long>>();

                //Insert based on channel history of bytes per second
                tempSortedTupleList=InsertChannelInfo(tempSortedTupleList,channelInfoScore);

                //Now add to the end of the actual sorted list
                sortedTupleList.AddRange(tempSortedTupleList);
            }

            //Let's dump this
            logWriter.WriteLine($"{DateTime.Now}: Using the following order of server/channel Pairs:");
            foreach(Tuple<string,ChannelInfo,long> tuple in sortedTupleList)
            {
                logWriter.WriteLine($"                   Server: {tuple.Item1} Channel: {tuple.Item2.description} Score: {tuple.Item2.score} Historical Rate: {tuple.Item3}KB/s");
            }
        }

        private List<ChannelInfo> AddToSortedList(ChannelInfo channelScoreToAdd, List<ChannelInfo> channelScoreList)
        {
            //Add to a sorted list based on score
            ChannelInfo[] channelScoresArray = channelScoreList.ToArray();
            for(int idx=0;idx<channelScoresArray.Length;idx++)
            {
                if(channelScoreToAdd.score>channelScoresArray[idx].score)
                {
                    channelScoreList.Insert(idx,channelScoreToAdd);
                    return channelScoreList;
                }
            }

            //If we've made it this far, then add to the end
            channelScoreList.Add(channelScoreToAdd);
            return channelScoreList;
        }

        //Take a single channel and insert it into the server list
        private List<Tuple<string,ChannelInfo,long>> InsertChannelInfo(List<Tuple<string,ChannelInfo,long>> tupleList,ChannelInfo channelInfo)
        {
            List<string> serverList=servers.GetServerList();
            foreach(string server in serverList)
            {
                bool insertedFlag=false;
                long avgKBytesSec=channeHistory.GetAvgKBytesSec(server,channelInfo.number);

                //Let's insert appropriately
                for(int index=0;index < tupleList.Count;index++)
                {
                    Tuple<string,ChannelInfo,long> tuple = tupleList[index];
                    if(avgKBytesSec > tuple.Item3)
                    {
                        tupleList.Insert(index,new Tuple<string,ChannelInfo,long>(server,channelInfo,avgKBytesSec));
                        insertedFlag=true;
                        break;
                    }
                }

                //If not inserted, let's add it to the end
                if(!insertedFlag)
                {
                    tupleList.Add(new Tuple<string,ChannelInfo,long>(server,channelInfo,avgKBytesSec));
                }
            }
        
            return tupleList;
        }

        //Determine score for channel.  The higher the score, the higher in the list 
        private int DetermineChannelScore(ChannelInfo channelInfo)
        {
            //Get quality preference score
            int qualScore=DetermineScore(channelInfo.qualityTag,recordInfo.qualityPref);

            //Get lang preference score
            int langScore=DetermineScore(channelInfo.lang,recordInfo.langPref);

            //Get channel preference score
            int chanScore=DetermineScore(channelInfo.number,recordInfo.channelPref);

            //total
            int score = qualScore+langScore+chanScore;

            return score;
        }

        private int DetermineScore(string pref,string stringList)
        {
            int score=0;
            string[] strArray = stringList.Split(',');
            foreach(string str in strArray)
            {
                //Determine potential score, plus strip score chars
                int potentialScore=0;  
                string newStr=DeterminePotentialScore(str,out potentialScore);

                //Now let's see if there's a match.  If so, add the score
                if(!string.IsNullOrEmpty(newStr) && pref.ToLower().Contains(newStr.ToLower()))
                    score=score+potentialScore; 
            }

            return score;
        }

        //Used to count '+' and '-' for scoring channels
        private string DeterminePotentialScore(string str,out int score)
        {
            char chr1='+'; //add 1
            char chr2='-'; //sub 1

            //so we don't "ruin" the old one
            string newStr=str;

            //Find all instances of chr1, count them and add them
            score=0; 
            foreach(char ch in str)
            {
                if(ch==chr1)
                    score++;
                if(ch==chr2)
                    score--;
            }

            //If score is zero or bigger, add the default '1' for the match itself
            if(score>=0)
                score++;

            //get rid of chr1 and chr2
            newStr = newStr.Replace(chr1.ToString(), "");
            newStr = newStr.Replace(chr2.ToString(), "");

            return newStr;
        }
    }
}
