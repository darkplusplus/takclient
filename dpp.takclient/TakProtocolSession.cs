using System;
using System.Xml;
using dpp.cot;

namespace dpp.takclient
{
    internal sealed class TakProtocolSession
    {
        internal const byte ProtobufProtocolVersion = 0x01;

        public TakProtocolSession(TakTransportMode preferredTransportMode)
        {
            PreferredTransportMode = preferredTransportMode;
            Reset();
        }

        public TakTransportMode PreferredTransportMode { get; }

        public TakTransportMode ActiveTransportMode { get; private set; }

        public TakNegotiationState NegotiationState { get; private set; }

        public void Reset()
        {
            ActiveTransportMode = TakTransportMode.StreamingXml;
            NegotiationState = TakNegotiationState.Idle;
        }

        public byte[] Serialize(Message message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if ((NegotiationState == TakNegotiationState.AwaitingResponse) && !IsNegotiationMessage(message))
            {
                throw new InvalidOperationException("TAK protocol negotiation is in progress. Application messages cannot be sent until the server responds.");
            }

            return TakClient.SerializeMessage(message, ActiveTransportMode);
        }

        public Message ProcessIncoming(Message message)
        {
            if ((message?.Event == null) || (PreferredTransportMode != TakTransportMode.StreamingProtobuf))
            {
                return null;
            }

            if ((ActiveTransportMode == TakTransportMode.StreamingXml) &&
                (NegotiationState == TakNegotiationState.Idle) &&
                SupportsProtocolVersion(message, ProtobufProtocolVersion))
            {
                NegotiationState = TakNegotiationState.AwaitingResponse;
                return CreateProtocolRequest(ProtobufProtocolVersion);
            }

            if ((ActiveTransportMode == TakTransportMode.StreamingXml) &&
                (NegotiationState == TakNegotiationState.AwaitingResponse) &&
                TryGetResponseStatus(message, out var accepted))
            {
                if (accepted)
                {
                    ActiveTransportMode = TakTransportMode.StreamingProtobuf;
                    NegotiationState = TakNegotiationState.Complete;
                }
                else
                {
                    NegotiationState = TakNegotiationState.Idle;
                }
            }

            return null;
        }

        internal static bool IsNegotiationMessage(Message message)
        {
            var type = message?.Event?.Type;
            return (type == "t-x-takp-v") || (type == "t-x-takp-q") || (type == "t-x-takp-r");
        }

        internal static bool SupportsProtocolVersion(Message message, byte version)
        {
            if ((message?.Event?.Type != "t-x-takp-v") || (message.Event.Detail?.AdditionalElements == null))
            {
                return false;
            }

            foreach (var control in message.Event.Detail.AdditionalElements)
            {
                if ((control == null) || !IsElement(control, "TakControl"))
                {
                    continue;
                }

                foreach (XmlNode child in control.ChildNodes)
                {
                    if ((child is XmlElement element) &&
                        IsElement(element, "TakProtocolSupport") &&
                        byte.TryParse(element.GetAttribute("version"), out var supportedVersion) &&
                        (supportedVersion == version))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool TryGetResponseStatus(Message message, out bool status)
        {
            status = false;

            if ((message?.Event?.Type != "t-x-takp-r") || (message.Event.Detail?.AdditionalElements == null))
            {
                return false;
            }

            foreach (var control in message.Event.Detail.AdditionalElements)
            {
                if ((control == null) || !IsElement(control, "TakControl"))
                {
                    continue;
                }

                foreach (XmlNode child in control.ChildNodes)
                {
                    if ((child is XmlElement element) &&
                        IsElement(element, "TakResponse") &&
                        bool.TryParse(element.GetAttribute("status"), out status))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static Message CreateProtocolRequest(byte version)
        {
            return CreateProtocolControlMessage(
                "t-x-takp-q",
                "TakRequest",
                controlElement => controlElement.SetAttribute("version", version.ToString()));
        }

        private static Message CreateProtocolControlMessage(string eventType, string controlChildName, Action<XmlElement> configureControlChild)
        {
            var document = new XmlDocument();
            var takControl = document.CreateElement("TakControl");
            var controlChild = document.CreateElement(controlChildName);

            configureControlChild(controlChild);
            takControl.AppendChild(controlChild);

            return new Message
            {
                Event = new Event
                {
                    Version = "2.0",
                    Uid = "protouid",
                    Type = eventType,
                    How = "m-g",
                    Point = new Point
                    {
                        Lat = 0.0,
                        Lon = 0.0,
                        Hae = 0.0,
                        Ce = 999999.0,
                        Le = 999999.0,
                    },
                    Detail = new Detail
                    {
                        AdditionalElements = new[] { takControl },
                    },
                },
            };
        }

        private static bool IsElement(XmlElement element, string localName)
        {
            return string.Equals(element.LocalName, localName, StringComparison.Ordinal) ||
                   string.Equals(element.Name, localName, StringComparison.Ordinal);
        }
    }
}
