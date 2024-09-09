using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Reown.Core.Common.Model.Errors;

namespace Reown.Core.Network.Models
{
    /// <summary>
    ///     Indicates an error
    /// </summary>
    public class Error
    {
        /// <summary>
        ///     The error code of this error
        /// </summary>
        [JsonProperty("code")]
        public long Code;

        /// <summary>
        ///     Any extra data for this error
        /// </summary>
        [JsonProperty("data")]
        public string Data;

        /// <summary>
        ///     The message for this error
        /// </summary>
        [JsonProperty("message")]
        public string Message;

        public static IEqualityComparer<Error> CodeMessageDataComparer { get; } = new CodeMessageDataEqualityComparer();

        /// <summary>
        ///     Create an ErrorResponse with a given ErrorType and (optional) parameters
        /// </summary>
        /// <param name="type">The error type of the ErrorResponse to create</param>
        /// <param name="context">Extra context</param>
        /// <param name="extraData">Extra data that is stored in the Data field of the newly created ErrorResponse</param>
        /// <returns>A new ErrorResponse</returns>
        public static Error FromErrorType(ErrorType type, string context = null, string extraData = null)
        {
            var message = SdkErrors.MessageFromType(type, context);

            return new Error
            {
                Code = (long)type,
                Message = message,
                Data = extraData
            };
        }

        /// <summary>
        ///     Create an ErrorResponse from a ReownNetworkException
        /// </summary>
        /// <param name="reownNetworkException">The exception to grab error values from</param>
        /// <returns>A new ErrorResponse object using values from the given exception</returns>
        public static Error FromException(ReownNetworkException reownNetworkException)
        {
            return new Error
            {
                Code = reownNetworkException.Code,
                Message = reownNetworkException.Message,
                Data = reownNetworkException.ToString()
            };
        }

        /// <summary>
        ///     Convert this ErrorResponse to a ReownNetworkException
        /// </summary>
        /// <returns>A new ReownNetworkException using values from this ErrorResponse</returns>
        public ReownNetworkException ToException()
        {
            return ReownNetworkException.FromType((ErrorType)Code, Message);
        }

        protected bool Equals(Error other)
        {
            return Code == other.Code && Message == other.Message && Data == other.Data;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((Error)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Code, Message, Data);
        }

        private sealed class CodeMessageDataEqualityComparer : IEqualityComparer<Error>
        {
            public bool Equals(Error x, Error y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (ReferenceEquals(x, null))
                {
                    return false;
                }

                if (ReferenceEquals(y, null))
                {
                    return false;
                }

                if (x.GetType() != y.GetType())
                {
                    return false;
                }

                return x.Code == y.Code && x.Message == y.Message && x.Data == y.Data;
            }

            public int GetHashCode(Error obj)
            {
                return HashCode.Combine(obj.Code, obj.Message, obj.Data);
            }
        }
    }
}