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

                if(recordInfo.processSpawnedFlag && !recordInfo.completedFlag)
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
            string emailText=StartTable("Scheduled Shows")+scheduledShows+EndTable();
            emailText=emailText+StartTable("Shows NOT Scheduled (too many at once)")+tooManyShows+EndTable();
            emailText=emailText+StartTable("Shows not queued yet")+notScheduleShows+EndTable();
            emailText=emailText+StartTable("Shows Recorded")+recordedShows+EndTable();
            emailText=emailText+StartTable("Shows PARTIALLY Recorded")+partialShows+EndTable();
            emailText=emailText+StartTable("Shows NOT Recorded")+notRecordedShows+EndTable();

            //Send mail
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

        public string AddNewShowToString(string newShowText,RecordInfo recordInfo)
        {
            if(string.IsNullOrEmpty(newShowText))
                newShowText=@"<h3>New Shows Scheduled:</h3>";

            return newShowText+@"<br>"+BuildShowText(recordInfo);
        }

        public string AddCurrentScheduleToString(string currentlyScheduled,RecordInfo recordInfo)
        {
            if(string.IsNullOrEmpty(currentlyScheduled))
                currentlyScheduled=@"<p><p><h3>Current Schedule:</h3>";

            return currentlyScheduled+@"<br>"+BuildShowText(recordInfo);
        }        

        public string AddConcurrentShowToString(string concurentShowText,RecordInfo recordInfo)
        {
            if(string.IsNullOrEmpty(concurentShowText))
                concurentShowText=@"<p><p><h3>Shows NOT scheduled due to too many concurrent:</h3>";

            return concurentShowText+@"<br>"+BuildShowText(recordInfo);
        }        

        public void SendNewShowMail(IConfiguration configuration,string mailText)
        {
            SendMail(configuration,@"Current Shows Scheduled:",mailText);
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

        private string BuildShowText(RecordInfo recordInfo)
        {
            string day = recordInfo.GetStartDT().ToString("ddd");
            if(recordInfo.GetStartDT().Day==DateTime.Now.Day)
                day="Today";
            if(recordInfo.GetStartDT().Day==DateTime.Now.AddDays(1).Day)
                day="Tomorrow";            
            string startTime = recordInfo.GetStartDT().ToString("HH:mm");
            string endTime = recordInfo.GetEndDT().ToString("HH:mm");

            return String.Format($"{day} {startTime}-{endTime}   {recordInfo.description} on channel/s {recordInfo.GetChannelString()}");
        }  

        private string BuildShowReadyText(RecordInfo recordInfo)
        {
            return String.Format($"Published: {recordInfo.description}");
        }

        private string BuildShowStartedText(RecordInfo recordInfo)
        {
            return String.Format($"Started: {recordInfo.description}");
        }        
    }
}