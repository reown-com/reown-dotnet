using System;
using System.Runtime.Serialization;

namespace Reown.Core.Common.Model.Errors
{
    /// <summary>
    ///     The base exception class for all Reown exceptions.
    /// </summary>
    [Serializable]
    public class ReownException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownException"/> class.
        /// </summary>
        public ReownException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ReownException(string message) : base(message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ReownException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
