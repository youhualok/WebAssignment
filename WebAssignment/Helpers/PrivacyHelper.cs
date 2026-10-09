using System;
using System.Text;

namespace WebAssignment.Helpers
{
    public static class PrivacyHelper
    {
        // Masks name: "John Doe" -> "J*** D**"
        public static string MaskName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "Anonymous";

            var parts = fullName.Split(' ');
            var maskedName = new StringBuilder();

            foreach (var part in parts)
            {
                if (part.Length > 1)
                {
                    maskedName.Append(part[0] + new string('*', part.Length - 1) + " ");
                }
                else
                {
                    maskedName.Append(part + " ");
                }
            }

            return maskedName.ToString().Trim();
        }

        // Masks email: "johndoe@gmail.com" -> "j******@gmail.com"
        public static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@")) return "******";

            var parts = email.Split('@');
            var localPart = parts[0];
            var domainPart = parts[1];

            if (localPart.Length <= 2)
            {
                return localPart[0] + "****@" + domainPart;
            }

            return localPart.Substring(0, 2) + new string('*', localPart.Length - 2) + "@" + domainPart;
        }

        // Masks phone: "0123456789" -> "012-****789"
        public static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
            return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 3);
        }
    }
}