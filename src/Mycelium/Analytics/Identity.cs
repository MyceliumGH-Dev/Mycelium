using System;
using System.Security.Cryptography;
using System.Text;

namespace Mycelium.Analytics
{
    /// <summary>
    /// Generates a stable, privacy-compliant unique identifier for analytics.
    /// The ID is based on machine and user details, but hashed with a salt
    /// so it cannot be reversed to identify the actual user.
    /// </summary>
    public static class Identity
    {
        // Salt for hashing - makes the hash unique to this project
        private const string Salt = "Mycelium_Analytics_2026!";

        /// <summary>
        /// Gets a stable unique identifier for the current user/machine combination.
        /// This ID persists across sessions but cannot be reversed to identify the user.
        /// </summary>
        /// <returns>A 12-character lowercase hex string, or "anonymous" on error.</returns>
        public static string GetUniqueId()
        {
            try
            {
                // Combine machine name and user name for a stable identifier
                string rawId = Environment.MachineName + Environment.UserName;

                // Hash with salt using SHA256
                using (var sha256 = SHA256.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(rawId + Salt);
                    byte[] hash = sha256.ComputeHash(bytes);

                    // Take first 12 characters for a short but unique ID
                    return BitConverter.ToString(hash)
                        .Replace("-", "")
                        .Substring(0, 12)
                        .ToLower();
                }
            }
            catch
            {
                return "anonymous";
            }
        }
    }
}
