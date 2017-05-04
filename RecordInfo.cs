using System;
using System.ComponentModel;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StreamCapture
{
    [JsonConverter(typeof(RecordInfoSerializer))]
    public class RecordInfo
    {
        public string strDuration { get; set; }
        public string strStartDT { get; set; }
        public string strEndDT { get; set; }
        public string strDTOffset  { get; set; }

        public string id { get; set; }
        public string fileName { get; set; }
        public string description { get; set; }
        public int preMinutes { get; set; }
        public int postMinutes { get; set; }
        public bool starredFlag { get; set; }
        public bool emailFlag {get; set; }
        public string qualityPref { get; set; }
        public string langPref { get; set; }
        public string channelPref { get; set; }
        public string category { get; set; }
        public int keywordPos { get; set;}

        public bool queuedFlag { get; set; }
        public bool processSpawnedFlag  { get; set; }
        public bool tooManyFlag { get; set; }
        public bool partialFlag { get; set; }
        public bool completedFlag { get; set; }
        public bool ignoreFlag { get; set;}

        public Channels channels;

        public RecordInfo()
        {
            //Init certain properties 
            id=DateTime.Now.Ticks.ToString();
            queuedFlag=false;
            processSpawnedFlag=false;
            completedFlag=false;
            partialFlag=false;
            tooManyFlag=false;
            strDTOffset="0";
            qualityPref="";
            langPref="";
            category="";
        }

        public DateTime GetStartDT()
        {
            if(strStartDT == null)
                return DateTime.Now;

            //Create base date time
            DateTime startDT=DateTime.Parse(strStartDT);

            //subtract pre time 
            int preMin =preMinutes*-1;
            startDT=startDT.AddMinutes(preMin);

            //Add offset 
            int timeOffset=Convert.ToInt32(strDTOffset);
            startDT=startDT.AddHours(timeOffset);

            return startDT;
        }

        public DateTime GetEndDT()
        {
            if(strEndDT == null)
                return DateTime.Now;

            //Create base date time
            DateTime endDT=DateTime.Parse(strEndDT);

            //Add offset 
            int timeOffset=Convert.ToInt32(strDTOffset);
            endDT=endDT.AddHours(timeOffset);

            return endDT;
        }

        //Return duration in minutes
        public int GetDuration()
        {
            int duration = Convert.ToInt32(strDuration);
            duration = duration + preMinutes + postMinutes;
            return duration;
        }
    }

    public class RecordInfoSerializer : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            RecordInfo recordInfo = value as RecordInfo;

            //ID
            writer.WriteStartObject();
            writer.WritePropertyName("ID");
            serializer.Serialize(writer, recordInfo.id);

            //description
            writer.WritePropertyName("Description");
            serializer.Serialize(writer, recordInfo.description);

            //start date
            writer.WritePropertyName("StartDT");
            serializer.Serialize(writer, recordInfo.GetStartDT());

            //Duration
            writer.WritePropertyName("Duration");
            serializer.Serialize(writer, recordInfo.GetDuration());

            //too many flag
            writer.WritePropertyName("TooManyFlag");
            serializer.Serialize(writer, recordInfo.tooManyFlag);
            
            //Queued flag
            writer.WritePropertyName("QueuedFlag");
            serializer.Serialize(writer, recordInfo.queuedFlag);
            
            //started flag
            writer.WritePropertyName("StartedFlag");
            serializer.Serialize(writer, recordInfo.processSpawnedFlag);
            
            //Partial flag
            writer.WritePropertyName("PartialFlag");
            serializer.Serialize(writer, recordInfo.partialFlag);

            //Completed flag
            writer.WritePropertyName("CompletedFlag");
            serializer.Serialize(writer, recordInfo.completedFlag);
            
            //Ignored Flag
            writer.WritePropertyName("IgnoredFlag");
            serializer.Serialize(writer, recordInfo.ignoreFlag);
            writer.WriteEndObject();                       
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            //Console.WriteLine("CanConvert!!!!!!!");
            return TypeDescriptor.GetConverter(objectType).CanConvertTo(typeof(RecordInfo));
        }
    }    
}