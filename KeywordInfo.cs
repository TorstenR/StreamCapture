using System.Collections.Generic;

namespace StreamCapture
{
    public class KeywordInfo
    {
        public bool starredFlag { get; set; }
        public bool emailFlag { get; set; }
        public List<string> keywords { get; set; }
        public List<string> exclude { get; set; }
        public int preMinutes { get; set; }
        public int postMinutes { get; set; }
        public string langPref { get; set; }
        public string qualityPref { get; set; }
        public string categoryPref { get; set; }
        public string channelPref { get; set; }
    }
}
