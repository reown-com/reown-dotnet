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
        /// <param name="message">The message that describes the connector error.</param>
        public ReownConnectorException(string message) : base(message)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="message">The error message that explains the reason for the connector failure.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownConnectorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ReownConnectorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
