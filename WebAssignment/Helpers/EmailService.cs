using System.Net;
using System.Net.Mail;

namespace WebAssignment.Helpers
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            await ExecuteSendEmailAsync(toEmail, subject, body, null, null);
        }

        // --- NEW: Method to support Attachments ---
        public async Task SendEmailWithAttachmentAsync(string toEmail, string subject, string body, byte[] attachmentData, string fileName)
        {
            await ExecuteSendEmailAsync(toEmail, subject, body, attachmentData, fileName);
        }

        private async Task ExecuteSendEmailAsync(string toEmail, string subject, string body, byte[]? attachmentData, string? fileName)
        {
            // Fetch settings from appsettings.json
            var emailSettings = _configuration.GetSection("EmailSettings");
            string senderEmail = emailSettings["SenderEmail"] ?? "admin@waliucinema.com";
            string senderPassword = emailSettings["SenderPassword"] ?? "yourpassword";
            string smtpServer = emailSettings["SmtpServer"] ?? "smtp.gmail.com";
            int smtpPort = int.Parse(emailSettings["SmtpPort"] ?? "587");

            using (var message = new MailMessage())
            {
                message.From = new MailAddress(senderEmail, "Waliu Cinema System");
                message.To.Add(new MailAddress(toEmail));
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = true;

                // ATTACHMENT LOGIC
                if (attachmentData != null && !string.IsNullOrEmpty(fileName))
                {
                    using (var ms = new MemoryStream(attachmentData))
                    {
                        var attachment = new Attachment(ms, fileName, "text/csv");
                        message.Attachments.Add(attachment);

                        // Send the email while the stream is open
                        using (var client = new SmtpClient(smtpServer, smtpPort))
                        {
                            client.EnableSsl = true;
                            client.Credentials = new NetworkCredential(senderEmail, senderPassword);
                            await client.SendMailAsync(message);
                        }
                    }
                }
                else
                {
                    // Standard Send without attachment
                    using (var client = new SmtpClient(smtpServer, smtpPort))
                    {
                        client.EnableSsl = true;
                        client.Credentials = new NetworkCredential(senderEmail, senderPassword);
                        await client.SendMailAsync(message);
                    }
                }
            }
        }
    }
}