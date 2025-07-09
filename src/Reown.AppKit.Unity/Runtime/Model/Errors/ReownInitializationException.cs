using System;
using System.Runtime.Serialization;
using Reown.Core.Common.Model.Errors;

namespace Reown.AppKit.Unity.Model.Errors
{
    /// <summary>
    ///     Represents errors that occur during initialization of Reown AppKit components.
    /// </summary>
    [Serializable]
    public class ReownInitializationException : ReownException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownInitializationException"/> class.
        /// </summary>
        public ReownInitializationException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownInitializationException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the initialization error.</param>
        public ReownInitializationException(string message) : base(message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownInitializationException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the initialization failure.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownInitializationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReownInitializationException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ReownInitializationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
