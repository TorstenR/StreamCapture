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
        
        //Dictionary of all shows we're interested in recording
        private Dictionary<string, RecordInfo> recordDict;
        private Schedule schedule;

        //List of queued shows in datetime order
        private List<RecordInfo> queuedRecordings;


        public Recordings(IConfiguration _configuration)
        {
            recordDict = new Dictionary<string, RecordInfo>();
            configuration = _configuration;
            schedule = new Schedule();
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
            //Refresh keywords
            Keywords keywords = new Keywords(configuration);

            //Refresh from website
            schedule.LoadSchedule(configuration["debug"]).Wait();
            List<ScheduleShow> scheduleShowList = schedule.GetScheduledShows();

            //Go through the shows and load up recordings if there's a match
            foreach(ScheduleShow scheduleShow in scheduleShowList)
            {
                string keyValue = scheduleShow.name + scheduleShow.time;

                //Find any shows that match
                Tuple<KeywordInfo,int> tuple = keywords.FindMatch(scheduleShow.name);   
                if (tuple != null)
                {
                    KeywordInfo keywordInfo = tuple.Item1; 

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

            //Return shows that should actually be queued (omitted those already done, too far in the future, etc...)
            return GetShowsToQueue();
        }        

        private void AddUpdateRecordInfo(string recordInfoKey,RecordInfo recordInfo)
        {
            if(recordDict.ContainsKey(recordInfoKey))
                recordDict[recordInfoKey]=recordInfo;
            else
                recordDict.Add(recordInfoKey,recordInfo);
        }

        private void DeleteRecordInfo(RecordInfo recordInfoToDelete)
        {
            recordDict.Remove(recordInfoToDelete.description + recordInfoToDelete.strStartDT);
        }

        private List<RecordInfo> GetShowsToQueue()
        {
            //Build mail to send out
            Mailer mailer = new Mailer();
            string concurrentShowText = "";
            string currentScheduleText="";

            //Starting new as this is always time dependent
            queuedRecordings=new List<RecordInfo>();

            //List for items we need to remove from the recordInfoList (we can't while in the loop)
            List<RecordInfo> toDeleteList = new List<RecordInfo>();

            //Go through potential shows and add the ones we should record
            //Omit those which are already done, too far in the future, or too many concurrent.  (already queued is fine)
            foreach(RecordInfo recordInfo in SortBasedOnKeywordPos(recordDict.Values.ToList()))
            {
                bool showAlreadyDone=recordInfo.GetEndDT()<DateTime.Now;
                bool showTooFarAway=recordInfo.GetStartDT()>DateTime.Now.AddHours(Convert.ToInt32(configuration["hoursInFuture"]));

                if(showAlreadyDone)
                {
                    Console.WriteLine($"{DateTime.Now}: Show already finished: {recordInfo.description} at {recordInfo.GetStartDT()}");
                    toDeleteList.Add(recordInfo); //So we don't leak
                }
                if(showTooFarAway)
                    Console.WriteLine($"{DateTime.Now}: Show too far away: {recordInfo.description} at {recordInfo.GetStartDT()}");

                if(recordInfo.processSpawnedFlag)
                    Console.WriteLine($"{DateTime.Now}: Show already queued: {recordInfo.description} at {recordInfo.GetStartDT()}");                    

                //Let's queue this since it looks good so far
                if(!showAlreadyDone && !showTooFarAway)
                {
                    AddToQueued(recordInfo);
                }
            }

            //Remove too many concurrent shows
            RemoveConcurrent();

            //Let's clean up master dictionary now of old shows
            foreach (RecordInfo recordInfo in toDeleteList)
                DeleteRecordInfo(recordInfo);                

            //build email and print schedule
            Console.WriteLine($"{DateTime.Now}: Current Schedule ==================");
            foreach(RecordInfo recordInfo in queuedRecordings)
            {
                Console.WriteLine($"{DateTime.Now}: {recordInfo.description} at {recordInfo.GetStartDT()} - {recordInfo.GetEndDT()}");
                currentScheduleText=mailer.AddCurrentScheduleToString(currentScheduleText,recordInfo);  
            }
            Console.WriteLine($"{DateTime.Now}: ===================================");

            //Send mail if we have something
            string mailText="";
            if(!string.IsNullOrEmpty(currentScheduleText))
                mailText=mailText+currentScheduleText;   
            if(!string.IsNullOrEmpty(concurrentShowText))
                mailText=mailText+concurrentShowText;                 
            if(!string.IsNullOrEmpty(mailText))
                mailer.SendNewShowMail(configuration,mailText);                   

            //Ok, we can now return the list
            return queuedRecordings;
        }

        private void AddToQueued(RecordInfo recordInfoToAdd)
        {
            //Make a sorted list based on start time
            // We'll make sure concurrent shows are respected
            RecordInfo[] recordInfoArray = queuedRecordings.ToArray();
            for(int idx=0;idx<recordInfoArray.Length;idx++)
            {
                if(recordInfoToAdd.GetStartDT()<recordInfoArray[idx].GetStartDT())
                {
                    queuedRecordings.Insert(idx,recordInfoToAdd);
                    return;
                }

            }

            //If we've made it this far, then add to the end
            queuedRecordings.Add(recordInfoToAdd);
        }

        private void RemoveConcurrent()
        {
            Console.WriteLine($"{DateTime.Now}: Concurrency Removals ==================");

            //stack to keep track of end dates
            List<DateTime> endTimeStack = new List<DateTime>();

            int maxConcurrent=Convert.ToInt16(configuration["concurrentCaptures"]);
            int concurrent=0;

            RecordInfo[] recordInfoArray = queuedRecordings.ToArray();
            for(int idx=0;idx<recordInfoArray.Length;idx++)
            {
                concurrent++;  //increment because it's a new record              

                //Check if we can decrement
                DateTime[] endTimeArray = endTimeStack.ToArray();
                for(int i=0;i<endTimeArray.Length;i++)
                {
                    if(recordInfoArray[idx].GetStartDT()>=endTimeArray[i])
                    {
                        concurrent--;
                        endTimeStack.Remove(endTimeArray[i]);
                    }
                }
                endTimeStack.Add(recordInfoArray[idx].GetEndDT());

                //Let's make sure we're not over max
                if(concurrent>maxConcurrent)
                {
                    queuedRecordings.Remove(recordInfoArray[idx]);
                    Console.WriteLine($"{DateTime.Now}: Too many for this time slot: {recordInfoArray[idx].description} at {recordInfoArray[idx].GetStartDT()} - {recordInfoArray[idx].GetEndDT()}"); 
                }
            }           
        }

        private List<RecordInfo> SortBasedOnKeywordPos(List<RecordInfo> listToBeSorted)
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