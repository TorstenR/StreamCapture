using System;
using System.Linq;
using System.Collections.Generic;

namespace StreamCapture
{
    public class Servers
    {     
        private List<Tuple<string,long>> serverList;

        public Servers(string strServers)
        {
            serverList = new List<Tuple<string,long>>();

            string[] serverArray = strServers.Split(',');
            foreach (string server in serverArray)
            {
                serverList.Add(new Tuple<string,long>(server,0));
            }
        }

        public string[] GetServerList()
        {
            return serverList.Select(item => item.Item1).ToArray();
        }

        public string GetServerName(int serverIdx)
        {
            Tuple<string,long> retVal=serverList.ElementAt(serverIdx);
            return retVal.Item1;
        }

        public long GetServerAvgRate(int serverIdx)
        {
            Tuple<string,long> retVal=serverList.ElementAt(serverIdx);
            return retVal.Item2;
        }

        public int GetNumberOfServers()
        {
            return serverList.Count();
        }

        public void SetAvgKBytesPerSec(long avgKBytesSec,int serverIdx)
        {
            Tuple<string,long> retVal=serverList.ElementAt(serverIdx);
            serverList[serverIdx]=new Tuple<string,long>(retVal.Item1,(retVal.Item2+avgKBytesSec)/2);
        }
    }
}