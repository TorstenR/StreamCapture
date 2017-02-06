using System;
using System.Linq;
using System.Collections.Generic;

namespace StreamCapture
{
    public class Servers
    {
        private List<String> serverList;

        public Servers(string strServers)
        {
            serverList = new List<string>();

            string[] serverArray = strServers.Split(',');
            foreach (string server in serverArray)
            {
                serverList.Add(server);
            }
        }

        public List<string> GetServerList()
        {
            //return serverList.Select(s => s.server).ToArray();
            return serverList;
        }
    }
}