using System;
using Reown.Core.Common.Model.Errors;

namespace Reown.AppKit.Unity.WebGl
{
    internal class InteropException : ReownException
    {
        public InteropException(string message) : base(message)
        {
        }

        public InteropException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}