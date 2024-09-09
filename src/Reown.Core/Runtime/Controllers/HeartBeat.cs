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
        ///     Initialize the heartbeat module. This will start the pulse event and
        ///     will continuously emit the pulse event at the configured interval. If the
        ///     HeartBeatCancellationToken is cancelled, then the interval will be halted.
        /// </summary>
        /// <returns></returns>
        public Task InitAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken != default)
            {
                CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            var token = CancellationTokenSource.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        Pulse();
                    }
                    catch (Exception ex)
                    {
                        ReownLogger.LogError(ex);
                    }

                    await Task.Delay(Interval, token);
                }
            }, token);

            return Task.CompletedTask;
        }

        private void Pulse()
        {
            OnPulse?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                CancellationTokenSource?.Dispose();
            }

            Disposed = true;
        }
    }
}