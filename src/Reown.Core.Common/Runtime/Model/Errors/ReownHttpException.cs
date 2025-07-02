using System;
using System.Runtime.Serialization;

namespace Reown.Core.Common.Model.Errors
{
    /// <summary>
    /// </summary>
    [Serializable]
    public class ReownHttpException : ReownException
    {
        /// <summary>
        /// </summary>
        public ReownHttpException()
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ReownHttpException(string message) : base(message)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReownHttpException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// </summary>
        protected ReownHttpException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
