using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Durak.Services
{
    public class EmailSender : IEmailSender
    {

        private readonly ILogger _logger;
        public AuthMessageSenderOptions Options { get;}

        public EmailSender(IOptions<AuthMessageSenderOptions> optionsAccessor,
                            ILogger<EmailSender> logger)
        {
            Options = optionsAccessor.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string message)
        {
            if (string.IsNullOrEmpty(Options.SendGridKey))
                throw new Exception("Null SendGridKey");

            await Execute(Options.SendGridKey, subject, message, toEmail);
        }

        public async Task Execute(string apiKey, string subject, string message, string toEmail)
        {
            var client = new SendGridClient(apiKey);
            var msg = new SendGridMessage()
            {
                Subject = subject,
                From = new EmailAddress("owen.lingza@outlook.com", "DurakSupport"),
                PlainTextContent = message,
                HtmlContent = message
            };

            msg.AddTo(new EmailAddress(toEmail));

            msg.SetClickTracking(false, false);
            var response = await client.SendEmailAsync(msg);
            _logger.LogInformation(response.IsSuccessStatusCode ? $"Email to {toEmail} queued successfully" : $"Email to {toEmail} failed");

        }
    }
}
