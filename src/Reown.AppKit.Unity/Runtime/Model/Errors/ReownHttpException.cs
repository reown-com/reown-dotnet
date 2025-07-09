using System;
using System.Runtime.Serialization;
using Reown.Core.Common.Model.Errors;

namespace Reown.AppKit.Unity.Model.Errors
{
    /// <summary>
    ///     Represents errors that occur during HTTP operations in the Reown AppKit.
    /// </summary>
    [Serializable]
    public class ReownHttpException : ReownException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownHttpException"/> class.
        /// </summary>
        public ReownHttpException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownHttpException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the HTTP error.</param>
        public ReownHttpException(string message) : base(message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownHttpException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the HTTP failure.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownHttpException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownHttpException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ReownHttpException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
