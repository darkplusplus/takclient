using dpp.cot;
using NetCoreServer;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace dpp.takclient
{
    public class TakClient : TcpClient
    {
        private static readonly byte[] XmlDeclaration = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        private readonly int _backoffMin = 300;
        private readonly int _backoffMax = 300000;
        private readonly List<byte> _receiveBuffer = new List<byte>();
        private readonly TakProtocolSession _protocolSession;
        private int _backoff;
        private bool _stopping = false;
        public TakTransportMode TransportMode { get; }
        public TakTransportMode ActiveTransportMode => _protocolSession.ActiveTransportMode;
        public event Action<Message> MessageReceived;
        public event Action<TakTransportMode> TransportModeChanged;

        public TakClient(string address, int port, int? backoffMax = null, TakTransportMode transportMode = TakTransportMode.StreamingXml) : base(address, port)
        {
            _backoff = _backoffMin;
            TransportMode = transportMode;
            _protocolSession = new TakProtocolSession(transportMode);

            if (backoffMax != null)
                _backoffMax = (int)backoffMax;

        }

        protected override void OnConnected()
        {
            // reset backoff
            _backoff = _backoffMin;
            _receiveBuffer.Clear();
            _protocolSession.Reset();
        }

        public long Send(Message msg)
        {
            return Send(_protocolSession.Serialize(msg));
        }

        public bool SendAsync(Message msg)
        {
            return SendAsync(_protocolSession.Serialize(msg));
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

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            var previousMode = _protocolSession.ActiveTransportMode;

            for (var index = 0; index < size; index++)
            {
                _receiveBuffer.Add(buffer[offset + index]);
            }

            ProcessReceiveBuffer(previousMode);
        }

        internal static byte[] SerializeMessage(Message msg, TakTransportMode transportMode)
        {
            if (msg == null)
            {
                throw new ArgumentNullException(nameof(msg));
            }

            switch (transportMode)
            {
                case TakTransportMode.StreamingXml:
                    return BuildStreamingXmlMessage(msg);

                case TakTransportMode.StreamingProtobuf:
                    return msg.ToStreamingBytes(TakProtocolSession.ProtobufProtocolVersion);

                default:
                    throw new ArgumentOutOfRangeException(nameof(transportMode), transportMode, "Unknown TAK transport mode.");
            }
        }

        private static byte[] BuildStreamingXmlMessage(Message msg)
        {
            var xmlBytes = Encoding.UTF8.GetBytes(msg.ToXmlString());
            var payload = new byte[XmlDeclaration.Length + xmlBytes.Length];

            System.Buffer.BlockCopy(XmlDeclaration, 0, payload, 0, XmlDeclaration.Length);
            System.Buffer.BlockCopy(xmlBytes, 0, payload, XmlDeclaration.Length, xmlBytes.Length);

            return payload;
        }

        private void ProcessReceiveBuffer(TakTransportMode previousMode)
        {
            while (_receiveBuffer.Count > 0)
            {
                var snapshot = _receiveBuffer.ToArray();
                Message message;
                int bytesConsumed;

                var parsed = (_protocolSession.ActiveTransportMode == TakTransportMode.StreamingProtobuf)
                    ? TakMessageStreamParser.TryParseProtobuf(snapshot, 0, snapshot.Length, TakProtocolSession.ProtobufProtocolVersion, out message, out bytesConsumed)
                    : TakMessageStreamParser.TryParseXml(snapshot, 0, snapshot.Length, out message, out bytesConsumed);

                if (!parsed)
                {
                    break;
                }

                _receiveBuffer.RemoveRange(0, bytesConsumed);

                var outboundControlMessage = _protocolSession.ProcessIncoming(message);
                MessageReceived?.Invoke(message);

                if (outboundControlMessage != null)
                {
                    SendAsync(SerializeMessage(outboundControlMessage, TakTransportMode.StreamingXml));
                }

                if (previousMode != _protocolSession.ActiveTransportMode)
                {
                    previousMode = _protocolSession.ActiveTransportMode;
                    TransportModeChanged?.Invoke(previousMode);
                }
            }
        }
    }
}
