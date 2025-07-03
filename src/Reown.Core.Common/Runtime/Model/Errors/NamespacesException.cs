using System;
using System.Runtime.Serialization;

namespace Reown.Core.Common.Model.Errors
{
    /// <summary>
    ///     Represents errors that occur during WalletConnect namespace operations in the Reown .NET SDK.
    /// </summary>
    public class NamespacesException : ReownException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NamespacesException"/> class.
        /// </summary>
        public NamespacesException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="NamespacesException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the namespace error.</param>
        public NamespacesException(string message)
            : base(message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="NamespacesException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the namespace exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public NamespacesException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="NamespacesException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected NamespacesException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
