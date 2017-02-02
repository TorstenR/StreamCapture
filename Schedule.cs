using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StreamCapture
{
    public class Schedule
    {
        private Dictionary<string, ScheduleChannels> scheduleChannelDict;

        public Schedule()
        {
            LoadSchedule().Wait();
        }

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