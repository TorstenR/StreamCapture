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
    public class Schedule
    {
        JObject jsonScheduleObject;

        public async Task LoadSchedule()
        {
            string schedString;
            using (var client = new HttpClient())
            {
                Uri uri = new Uri("https://iptvguide.netlify.com/iptv.json");
                var response = await client.GetAsync(uri);
                response.EnsureSuccessStatusCode(); // Throw in not success
                schedString = await response.Content.ReadAsStringAsync();
            }

            jsonScheduleObject = JObject.Parse(schedString);
        }

        //Returns dictionary of all shows (record info) that match keywords
        public Dictionary<string, RecordInfo> GetRecordSchedule(Keywords keywords, IConfiguration configuration)
        {
            Dictionary<string, RecordInfo> recInfoDict = new Dictionary<string, RecordInfo>();

            IEnumerable<JToken> channels = jsonScheduleObject.SelectTokens("$..items");
            foreach (JToken channelInfo in channels)
            {
                IEnumerable<JToken> channelContent = channelInfo.Children();
                foreach (JToken show in channelContent)
                {
                    string keyValue = show["name"].ToString() + show["time"].ToString();
                    KeywordInfo keywordInfo = keywords.FindMatch(show["name"].ToString());
                    if (keywordInfo != null)
                    {
                        //Build record info 
                        RecordInfo recordInfo;
                        if (recInfoDict.ContainsKey(keyValue))
                            recordInfo = recInfoDict[keyValue];
                        else
                            recordInfo = new RecordInfo();

                        //Build channel list
                        recordInfo.AddChannel(show["channel"].ToString(), show["quality"].ToString());

                        recordInfo.id = show["id"].ToString();
                        recordInfo.description = show["name"].ToString();
                        recordInfo.strStartDT = show["time"].ToString();
                        recordInfo.strEndDT = show["end_time"].ToString();
                        recordInfo.strDuration = show["runtime"].ToString();
                        recordInfo.strDTOffset = configuration["schedTimeOffset"];
                        recordInfo.preMinutes = keywordInfo.preMinutes;
                        recordInfo.postMinutes = keywordInfo.postMinutes;

                        //Clean up description, and then use as filename
                        recordInfo.fileName = show["name"].ToString() + show["id"].ToString();
                        string myChars = @"|'/\ ,<>#@!+_-&^*()~`";
                        string invalidChars = myChars + new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                        foreach (char c in invalidChars)
                        {
                            recordInfo.fileName = recordInfo.fileName.Replace(c.ToString(), "");
                        }

                        //Update or add
                        if (recInfoDict.ContainsKey(keyValue))
                            recInfoDict[keyValue] = recordInfo;
                        else
                            recInfoDict.Add(keyValue, recordInfo);
                    }
                }
            }

            return recInfoDict;
        }
    }
}