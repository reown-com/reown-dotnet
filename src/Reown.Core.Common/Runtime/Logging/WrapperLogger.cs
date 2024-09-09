using System;

namespace Reown.Core.Common.Logging
{
    public class WrapperLogger : ILogger
    {
        private readonly ILogger _logger;
        private readonly string _prefix;

        public WrapperLogger(ILogger logger, string prefix)
        {
            _logger = logger;
            _prefix = prefix;
        }

        public void Log(string message)
        {
            _logger?.Log($"[{_prefix}] {message}");
        }

        public void LogError(string message)
        {
            _logger?.LogError($"[{_prefix}] {message}");
        }

        public void LogError(Exception e)
        {
            _logger?.LogError(e);
        }
    }
}