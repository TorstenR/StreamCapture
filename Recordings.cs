using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace StreamCapture
{
    public class Recordings
    {
        public ManualResetEvent mre; //used to release the sleeping main thread if we want a reload from the web server

        private IConfiguration configuration;
        //Master dictionary of all shows we're interested in recording.  This is *not* the shows we necessarily will queue.
        //For example, this master list will have entires too far in the future, too many concurrent, etc.
        //The list is cleaned up when an entry is already completed.
        private Dictionary<string, RecordInfo> recordDict;
        //List of queued shows in datetime order
        //This list is derived from the recordDict master dictionary.  However, it's put in datetime order, and entries
        //too far in the future, or too many at once are omitted.  In other words, this is the list of shows
        //we'll actually queue to record/capture
        private List<RecordInfo> queuedRecordings;

        public Recordings(IConfiguration _configuration,ManualResetEvent _mre)
        {
            mre=_mre;
            recordDict = new Dictionary<string, RecordInfo>();
            configuration = _configuration;
        }
        
        //
        // This is a key member function.  This and 'CaptureStream' do the bulk of the work
        //
        // As such, I'll document this a bit more than usual so when I forget (tomorrow) I can read and remember some...
        //
        public List<RecordInfo> BuildRecordSchedule(List<ScheduleShow> scheduleShowList)
        {
            //Refresh keywords
            //
            //Reads keywords from the keywords.json file.  This is data used to determine which entries in the schedule
            //we are interested in recording/capturing
            Keywords keywords = new Keywords(configuration);

            //Go through the shows and load up recordings if there's a match
            //
            //This loop compares keywords and schedule entires, when there's a match, it 
            //adds creates a RecordInfo istance and adds it to a master Dictionary
            //of shows we thing we care about.  Later we'll determine which ones we'll actually capture. 
            foreach(ScheduleShow scheduleShow in scheduleShowList)
            {
                //Find any shows that match
                Tuple<KeywordInfo,int> tuple = keywords.FindMatch(scheduleShow);   
                if (tuple != null)
                {

                    //Build record info if already exists, otherwise, create new                 
                    RecordInfo recordInfo=GetRecordInfo(BuildRecordInfoKeyValue(scheduleShow));

                    //Load the recordInfo object w/ the specific schedule
                    recordInfo = BuildRecordInfoFromShedule (recordInfo,scheduleShow);

                     //Load the recordInfo object w/ the specifics from keywords.json file
                    recordInfo = BuildRecordInfoFromKeywords(recordInfo,tuple);              

                    //Update or add  (assuming the show has not already ended)
                    if(recordInfo.GetEndDT()>DateTime.Now)
                        AddUpdateRecordInfo(BuildRecordInfoKeyValue(recordInfo),recordInfo);
                }
            }

            //Return shows that should actually be queued (omitted those already done, too far in the future, etc...)
            //
            //This is an important call.  Please see remarks in this member function for more info.
            return GetShowsToQueue();
        }

        //Let's load the record info object from the show found in the schedule
        public RecordInfo BuildRecordInfoFromShedule(RecordInfo recordInfo,ScheduleShow scheduleShow)
        {
                //Fill out the recording info
                recordInfo.id = scheduleShow.id;
                recordInfo.description = scheduleShow.name;
                recordInfo.strStartDT = scheduleShow.time;
                //recordInfo.strStartDT = DateTime.Now.AddHours(4).ToString();
                recordInfo.strEndDT = scheduleShow.end_time;
                recordInfo.strDuration = scheduleShow.runtime;
                recordInfo.category = scheduleShow.category;
                //recordInfo.strDuration = "1";
                recordInfo.strDTOffset = configuration["schedTimeOffset"];

                //Clean up description, and then use as filename
                recordInfo.fileName = scheduleShow.name.Replace(' ','_');
                string myChars = @"|'/\ ,<>#@!+&^*()~`;";
                string invalidChars = myChars + new string(Path.GetInvalidFileNameChars());
                foreach (char c in invalidChars)
                {
                    recordInfo.fileName = recordInfo.fileName.Replace(c.ToString(), "");
                }

                //If starred, add designator to filename
                if(recordInfo.starredFlag)
                    recordInfo.fileName = "+_" + recordInfo.fileName;

                return recordInfo;
        }

        private RecordInfo BuildRecordInfoFromKeywords(RecordInfo recordInfo,Tuple<KeywordInfo,int> tuple)
        {
                recordInfo.keywordPos = tuple.Item2;  //used for sorting the most important shows 

                KeywordInfo keywordInfo = tuple.Item1;
                recordInfo.preMinutes = keywordInfo.preMinutes;
                recordInfo.postMinutes = keywordInfo.postMinutes;
                recordInfo.starredFlag = keywordInfo.starredFlag;
                recordInfo.emailFlag = keywordInfo.emailFlag;
                recordInfo.qualityPref = keywordInfo.qualityPref;
                recordInfo.langPref = keywordInfo.langPref;
                recordInfo.channelPref = keywordInfo.channelPref;

                return recordInfo;
        }        

        public List<RecordInfo> GetSortedMasterRecordList()
        {
            List<RecordInfo> sortedRecordInfoList = new List<RecordInfo>();
            foreach(RecordInfo recordInfo in recordDict.Values.ToList())
                sortedRecordInfoList = AddToSortedList(recordInfo,sortedRecordInfoList);

            return sortedRecordInfoList;
        }

        public List<RecordInfo> GetRecordInfoList()
        {
            return recordDict.Values.ToList();
        }

        public Dictionary<string, RecordInfo> GetRecordInfoDictionary()
        {
            return recordDict;
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
        public string BuildRecordInfoKeyValue(RecordInfo recordInfo)        
        {
            return recordInfo.strStartDT + recordInfo.description;
        }

        public string BuildRecordInfoKeyValue(ScheduleShow scheduleShow)        
        {
            return scheduleShow.time + scheduleShow.name;
        }

        public void AddUpdateRecordInfo(string recordInfoKey,RecordInfo recordInfo)
        {
            //set flag
            recordInfo.selectedFlag=true;  

            if(recordDict.ContainsKey(recordInfoKey))
                recordDict[recordInfoKey]=recordInfo;
            else
                recordDict.Add(recordInfoKey,recordInfo);
        }

        private void DeleteRecordInfo(RecordInfo recordInfoToDelete)
        {
            recordDict.Remove(BuildRecordInfoKeyValue(recordInfoToDelete));
        }

        //Figures out which schedule entries we actually intend to queue for capture
        //
        //It uses keyword order and maximum concurrent captures allowed to determine which
        //entires are queued and which are passed over.
        private List<RecordInfo> GetShowsToQueue()
        {
            //Build mail to send out
            Mailer mailer = new Mailer();
            string concurrentShowText = "";
            string currentScheduleText="";

            //check timeframe in the future to queue
            DateTime futureCutoff;
            if(configuration["hoursInFuture"]=="today")
                futureCutoff=new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,23,59,59,0,DateTime.Now.Kind);
            else
                futureCutoff=DateTime.Now.AddHours(Convert.ToInt32(configuration["hoursInFuture"]));

            //Starting new as this is always time dependent
            queuedRecordings=new List<RecordInfo>();

            Console.WriteLine($"{DateTime.Now}: Schedule Notes: ==================");

            //Go through potential shows and add the ones we should record
            //Omit those which are already done, too far in the future, or too many concurrent.  (already queued is fine obviously)
            //
            //recordingList has the shows in order of the keywords which they matched on, excluding dates beyond cutoff
            List<RecordInfo> recordingList = SortBasedOnKeywordPos(recordDict.Values.ToList(),futureCutoff);
            foreach(RecordInfo recordInfo in recordingList.ToArray())
            {
                bool showAlreadyDone=recordInfo.GetEndDT()<DateTime.Now;
                //bool showTooFarAway=recordInfo.GetStartDT()>futureCutoff;
                bool tooManyConcurrent=!IsConcurrencyOk(recordInfo,queuedRecordings);
                bool showCancelled=recordInfo.cancelledFlag;

                if (showAlreadyDone)
                {
                    //Console.WriteLine($"{DateTime.Now}: Show already finished: {recordInfo.description} at {recordInfo.GetStartDT()}");
                }
                else if(showCancelled)
                {
                    Console.WriteLine($"{DateTime.Now}: Show cancelled by user: {recordInfo.description} at {recordInfo.GetStartDT()}");
                }
                //else if(showTooFarAway)
                //{
                    //Console.WriteLine($"{DateTime.Now}: Show too far away: {recordInfo.description} at {recordInfo.GetStartDT()}");
                //}
                else if(tooManyConcurrent)
                {
                    Console.WriteLine($"{DateTime.Now}: Too many at once: {recordInfo.description} at {recordInfo.GetStartDT()} - {recordInfo.GetEndDT()}"); 

                    //Send mail is newly too many
                    if(recordInfo.tooManyFlag==false)
                        concurrentShowText=mailer.AddTableRow(concurrentShowText,recordInfo); //send email

                    //Set flag
                    recordInfo.tooManyFlag=true;

                    //Alert if show is starred
                    if(recordInfo.starredFlag)
                        mailer.SendShowAlertMail(configuration,recordInfo,"Starred show won't record - too many at once");
                }
                else //Let's queue this since it looks good
                {
                    recordInfo.tooManyFlag=false; //reset flag
                    
                    //Log if we've already queued
                    //if(recordInfo.queuedFlag)
                    //    Console.WriteLine($"{DateTime.Now}: Show already queued: {recordInfo.description} at {recordInfo.GetStartDT()}");

                    //If this is newly queued, then add to email
                    if (recordInfo.queuedFlag == false)
                    {
                        Console.WriteLine($"{DateTime.Now}: Show newly queued: {recordInfo.description} at {recordInfo.GetStartDT()}");
                        currentScheduleText = mailer.AddTableRow(currentScheduleText, recordInfo); ;//mail update
                    }

                    //see if the show is super long
                    if ((recordInfo.GetEndDT() - recordInfo.GetStartDT()).Hours > 4)
                    {
                        Console.WriteLine($"{DateTime.Now}: Show really long: {recordInfo.description} at {recordInfo.GetStartDT()} for {recordInfo.GetDuration()} minutes");
                        mailer.SendShowAlertMail(configuration, recordInfo, "WARNING - Show really long");
                    }

                    //Add this to the queue so it's up to date
                    recordInfo.queuedFlag=true;
                    queuedRecordings = AddToSortedList(recordInfo,queuedRecordings);
                }
            }
             
            //build email and print schedule
            Console.WriteLine($"{DateTime.Now}: Current Schedule ==================");
            foreach(RecordInfo recordInfo in queuedRecordings)
            {
                Console.WriteLine($"{DateTime.Now}: {recordInfo.description} at {recordInfo.GetStartDT()} - {recordInfo.GetEndDT()}");
            }
            Console.WriteLine($"{DateTime.Now}: ===================================");

            //Send mail if we have something AND a digest email has not already been sent
            string[] times = configuration["scheduleCheck"].Split(',');
            if (DateTime.Now.Hour != Convert.ToInt16(times[0]))
                mailer.SendUpdateEmail(configuration,currentScheduleText,concurrentShowText);                

            //Ok, we can now return the list
            return queuedRecordings;
        }

        public void CleanupOldShows()
        {
            foreach(RecordInfo recordInfo in recordDict.Values.ToList())
            {
                if(recordInfo.GetEndDT()<DateTime.Now)
                    DeleteRecordInfo(recordInfo);
            }
        }

        private List<RecordInfo> AddToSortedList(RecordInfo recordInfoToAdd,List<RecordInfo> list)
        {
            //Add to a sorted list based on start time
            RecordInfo[] recordInfoArray = list.ToArray();
            for(int idx=0;idx<recordInfoArray.Length;idx++)
            {
                if(recordInfoToAdd.GetStartDT()<recordInfoArray[idx].GetStartDT())
                {
                    list.Insert(idx,recordInfoToAdd);
                    return list;
                }
            }

            //If we've made it this far, then add to the end
            list.Add(recordInfoToAdd);
            return list;
        }

        //Checks to make sure we're not recording too many shows at once
        //
        //The approach taken is to try and add shows to this queued list one at a time *in keyword order*.
        //This way, shows matched on higher keywords, get higher priority.
        private bool IsConcurrencyOk(RecordInfo recordingToAdd,List<RecordInfo> recordingList)
        {
            //If a manual entry (by user from web interface), we're good
            if(recordingToAdd.manualFlag)
                return true;

            //Temp list to test with
            List<RecordInfo> tempList = new List<RecordInfo>();
            bool okToAddFlag=true;

            //Only add shows which are before show to add
            foreach(RecordInfo show in recordingList)
            {
                if(show.GetStartDT() < recordingToAdd.GetEndDT())
                    tempList.Add(show);
            }

            //Add to this temp list and then we'll check for concurrency
            tempList=AddToSortedList(recordingToAdd,tempList);

            //stack to keep track of end dates
            List<DateTime> endTimeStack = new List<DateTime>();

            int concurrentBase=Convert.ToInt16(configuration["concurrentCaptures"]);
            int addtlConcurrent = Convert.ToInt16(configuration["additionalStarredCaptures"]);
            int concurrent=0;

            RecordInfo[] recordInfoArray = tempList.ToArray();
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
                int maxConcurrent = concurrentBase;
                if (recordingToAdd.starredFlag)
                    maxConcurrent = concurrentBase + addtlConcurrent;
                if (concurrent > maxConcurrent)
                    okToAddFlag = false;
                //else
                //    okToAddFlag = true;
            } 

            return okToAddFlag;         
        }

        //Shorts the items based on where they were found in the keyword list
        //This enables us to try and add shows in keyword priority (assuming some won't get a slot)
        private List<RecordInfo> SortBasedOnKeywordPos(List<RecordInfo> listToBeSorted,DateTime futureCutoff)
        {
            List<RecordInfo> sortedList=new List<RecordInfo>();

            foreach(RecordInfo recordInfo in listToBeSorted)
            {
                if (recordInfo.GetStartDT() < futureCutoff)  //make sure we're only talking about the current timeframe
                {
                    bool insertedFlag = false;
                    RecordInfo[] sortedArray = sortedList.ToArray();
                    for (int idx = 0; idx < sortedArray.Length; idx++)
                    {
                        if (recordInfo.keywordPos <= sortedArray[idx].keywordPos)
                        {
                            sortedList.Insert(idx, recordInfo);
                            insertedFlag = true;
                            break;
                        }
                    }

                    //Not found, so add to the end
                    if (!insertedFlag)
                    {
                        sortedList.Add(recordInfo);
                    }
                }
            }

            return sortedList;
        }
    }
}