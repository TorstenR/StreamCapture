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
    public class ChannelHistoryInfo
    {
        public string channel { get; set; }
        public double hoursRecorded { get; set; }
        public long recordingsAttempted { get; set; }
        public long errors { get; set; }
        public DateTime lastAttempt { get; set; }
        public DateTime lastSuccess { get; set; }
        public bool activeFlag { get; set; }
    }
}