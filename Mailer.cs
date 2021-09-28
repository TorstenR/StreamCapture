using System;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using System.Collections.Generic;
using MailKit.Net.Smtp;
using MailKit.Security;
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
            string text=String.Format($"Show: {recordInfo.description}.  Start Time: {recordInfo.GetStartDT().ToString("HH:mm")}");
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

            bool modernAuth=true;
            if(string.IsNullOrEmpty(configuration["smtpClientID"]))
                modernAuth=false;

            Console.WriteLine($"{DateTime.Now}: Sending email...");

            try
            {
                string[] addresses = configuration["mailAddress"].Split(',');

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("StreamCapture", configuration["smtpUser"]));
                foreach(string address in addresses)
                    message.To.Add(new MailboxAddress(address, address));
                message.Subject = subjectTest+" (18.04)";

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = bodyText;
                message.Body = bodyBuilder.ToMessageBody();    

                SaslMechanismOAuth2 oauth2=null;
                if(modernAuth)
                {
                    //oauth2 for microsoft graph
                    //
                    // A few notes as this was tough....
                    // - Setup https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app
                    // - Be sure and add right permissions under API permission for the app  (in this case, it was smtp)
                    // - Permissions must be delgated because as of 9/30/21, you couldn't assign smtp to an app
                    // - You must have admin consent enabled (again part of the api permissions screen)
                    // - There's a non-zero chance that in the future, smtp will be deprecated entirely
                    //
                    // https://github.com/jstedfast/MailKit/blob/master/ExchangeOAuth2.md
                    // https://github.com/jstedfast/MailKit/issues/989
                    //
                    NetworkCredential networkCredential = new NetworkCredential(configuration["smtpUser"], configuration["smtpPass"]);
                    
                    var scopes = new string[] {
                        //"email",
                        //"offline_access",
                        //"https://outlook.office.com/IMAP.AccessAsUser.All", // Only needed for IMAP
                        //"https://outlook.office.com/POP.AccessAsUser.All",  // Only needed for POP
                        "https://outlook.office.com/SMTP.Send", // Only needed for SMTP
                    };

                    //Get oauth2 token for smtp client
                    var app = PublicClientApplicationBuilder.Create(configuration["smtpClientID"]).WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs).Build();
                    var Task = app.AcquireTokenByUsernamePassword(scopes,networkCredential.UserName, networkCredential.SecurePassword).ExecuteAsync();
                    Task.Wait();
                    var authToken = Task.Result;
                    oauth2 = new SaslMechanismOAuth2 (authToken.Account.Username, authToken.AccessToken);
                }

                //Actually send message now
                using (var client = new SmtpClient())
                {
                    client.Connect(configuration["smtpServer"], Convert.ToInt16(configuration["smtpPort"]),false); 

                    if(!modernAuth) //msft is about to turn this option off
                    {
                        client.ServerCertificateValidationCallback = (s, c, h, e) => true; 
                        client.AuthenticationMechanisms.Remove("XOAUTH2");  
                        client.Authenticate(configuration["smtpUser"], configuration["smtpPass"]);
                    }
                    else
                    {
                        client.Authenticate(oauth2);
                    }
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
            return String.Format($"Started: {recordInfo.description}.  Should be done by {recordInfo.GetEndDT().ToString("HH:mm")}");
        }        
    }
}