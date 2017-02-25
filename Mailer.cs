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
                string showText=BuildShowText(recordInfo)+"<br>";

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

            string emailText=@"<p><p><h3>Scheduled Shows:</h3><br>"+scheduledShows;
            emailText=emailText+@"<p><p><h3>Shows NOT Scheduled: (too many at once) </h3><br>"+tooManyShows;
            emailText=emailText+@"<p><p><h3>Shows not queued yet: (</h3><br>"+notScheduleShows;
            emailText=emailText+@"<p><p><h3>Shows Recorded:</h3><br>"+recordedShows;
            emailText=emailText+@"<p><p><h3>Shows PARTIALLY Recorded:</h3><br>"+partialShows;
            emailText=emailText+@"<p><p><h3>Shows NOT Recorded: (left overs) </h3><br>"+notRecordedShows;

            //Send mail
            SendMail(configuration,"Daily Digest",emailText);
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