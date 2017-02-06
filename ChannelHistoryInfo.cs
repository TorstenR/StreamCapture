using System;
using System.Collections.Generic;

namespace StreamCapture
{
    public class ChannelHistoryInfo
    {
        public string channel { get; set; }
        public double hoursRecorded { get; set; }
        public long recordingsAttempted { get; set; }
        public Dictionary<string,long> serverSpeed { get; set; }
        public DateTime lastAttempt { get; set; }
        public DateTime lastSuccess { get; set; }
        public bool activeFlag { get; set; }
    }
}