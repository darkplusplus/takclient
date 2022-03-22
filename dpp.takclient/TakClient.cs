using dpp.cot;
using NetCoreServer;
using System;
using System.Threading;

namespace dpp.takclient
{
    public class TakClient : TcpClient
    {
        private readonly int _backoffMin = 300;
        private readonly int _backoffMax = 300000;
        private int _backoff;
        private bool _stopping = false;
        public TakClient(string address, int port, int? backoffMax) : base(address, port)
        {
            _backoff = _backoffMin;
            if (backoffMax is not null)
                _backoffMax = (int)backoffMax;

        }

        protected override void OnConnected()
        {
            // reset backoff
            _backoff = _backoffMin;
        }

        public long Send(Message msg)
        {
            return Send(msg.ToXmlBytes());
        }

        public bool SendAsync(Message msg)
        {
            return SendAsync(msg.ToXmlBytes());
        }

        protected override void OnDisconnected()
        {
            Thread.Sleep(_backoff);
            _backoff = (int)Math.Round(Math.Clamp(_backoff * Math.E, _backoffMin, _backoffMax));

            if (_stopping == false)
            {
                ConnectAsync();
            }
        }

        protected override void Dispose(bool disposingManagedResources)
        {
            _stopping = true;
            DisconnectAsync();

            base.Dispose(disposingManagedResources);
        }
    }
}
