using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A single-consumer asynchronous message pump. Items enqueued from any thread are processed one at a
    ///     time, in FIFO order, by whichever caller first finds the pump idle. Enqueue and the drain lifecycle
    ///     are synchronized so that concurrent producers can neither corrupt the queue, run the processor
    ///     concurrently, nor strand an item that arrives while the pump is winding down.
    /// </summary>
    /// <typeparam name="T">The type of item processed by the pump.</typeparam>
    internal sealed class SequentialMessagePump<T>
    {
        private readonly object _lock = new();
        private readonly Func<T, Task> _process;
        private readonly Queue<T> _queue = new();
        private bool _isProcessing;

        /// <summary>
        ///     Creates a pump that feeds each dequeued item to <paramref name="process" />.
        /// </summary>
        /// <param name="process">
        ///     The per-item processor. An exception it throws is logged and does not stop the pump or drop later items.
        /// </param>
        public SequentialMessagePump(Func<T, Task> process)
        {
            _process = process;
        }

        /// <summary>
        ///     Enqueues an item and ensures the pump is draining. The returned task completes when the calling
        ///     drainer stops; it completes synchronously when another caller is already draining.
        /// </summary>
        /// <param name="item">The item to process.</param>
        public Task Enqueue(T item)
        {
            lock (_lock)
            {
                _queue.Enqueue(item);
            }

            return Drain();
        }

        private async Task Drain()
        {
            lock (_lock)
            {
                if (_isProcessing)
                {
                    return;
                }

                _isProcessing = true;
            }

            try
            {
                while (true)
                {
                    T item;
                    lock (_lock)
                    {
                        if (_queue.Count == 0)
                        {
                            _isProcessing = false;
                            return;
                        }

                        item = _queue.Dequeue();
                    }

                    try
                    {
                        await _process(item);
                    }
                    catch (Exception e)
                    {
                        ReownLogger.LogError(e);
                    }
                }
            }
            catch (Exception e)
            {
                ReownLogger.LogError(e);
                lock (_lock)
                {
                    _isProcessing = false;
                }
            }
        }
    }
}
