using System;
using System.Runtime.Serialization;

namespace Reown.Core.Common.Model.Errors
{
    /// <summary>
    /// </summary>
    [Serializable]
    public class ReownInitializationException : ReownException
    {
        /// <summary>
        /// </summary>
        public ReownInitializationException()
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ReownInitializationException(string message) : base(message)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownInitializationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// </summary>
        protected ReownInitializationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
