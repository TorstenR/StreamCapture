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
    public class KeywordInfo
    {
        public string keywords { get; set; }
        public string exclude { get; set; }
        public int preMinutes { get; set; }
        public int postMinutes { get; set; }
        public string langPref { get; set; }
        public string qualityPref { get; set; }
    }
}
