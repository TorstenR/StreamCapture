using System;
using Microsoft.Extensions.Configuration;
using MailKit.Net.Smtp;
using MailKit;
using MimeKit;

namespace StreamCapture
{
    public class Mailer
    {
        public string AddNewShowToString(string newShowText,RecordInfo recordInfo)
        {
            if(string.IsNullOrEmpty(newShowText))
                newShowText=@"<h3>New Shows Scheduled:</h3>";

            return newShowText+@"<br>"+BuildNewShowText(recordInfo);
        }

        public string AddConcurrentShowToString(string concurentShowText,RecordInfo recordInfo)
        {
            if(string.IsNullOrEmpty(concurentShowText))
                concurentShowText=@"<p><p><h3>Shows NOT scheduled due to too many concurrent:</h3>";

            return concurentShowText+@"<br>"+BuildConcurrentShowText(recordInfo);
        }        

        public void SendNewShowMail(IConfiguration configuration,string mailText)
        {
            SendMail(configuration,@"New Shows Scheduled:",mailText);
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

        public void SendErrorMail(IConfiguration configuration,string subject,string body)
        {
            SendMail(configuration, subject, body);
        }

        public void SendMail(IConfiguration configuration,string subjectTest,string bodyText)
        {
            if(string.IsNullOrEmpty(configuration["smtpUser"]) || string.IsNullOrEmpty(configuration["mailAddress"]))
                return;

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("StreamCapture", ""));
                message.To.Add(new MailboxAddress("", configuration["mailAddress"]));
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

        private string BuildNewShowText(RecordInfo recordInfo)
        {
            return String.Format($"Scheduled: {recordInfo.description} starting at {recordInfo.GetStartDT()} on channel/s {recordInfo.GetChannelString()}");
        }

        private string BuildConcurrentShowText(RecordInfo recordInfo)
        {
            return String.Format($"Not Scheduled: {recordInfo.description} starting at {recordInfo.GetStartDT()} on channel/s {recordInfo.GetChannelString()}");
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