using System;
using System.Threading.Tasks;

namespace Reown.AppKit.Unity
{
    public class ConnectionProposal : IDisposable
    {
        public bool IsConnected { get; protected set; }

        public event Action<ConnectionProposal> ConnectionUpdated
        {
            add => connectionUpdated += value;
            remove => connectionUpdated -= value;
        }

        public event Action<ConnectionProposal> Connected
        {
            add => connected += value;
            remove => connected -= value;
        }

        public event Action<SignatureRequest> SignatureRequested
        {
            add => signatureRequested += value;
            remove => signatureRequested -= value;
        }

        public readonly Connector connector;

        protected Action<ConnectionProposal> connectionUpdated;
        protected Action<ConnectionProposal> connected;
        protected Action<SignatureRequest> signatureRequested;

        private bool _disposed;

        public ConnectionProposal(Connector connector)
        {
            this.connector = connector;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                connectionUpdated = null;
                connected = null;
            }

            _disposed = true;
        }

        public class SignatureRequest
        {
            public Func<Task> ApproveAsync { get; set; }
            public Func<Task> RejectAsync { get; set; }
        }
    }
}