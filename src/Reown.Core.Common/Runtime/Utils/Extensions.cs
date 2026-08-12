using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace Reown.Core.Common.Utils
{
    /// <summary>
    ///     General purpose extension methods
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        ///     Returns true if the given object is a numeric type
        /// </summary>
        /// <param name="o">The object to check</param>
        /// <returns>Returns true if the object has a numeric type</returns>
        public static bool IsNumericType(this object o)
        {
            switch (Type.GetTypeCode(o.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Add a query parameter to the given source string
        /// </summary>
        /// <param name="source">The source string to add the generated query parameter to</param>
        /// <param name="key">The key of the query parameter</param>
        /// <param name="value">The value of the query parameter</param>
        /// <returns>The original source string with the generated query parameter appended</returns>
        public static string AddQueryParam(
            this string source, string key, string value)
        {
            string delim;
            if (source == null || !source.Contains("?"))
            {
                delim = "?";
            }
            else if (source.EndsWith("?") || source.EndsWith("&"))
            {
                delim = string.Empty;
            }
            else
            {
                delim = "&";
            }

            return source + delim + WebUtility.UrlEncode(key)
                   + "=" + WebUtility.UrlEncode(value);
        }

        /// <remarks>
        ///     The task is awaited once <see cref="Task.WhenAny(Task[])" /> picks it, so a fault
        ///     surfaces as the original exception. Returning without awaiting dropped it silently on
        ///     the non-generic overloads, and reading <c>.Result</c> on the generic ones wrapped it in
        ///     an <see cref="AggregateException" /> — either way callers could neither see the failure
        ///     nor match on its message, and a task that failed instantly looked like success.
        /// </remarks>
        /// <summary>
        ///     Takes the exception off a task nobody is waiting for any more.
        /// </summary>
        /// <remarks>
        ///     A timed-out task keeps running and may still fail. Nothing observes it by then, so its
        ///     exception resurfaces on the finalizer thread as an UnobservedTaskException — noise at
        ///     best, and a process kill wherever that event is left unhandled.
        /// </remarks>
        /// <summary>
        ///     Retrieves a task's exception so a failure nobody awaits cannot resurface later.
        /// </summary>
        /// <remarks>
        ///     For the fan-out case: when a failure is both stored in a <see cref="TaskCompletionSource{T}" />
        ///     for concurrent callers and thrown to the one that started the work, the stored copy is
        ///     left unobserved whenever there are no concurrent callers. Observing does not swallow it —
        ///     awaiting the task afterwards still throws.
        /// </remarks>
        public static void ObserveFault(this Task task)
        {
            ObserveAbandoned(task);
        }

        private static void ObserveAbandoned(Task task)
        {
            _ = task.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public static async Task<T> WithTimeout<T>(this Task<T> task, int timeout = 1000,
            string message = "Timeout of %t exceeded")
        {
            var resultT = await Task.WhenAny(task, Task.Delay(timeout));
            if (resultT != task)
            {
                ObserveAbandoned(task);
                throw new TimeoutException(message.Replace("%t", timeout.ToString()));
            }

            return await task;
        }

        /// <remarks>
        ///     The task is awaited once <see cref="Task.WhenAny(Task[])" /> picks it, so a fault
        ///     surfaces as the original exception. Returning without awaiting dropped it silently on
        ///     the non-generic overloads, and reading <c>.Result</c> on the generic ones wrapped it in
        ///     an <see cref="AggregateException" /> — either way callers could neither see the failure
        ///     nor match on its message, and a task that failed instantly looked like success.
        /// </remarks>
        public static async Task WithTimeout(this Task task, int timeout = 1000,
            string message = "Timeout of %t exceeded")
        {
            var resultT = await Task.WhenAny(task, Task.Delay(timeout));
            if (resultT != task)
            {
                ObserveAbandoned(task);
                throw new TimeoutException(message.Replace("%t", timeout.ToString()));
            }

            await task;
        }

        /// <remarks>
        ///     The task is awaited once <see cref="Task.WhenAny(Task[])" /> picks it, so a fault
        ///     surfaces as the original exception. Returning without awaiting dropped it silently on
        ///     the non-generic overloads, and reading <c>.Result</c> on the generic ones wrapped it in
        ///     an <see cref="AggregateException" /> — either way callers could neither see the failure
        ///     nor match on its message, and a task that failed instantly looked like success.
        /// </remarks>
        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout,
            string message = "Timeout of %t exceeded")
        {
            var resultT = await Task.WhenAny(task, Task.Delay(timeout));
            if (resultT != task)
            {
                ObserveAbandoned(task);
                throw new TimeoutException(message.Replace("%t", timeout.ToString()));
            }

            return await task;
        }

        /// <remarks>
        ///     The task is awaited once <see cref="Task.WhenAny(Task[])" /> picks it, so a fault
        ///     surfaces as the original exception. Returning without awaiting dropped it silently on
        ///     the non-generic overloads, and reading <c>.Result</c> on the generic ones wrapped it in
        ///     an <see cref="AggregateException" /> — either way callers could neither see the failure
        ///     nor match on its message, and a task that failed instantly looked like success.
        /// </remarks>
        public static async Task WithTimeout(this Task task, TimeSpan timeout,
            string message = "Timeout of %t exceeded")
        {
            var resultT = await Task.WhenAny(task, Task.Delay(timeout));
            if (resultT != task)
            {
                ObserveAbandoned(task);
                throw new TimeoutException(message.Replace("%t", timeout.ToString()));
            }

            await task;
        }

        public static bool SetEquals<T>(this IEnumerable<T> first, IEnumerable<T> second,
            IEqualityComparer<T> comparer)
        {
            return new HashSet<T>(second, comparer ?? EqualityComparer<T>.Default)
                .SetEquals(first);
        }

        public static Action ListenOnce(this EventHandler eventHandler, EventHandler handler)
        {
            EventHandler internalHandler = null;
            internalHandler = (sender, args) =>
            {
                eventHandler -= internalHandler;
                handler(sender, args);
            };

            eventHandler += internalHandler;

            return () =>
            {
                try
                {
                    eventHandler -= internalHandler;
                }
                catch (Exception)
                {
                    // ignored
                }
            };
        }

        public static Action ListenOnce<TEventArgs>(
            this EventHandler<TEventArgs> eventHandler,
            EventHandler<TEventArgs> handler)
        {
            EventHandler<TEventArgs> internalHandler = null;
            internalHandler = (sender, args) =>
            {
                eventHandler -= internalHandler;
                handler(sender, args);
            };

            eventHandler += internalHandler;

            return () =>
            {
                try
                {
                    eventHandler -= internalHandler;
                }
                catch (Exception)
                {
                    // ignored
                }
            };
        }
    }
}