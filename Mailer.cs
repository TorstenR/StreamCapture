using System;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using MailKit.Net.Smtp;
using MailKit;
using MimeKit;

namespace StreamCapture
{
    public class Mailer
    {
        public void SendDailyDigest(IConfiguration configuration,Recordings recordings)
        {
            string scheduledShows="";
            string notScheduleShows="";
            string recordedShows="";
            string partialShows="";
            string notRecordedShows="";
            string tooManyShows="";

            //Build each portion of the mail
            List<RecordInfo> sortedRecordInfoList = recordings.GetSortedMasterRecordList();
            foreach(RecordInfo recordInfo in sortedRecordInfoList)
            {
                string showText=BuildTableRow(recordInfo);

                if(recordInfo.queuedFlag && !recordInfo.completedFlag)
                    scheduledShows=scheduledShows+showText;
                else if(recordInfo.tooManyFlag)
                    tooManyShows=tooManyShows+showText;
                else if(!recordInfo.processSpawnedFlag && recordInfo.GetStartDT()>DateTime.Now)
                    notScheduleShows=notScheduleShows+showText;
                else if(recordInfo.completedFlag && !recordInfo.partialFlag)
                    recordedShows=recordedShows+showText;
                else if(recordInfo.partialFlag)
                    partialShows=partialShows+showText;
                else
                    notRecordedShows=notRecordedShows+showText;
            }
            string emailText="";
            if(!string.IsNullOrEmpty(scheduledShows))
                emailText=emailText+StartTable("Scheduled Shows")+scheduledShows+EndTable();
            if(!string.IsNullOrEmpty(tooManyShows))
                emailText=emailText+StartTable("Shows NOT Scheduled (too many at once)")+tooManyShows+EndTable();
            if(!string.IsNullOrEmpty(notScheduleShows))
                emailText=emailText+StartTable("Shows not scheduled yet")+notScheduleShows+EndTable();
            if(!string.IsNullOrEmpty(recordedShows))
                emailText=emailText+StartTable("Shows Recorded")+recordedShows+EndTable();
            if(!string.IsNullOrEmpty(partialShows))
                emailText=emailText+StartTable("Shows PARTIALLY Recorded")+partialShows+EndTable();
            if(!string.IsNullOrEmpty(notRecordedShows))
                emailText=emailText+StartTable("Shows NOT Recorded")+notRecordedShows+EndTable();

            //Send mail
            if(!string.IsNullOrEmpty(emailText))
                SendMail(configuration,"Daily Digest",emailText);
        }

        private string StartTable(string caption)
        {
            string tableStr=@"<p><p><TABLE border='2' frame='hsides' rules='groups'><CAPTION>" + caption + @"</CAPTION>";
            tableStr=tableStr+@"<TR><TH><TH>Day<TH>Start<TH>Duration<TH>Category<TH>Description<TBODY>";
            return tableStr;
        }

        private string BuildTableRow(RecordInfo recordInfo)
        {
            string day = recordInfo.GetStartDT().ToString("ddd");
            if(recordInfo.GetStartDT().Day==DateTime.Now.AddDays(-1).Day)
                day="Yesterday";           
            if(recordInfo.GetStartDT().Day==DateTime.Now.Day)
                day="Today";
            if(recordInfo.GetStartDT().Day==DateTime.Now.AddDays(1).Day)
                day="Tomorrow";            
            string startTime = recordInfo.GetStartDT().ToString("HH:mm");
            double duration = Math.Round((double)recordInfo.GetDuration()/60.0,1);
            string star="";
            if(recordInfo.starredFlag)
                star="*";

            return String.Format($"<TR><TD>{star}<TD>{day}<TD>{startTime}<TD align='center'>{duration}H<TD>{recordInfo.category}<TD>{recordInfo.description}");           
        }            

        private string EndTable()
        {
            return "</TABLE>";
        }

        public string AddTableRow(string currentlyScheduled,RecordInfo recordInfo)
        {
            return currentlyScheduled+BuildTableRow(recordInfo);
        }        

        public void SendUpdateEmail(IConfiguration configuration,string currentScheduleText,string concurrentShowText)
        {
            string emailText="";

            if(!string.IsNullOrEmpty(currentScheduleText))
                emailText=emailText+StartTable("Shows Newly Scheduled")+currentScheduleText+EndTable();
            if(!string.IsNullOrEmpty(concurrentShowText))
                emailText=emailText+StartTable("Shows NOT recording due to too many")+concurrentShowText+EndTable();

            //Send mail if there are updates
            if(!string.IsNullOrEmpty(emailText))
                SendMail(configuration,"Schedule Updates",emailText);
        }    

        public void SendShowReadyMail(IConfiguration configuration,RecordInfo recordInfo)
        {
            string text=BuildShowReadyText(recordInfo);
            SendMail(configuration,text,text);
        }

        public void SendShowStartedMail(IConfiguration configuration,RecordInfo recordInfo)
        {
            string text=BuildShowStartedText(recordInfo);
            SendMail(configuration,text,text);
        }

        public void SendShowAlertMail(IConfiguration configuration,RecordInfo recordInfo,string subject)
        {
            string text=BuildShowStartedText(recordInfo);
            SendMail(configuration,subject,text);
        }

        public void SendErrorMail(IConfiguration configuration,string subject,string body)
        {
            SendMail(configuration, subject, body);
        }

        public void SendMail(IConfiguration configuration,string subjectTest,string bodyText)
        {
            if(string.IsNullOrEmpty(configuration["smtpUser"]) || string.IsNullOrEmpty(configuration["mailAddress"]))
                return;

            Console.WriteLine($"{DateTime.Now}: Sending email...");

            try
            {
                string[] addresses = configuration["mailAddress"].Split(',');

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("StreamCapture", ""));
                foreach(string address in addresses)
                    message.To.Add(new MailboxAddress("", address));
                message.Subject = subjectTest;

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = bodyText;
                message.Body = bodyBuilder.ToMessageBody();                

                using (var client = new SmtpClient())
                {
                    client.Connect(configuration["smtpServer"], Convert.ToInt16(configuration["smtpPort"]), false);
                    client.AuthenticationMechanisms.Remove("XOAUTH2");  
                    client.Authenticate(configuration["smtpUser"], configuration["smtpPass"]);
                    client.Send(message);
                    client.Disconnect(true);
                }
            }
            catch(Exception e)
            {
                //swallow email exceptions
                Console.WriteLine($"{DateTime.Now}: ERROR: Problem sending mail.  Error: {e.Message}");
            }
        }

        private string BuildShowReadyText(RecordInfo recordInfo)
        {
            return String.Format($"Published: {recordInfo.description}");
        }

        private string BuildShowStartedText(RecordInfo recordInfo)
        {
            return String.Format($"Started: {recordInfo.description}.  Should be done by {recordInfo.GetEndDT()}");
        }        
    }
}