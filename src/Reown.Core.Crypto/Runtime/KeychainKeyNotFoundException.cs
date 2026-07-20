using System;

namespace Reown.Core.Crypto
{
    /// <summary>
    ///     The exception that is thrown when the <see cref="KeyChain" /> does not contain a key
    ///     for the requested tag.
    /// </summary>
    /// <remarks>
    ///     Inherits from <see cref="InvalidOperationException" /> to preserve backward compatibility
    ///     with callers that catch <see cref="InvalidOperationException" />.
    /// </remarks>
    public class KeychainKeyNotFoundException : InvalidOperationException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="KeychainKeyNotFoundException" /> class for the given tag.
        /// </summary>
        /// <param name="tag">The tag that was not found in the keychain.</param>
        public KeychainKeyNotFoundException(string tag)
            : base($"Keychain does not contain key with tag: {tag}.")
        {
            Tag = tag;
        }

        /// <summary>
        ///     The tag that was not found in the keychain.
        /// </summary>
        public string Tag { get; }
    }
}
