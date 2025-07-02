using System;
using System.Runtime.Serialization;

namespace Reown.Core.Common.Model.Errors
{
    /// <summary>
    /// </summary>
    [Serializable]
    public class ReownConnectorException : ReownException
    {
        /// <summary>
        /// </summary>
        public ReownConnectorException()
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ReownConnectorException(string message) : base(message)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownConnectorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// </summary>
        protected ReownConnectorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
