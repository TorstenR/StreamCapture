using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading;

namespace StreamCapture
{
    public class Schedule
    {
        private Dictionary<string, ScheduleChannels> scheduleChannelDict;

        public async Task LoadSchedule(string scheduleURL, string debugCmdLine)
        {      
            string schedString="";    
            int retries=3;
            while(true)
            {
                try
                {
                    //try and deserialize
                    schedString = await GetSchedule(scheduleURL, debugCmdLine);
                    scheduleChannelDict = JsonConvert.DeserializeObject<Dictionary<string, ScheduleChannels>>(schedString); 
                    break;  //success
                }
                catch
                {
                    if(--retries == 0) //are we out of retries?
                    {
                        Console.WriteLine("======================");
                        Console.WriteLine($"{DateTime.Now}: ERROR - Exception deserializing schedule json");
                        Console.WriteLine("======================");
                        Console.WriteLine($"JSON: {schedString}");

                        throw;  //throw exception up the stack
                    }
                    else 
                    {
                        Thread.Sleep(5000);
                    }
                }
            }
        }

        private async Task<string> GetSchedule(string scheduleURL, string debugCmdLine)
        {
            string schedString;

            using (var client = new HttpClient())
            {
                if(string.IsNullOrEmpty(debugCmdLine))
                {
                    Uri uri = new Uri(scheduleURL);
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

            return schedString;  
        }

        public List<ScheduleShow> GetScheduledShows()
        {
            List<ScheduleShow> scheduledShowList = new List<ScheduleShow>();
            foreach (KeyValuePair<string, ScheduleChannels> kvp in scheduleChannelDict)
            {
                if(kvp.Value.items!=null)
                    scheduledShowList.AddRange(kvp.Value.items);
            }

            return scheduledShowList;
        }
    }
}