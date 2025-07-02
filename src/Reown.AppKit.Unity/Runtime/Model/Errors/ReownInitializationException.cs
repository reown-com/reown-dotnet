using System;
using System.Runtime.Serialization;
using Reown.Core.Common.Model.Errors;

namespace Reown.AppKit.Unity.Model.Errors
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
        /// <param name="message">The message that describes the initialization error.</param>
        public ReownInitializationException(string message) : base(message)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="message">The error message that explains the reason for the initialization failure.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownInitializationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ReownInitializationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
