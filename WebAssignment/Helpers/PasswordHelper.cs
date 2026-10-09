using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using WebAssignment.Models;
using System.Text;

namespace WebAssignment.Helpers
{
    public static class PasswordHelper
    {
        private static readonly PasswordHasher<User> Hasher = new();

        public static string HashPassword(string password)
        {
            return Hasher.HashPassword(new User(), password);
        }

        // 2. Check if the entered password matches the scrambled one
        public static bool VerifyPassword(string enteredPassword, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash)) return false;
            try
            {
                return Hasher.VerifyHashedPassword(new User(), storedHash, enteredPassword)
                    != PasswordVerificationResult.Failed;
            }
            catch
            {
                using var sha256 = SHA256.Create();
                var legacy = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(enteredPassword)));
                return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(legacy), Encoding.UTF8.GetBytes(storedHash));
            }
        }

        public static bool NeedsRehash(string storedHash) => !storedHash.StartsWith("AQAAAA", StringComparison.Ordinal);
    }
}
