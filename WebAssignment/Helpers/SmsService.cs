using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace WebAssignment.Helpers
{
    public class SmsService
    {
        private readonly IConfiguration _configuration;

        public SmsService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendSmsAsync(string toPhoneNumber, string messageBody)
        {
            string? accountSid = _configuration["SmsSettings:AccountSid"];
            string? authToken = _configuration["SmsSettings:AuthToken"];
            string? fromNumber = _configuration["SmsSettings:FromNumber"];

            if (string.IsNullOrWhiteSpace(accountSid) ||
                string.IsNullOrWhiteSpace(authToken) ||
                string.IsNullOrWhiteSpace(fromNumber))
            {
                throw new InvalidOperationException("Twilio SMS configuration is missing.");
            }

            TwilioClient.Init(accountSid, authToken);
            await MessageResource.CreateAsync(
                body: messageBody,
                from: new PhoneNumber(fromNumber),
                to: new PhoneNumber(toPhoneNumber));
        }
    }
}
