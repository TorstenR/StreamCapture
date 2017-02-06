using System;
using System.Linq;
using System.Collections.Generic;

namespace StreamCapture
{
    public class Servers
    {
        private List<ServerInfo> serverList;

        public Servers(string strServers)
        {
            serverList = new List<ServerInfo>();

            string[] serverArray = strServers.Split(',');
            foreach (string server in serverArray)
            {
                serverList.Add(new ServerInfo(server,0));
            }
        }

        public List<ServerInfo> GetServerList()
        {
            //return serverList.Select(s => s.server).ToArray();
            return serverList;
        }
    }
}