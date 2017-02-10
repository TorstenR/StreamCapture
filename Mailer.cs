using System;
using Microsoft.Extensions.Configuration;
using MailKit.Net.Smtp;
using MailKit;
using MimeKit;

namespace StreamCapture
{
    public class Mailer
    {
        public string AddNewShowToString(string mailText,RecordInfo recordInfo)
        {
            if(string.IsNullOrEmpty(mailText))
                mailText="";

            return mailText+"\n"+BuildNewShowText(recordInfo);
        }

        public void SendNewShowMail(IConfiguration configuration,string mailText)
        {
            SendMail(configuration,"New Shows Scheduled",mailText);
        }

        public void SendNewShowMail(IConfiguration configuration,RecordInfo recordInfo)
        {
            SendMail(configuration,"Scheduled: "+recordInfo.description,BuildNewShowText(recordInfo));
        }

        public void SendShowReadyMail(IConfiguration configuration,RecordInfo recordInfo)
        {
            string text=BuildShowReadyText(recordInfo);
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
                message.Body = new TextPart("plain")
                {
                    Text = bodyText
                };

                using (var client = new SmtpClient())
                {
                    client.Connect(configuration["smtpServer"], Convert.ToInt16(configuration["smtpPort"]), false);
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
	                // Note: since we don't have an OAuth2 token, disable 	// the XOAUTH2 authentication mechanism.     
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

        private string BuildShowReadyText(RecordInfo recordInfo)
        {
            return String.Format($"Published: {recordInfo.description}");
        }
    }
}