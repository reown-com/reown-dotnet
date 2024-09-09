using System;

namespace Reown.Core.Models
{
    public class DisposeHandlerToken : IDisposable
    {
        private readonly Action _onDispose;

        protected bool Disposed;

        public DisposeHandlerToken(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentException("onDispose must be non-null");
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
                _onDispose();
            }

            Disposed = true;
        }
    }
}