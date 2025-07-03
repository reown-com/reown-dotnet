using System;
using System.Runtime.Serialization;

namespace Reown.Core.Common.Model.Errors
{
    /// <summary>
    ///     Represents errors that occur when an operation or resource has expired in the Reown .NET SDK
    /// </summary>
    public class ExpiredException : ReownException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ExpiredException"/> class.
        /// </summary>
        public ExpiredException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ExpiredException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the expiration error.</param>
        public ExpiredException(string message)
            : base(message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ExpiredException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the expiration.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ExpiredException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ExpiredException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ExpiredException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}