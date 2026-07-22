using System;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;
using Reown.Core.Controllers;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests the failure containment of <see cref="SequentialMessagePump{T}" />: a processor exception must not
    ///     stop the pump, and a logger that itself throws while reporting that exception must neither fault the
    ///     producer's <c>Enqueue</c> task nor leave the pump wedged with its processing flag stuck.
    /// </summary>
    public class SequentialMessagePumpTests
    {
        /// <summary>
        ///     Ensures that when the item processor throws and the logger throws while reporting it, the exception
        ///     does not escape to the producer and the pump keeps draining subsequent items.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ThrowingLogger_DoesNotFaultEnqueueOrWedgePump()
        {
            var originalLogger = ReownLogger.Instance;
            ReownLogger.Instance = new ThrowingLogger();

            try
            {
                var secondItemProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pump = new SequentialMessagePump<int>(item =>
                {
                    if (item == 1)
                    {
                        throw new InvalidOperationException("processor failure");
                    }

                    secondItemProcessed.TrySetResult(true);
                    return Task.CompletedTask;
                });

                await pump.Enqueue(1);
                await pump.Enqueue(2);

                var completed = await Task.WhenAny(secondItemProcessed.Task, Task.Delay(TimeSpan.FromSeconds(5)));

                Assert.Same(secondItemProcessed.Task, completed);
            }
            finally
            {
                ReownLogger.Instance = originalLogger;
            }
        }

        private sealed class ThrowingLogger : ILogger
        {
            public void Log(string message)
            {
                throw new InvalidOperationException("logger failure");
            }

            public void LogError(string message)
            {
                throw new InvalidOperationException("logger failure");
            }

            public void LogError(Exception e)
            {
                throw new InvalidOperationException("logger failure");
            }
        }
    }
}
