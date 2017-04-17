using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;

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

        //Refreshes channel list for a given recording so we can catch any unexpected last minute changes since it was first queued
        public void RefreshChannelList(IConfiguration configuration, RecordInfo recordInfo)
        {
            //Grab the latest schedule
            Schedule schedule = new Schedule();
            schedule.LoadSchedule(configuration["scheduleURL"], configuration["debug"]).Wait();
            List<ScheduleShow> scheduleShowList = schedule.GetScheduledShows();

            //Create new channels object and fill w/ latest by going through the schedule again
            Channels refreshedChannels = new Channels();
            foreach (ScheduleShow scheduleShow in scheduleShowList)
            {
                if (recordInfo.description == scheduleShow.name && recordInfo.strStartDT == scheduleShow.time)
                {
                    refreshedChannels.AddUpdateChannel(scheduleShow.channel, scheduleShow.quality, scheduleShow.language);
                }
            }

            //Update channel list if it was found
            if (refreshedChannels.GetNumberOfChannels() > 0)
            {
                recordInfo.channels = refreshedChannels;
            }
            else
            {
                string body = recordInfo.description + " is no longer scheduled.  (Was originally scheduled for " + recordInfo.strStartDT + ")";
                new Mailer().SendErrorMail(configuration, recordInfo.description + " no longer scheduled!", body);
                throw new Exception("Show no longer on the schedule.  Aborting...");
            }
        }
    }
}