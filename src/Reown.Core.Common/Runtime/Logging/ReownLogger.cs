using System;

namespace Reown.Core.Common.Logging
{
    public class ReownLogger
    {
        public static ILogger Instance;

        public static ILogger WithContext(string context)
        {
            return new WrapperLogger(Instance, context);
        }

        public static void Log(string message)
        {
            if (Instance == null)
                return;

            Instance.Log(message);
        }

        public static void LogError(string message)
        {
            if (Instance == null)
                return;

            Instance.LogError(message);
        }

        public static void LogError(Exception e)
        {
            if (Instance == null)
                return;

            Instance.LogError(e);
        }
    }
}