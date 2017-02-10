using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StreamCapture
{
    public class Schedule
    {
        private Dictionary<string, ScheduleChannels> scheduleChannelDict;

        public async Task LoadSchedule(string debugCmdLine)
        {
            string schedString;
            using (var client = new HttpClient())
            {
                if(string.IsNullOrEmpty(debugCmdLine))
                {
                    Uri uri = new Uri("https://iptvguide.netlify.com/iptv.json");
                    var response = await client.GetAsync(uri);
                    response.EnsureSuccessStatusCode(); // Throw in not success
                    schedString = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    using (StreamReader sr = File.OpenText("testschedule.json"))
                    {
                        schedString = sr.ReadToEnd();
                    }
                }
            }

            //create schedule objects
            scheduleChannelDict = JsonConvert.DeserializeObject<Dictionary<string, ScheduleChannels>>(schedString);
        }

        public List<ScheduleShow> GetScheduledShows()
        {
            List<ScheduleShow> scheduledShowList = new List<ScheduleShow>();
            foreach (KeyValuePair<string, ScheduleChannels> kvp in scheduleChannelDict)
            {
                scheduledShowList.AddRange(kvp.Value.items);
            }

            return scheduledShowList;
        }
    }
}