using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace StreamCapture
{
    public class Recordings
    {
        private IConfiguration configuration;
        private Dictionary<string, RecordInfo> recordDict;
        private Schedule schedule;
        private Keywords keywords;

        public Recordings(IConfiguration _configuration)
        {
            recordDict = new Dictionary<string, RecordInfo>();
            configuration = _configuration;
            schedule = new Schedule();
            keywords = new Keywords();
        }

        public void AddUpdateRecordInfo(string recordInfoKey,RecordInfo recordInfo)
        {
            if(recordDict.ContainsKey(recordInfoKey))
                recordDict[recordInfoKey]=recordInfo;
            else
                recordDict.Add(recordInfoKey,recordInfo);
        }

        public List<RecordInfo> GetScheduledRecordings()
        {
            return recordDict.Values.ToList();
        }

        public RecordInfo GetRecordInfo(string recordInfoKey)
        {
            RecordInfo recordInfo=null;
            bool recFoundFlag=recordDict.TryGetValue(recordInfoKey,out recordInfo);

            //Add new if not found
            if(!recFoundFlag)
                recordInfo=new RecordInfo();

            return recordInfo;
        }
        
        public List<RecordInfo> BuildRecordSchedule()
        {
            //Refresh from website
            schedule.LoadSchedule(configuration["debug"]).Wait();
            List<ScheduleShow> scheduleShowList = schedule.GetScheduledShows();

            //Go through the shows and load up recordings if there's a match
            foreach(ScheduleShow scheduleShow in scheduleShowList)
            {
                string keyValue = scheduleShow.name + scheduleShow.time;

                //Find any shows that match
                Tuple<KeywordInfo,int> tuple = keywords.FindMatch(scheduleShow.name);   
                KeywordInfo keywordInfo = tuple.Item1; 
                if (keywordInfo != null)
                {
                    //Build record info if already exists, otherwise, create new                 
                    RecordInfo recordInfo=GetRecordInfo(keyValue);

                    //Fill out the recording info
                    recordInfo.channels.AddUpdateChannel(scheduleShow.channel, scheduleShow.quality, scheduleShow.language);
                    recordInfo.id = scheduleShow.id;
                    recordInfo.description = scheduleShow.name;
                    recordInfo.strStartDT = scheduleShow.time;
                    //recordInfo.strStartDT = DateTime.Now.AddHours(4).ToString();
                    recordInfo.strEndDT = scheduleShow.end_time;
                    recordInfo.strDuration = scheduleShow.runtime;
                    //recordInfo.strDuration = "1";
                    recordInfo.strDTOffset = configuration["schedTimeOffset"];
                    recordInfo.preMinutes = keywordInfo.preMinutes;
                    recordInfo.postMinutes = keywordInfo.postMinutes;
                    recordInfo.qualityPref = keywordInfo.qualityPref;
                    recordInfo.langPref = keywordInfo.langPref;
                    recordInfo.category = scheduleShow.category;

                    recordInfo.keywordPos = tuple.Item2;  //used for sorting the most important shows 

                    //Clean up description, and then use as filename
                    recordInfo.fileName = scheduleShow.name.Replace(' ','_');
                    string myChars = @"|'/\ ,<>#@!+&^*()~`;";
                    string invalidChars = myChars + new string(Path.GetInvalidFileNameChars());
                    foreach (char c in invalidChars)
                    {
                        recordInfo.fileName = recordInfo.fileName.Replace(c.ToString(), "");
                    }

                    //Update or add
                    AddUpdateRecordInfo(keyValue,recordInfo);
                }
            }

            //Return ordered list based on keyword position
            return SortBasedOnPos(recordDict.Values.ToList());
        }

        private List<RecordInfo> SortBasedOnPos(List<RecordInfo> listToBeSorted)
        {
            List<RecordInfo> sortedList=new List<RecordInfo>();

            foreach(RecordInfo recordInfo in listToBeSorted)
            {
                bool insertedFlag=false;
                RecordInfo[] sortedArray = sortedList.ToArray();
                for(int idx=0;idx<sortedArray.Length;idx++)
                {
                    if(recordInfo.keywordPos<=sortedArray[idx].keywordPos)
                    {
                        sortedList.Insert(idx,recordInfo);
                        insertedFlag=true;
                        break;
                    }
                }

                //Not found, so add to the end
                if(!insertedFlag)
                {
                    sortedList.Add(recordInfo);
                }
            }

            return sortedList;
        }
    }
}