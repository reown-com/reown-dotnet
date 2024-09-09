using System.Security.Cryptography;
using System.Text;

namespace Reown.Core.Common.Utils
{
    /// <summary>
    ///     Helper class for generating hashes
    /// </summary>
    public static class HashUtils
    {
        /// <summary>
        ///     Generate a SHA256 hash of the given message
        /// </summary>
        /// <param name="message">The message to hash</param>
        /// <returns>A SHA256 hash of the given message</returns>
        public static string HashMessage(string message)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(message));

                return bytes.ToHex();
            }
        }
    }
}