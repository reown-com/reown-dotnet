using System;
using System.Threading;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;
using Reown.Core.Interfaces;

namespace Reown.Core
{
    /// <summary>
    ///     The HeartBeat module emits a pulse event at a specific interval simulating
    ///     a heartbeat. It can be used as an setInterval replacement
    /// </summary>
    public class HeartBeat : IHeartBeat
    {
        private readonly object _lifecycleLock = new();
        private Task _pulseTask = Task.CompletedTask;
        private bool _initialized;

        /// <summary>
        ///     The context UUID that this heartbeat module uses
        /// </summary>
        public readonly Guid ContextGuid = Guid.NewGuid();

        protected bool Disposed;

        /// <summary>
        ///     Create a new Heartbeat module, optionally specifying options
        /// </summary>
        /// <param name="interval">The interval to emit the <see cref="IHeartBeat.OnPulse" /> event at</param>
        public HeartBeat(int interval = 5000)
        {
            Interval = interval;
        }

        /// <summary>
        ///     The CancellationTokenSource that can be used to stop the Heartbeat module
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; private set; } = new();

        /// <summary>
        ///     The name of this Heartbeat module
        /// </summary>
        public string Name
        {
            get => $"heartbeat-{ContextGuid}";
        }

        /// <summary>
        ///     The context string of this Heartbeat module
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     The interval (in milliseconds) the Pulse event gets emitted/triggered
        /// </summary>
        public int Interval { get; }

        public event EventHandler OnPulse;

        /// <summary>
        ///     Starts the pulse loop if it has not already been started. The loop stops when either the supplied
        ///     cancellation token or the <see cref="CancellationTokenSource" /> is cancelled.
        /// </summary>
        /// <param name="cancellationToken">A token that can stop the pulse loop.</param>
        /// <returns>A task that completes after the pulse loop has been started.</returns>
        public Task InitAsync(CancellationToken cancellationToken = default)
        {
            lock (_lifecycleLock)
            {
                if (Disposed || _initialized)
                {
                    return Task.CompletedTask;
                }

                if (cancellationToken.CanBeCanceled)
                {
                    var previousCancellationTokenSource = CancellationTokenSource;
                    CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                        previousCancellationTokenSource.Token,
                        cancellationToken);
                    previousCancellationTokenSource.Dispose();
                }

                _initialized = true;
                var token = CancellationTokenSource.Token;
                _pulseTask = Task.Run(() => PulseLoopAsync(token));
                ObservePulseTask(_pulseTask);
            }

            return Task.CompletedTask;
        }

        internal Task PulseTask
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _pulseTask;
                }
            }
        }

        private async Task PulseLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Pulse();
                    await Task.Delay(Interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogError(ex);
                }
            }
        }

        private static void ObservePulseTask(Task pulseTask)
        {
            pulseTask.ContinueWith(
                task => LogError(task.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void Pulse()
        {
            var handlers = OnPulse;
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    LogError(ex);
                }
            }
        }

        private static void LogError(Exception exception)
        {
            try
            {
                ReownLogger.LogError(exception);
            }
            catch
            {
            }
        }

        /// <summary>
        ///     Cancels the pulse loop and releases the resources used by this heartbeat module.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            CancellationTokenSource cancellationTokenSource = null;

            lock (_lifecycleLock)
            {
                if (Disposed) return;

                Disposed = true;
                if (disposing)
                {
                    cancellationTokenSource = CancellationTokenSource;
                }
            }

            if (cancellationTokenSource == null)
            {
                return;
            }

            try
            {
                cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }
        }
    }
}
