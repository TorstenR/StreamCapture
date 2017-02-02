using System.Collections.Generic;

namespace StreamCapture
{
    public class ScheduleChannels
    {
        public string channel_id { get; set; }
        public string img { get; set; }
        public List<ScheduleShow> items { get; set; }
        public string name { get; set; }
    }
    public class ScheduleShow
    {
        public string category { get; set; }
        public string channel { get; set; }
        public string description { get; set; }
        public string end_time { get; set; }
        public string id { get; set; }
        public string language { get; set; }
        public string name { get; set; }
        public string network_id { get; set; }
        public string parent_id { get; set; }
        public string quality { get; set; }
        public string runtime { get; set; }
        public string time { get; set; }
        public string timered { get; set; }
        public string version { get; set; }
    }
}