

namespace StreamCapture
{
    public class ServerInfo
    {
        public ServerInfo(string _s,long _a)
        {
            server=_s;
            avgKBytesSec=_a;
        }
        public string server { get; set; }
        public long avgKBytesSec { get; set; }
    }
}