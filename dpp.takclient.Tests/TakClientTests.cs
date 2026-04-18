using System;
using System.Text;
using dpp.cot;
using Xunit;

namespace dpp.takclient.Tests
{
    public class TakClientTests
    {
        private const string SupportAdvertisementXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<event version='2.0' uid='protouid' type='t-x-takp-v' time='2022-02-02T22:22:22Z' start='2022-02-02T22:22:22Z' stale='2022-02-02T22:32:22Z' how='m-g'>" +
            "<point lat='0.0' lon='0.0' hae='0.0' ce='999999' le='999999'/>" +
            "<detail><TakControl><TakProtocolSupport version='1'/></TakControl></detail>" +
            "</event>";

        private const string NegotiationAcceptedXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<event version='2.0' uid='protouid' type='t-x-takp-r' time='2022-02-02T22:22:22Z' start='2022-02-02T22:22:22Z' stale='2022-02-02T22:32:22Z' how='m-g'>" +
            "<point lat='0.0' lon='0.0' hae='0.0' ce='999999' le='999999'/>" +
            "<detail><TakControl><TakResponse status='true'/></TakControl></detail>" +
            "</event>";

        [Fact]
        public void StreamingXmlUsesXmlDeclarationAndRawEvent()
        {
            var message = new Message
            {
                Event = new Event
                {
                    Uid = "TEST-XML",
                    Type = "a-f-G-U-C",
                    How = "m-g",
                    Time = new DateTime(2022, 2, 2, 22, 22, 22, DateTimeKind.Utc),
                    Start = new DateTime(2022, 2, 2, 22, 22, 22, DateTimeKind.Utc),
                    Stale = new DateTime(2022, 2, 2, 22, 32, 22, DateTimeKind.Utc),
                }
            };

            var bytes = TakClient.SerializeMessage(message, TakTransportMode.StreamingXml);
            var payload = Encoding.UTF8.GetString(bytes);

            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<event", payload);
            Assert.Contains("uid=\"TEST-XML\"", payload);
            Assert.DoesNotContain("\u00ef", payload);
            Assert.DoesNotContain("magic", payload);
        }

        [Fact]
        public void StreamingProtobufUsesTakStreamingEnvelope()
        {
            var message = new Message
            {
                Event = new Event
                {
                    Uid = "TEST-PROTO",
                    Type = "a-f-G-U-C",
                    How = "m-g",
                    Time = new DateTime(2022, 2, 2, 22, 22, 22, DateTimeKind.Utc),
                    Start = new DateTime(2022, 2, 2, 22, 22, 22, DateTimeKind.Utc),
                    Stale = new DateTime(2022, 2, 2, 22, 32, 22, DateTimeKind.Utc),
                }
            };

            var bytes = TakClient.SerializeMessage(message, TakTransportMode.StreamingProtobuf);

            Assert.NotEmpty(bytes);
            Assert.Equal(0xbf, bytes[0]);

            Assert.True(Message.TryParseStreaming(bytes, 0, bytes.Length, 0x01, out var parsed, out var consumed));
            Assert.Equal(bytes.Length, consumed);
            Assert.NotNull(parsed.Event);
            Assert.Equal("TEST-PROTO", parsed.Event.Uid);
        }

        [Fact]
        public void NullMessageThrows()
        {
            Assert.Throws<ArgumentNullException>(() => TakClient.SerializeMessage(null, TakTransportMode.StreamingXml));
        }

        [Fact]
        public void ProtocolSessionRequestsProtobufWhenServerAdvertisesSupport()
        {
            var session = new TakProtocolSession(TakTransportMode.StreamingProtobuf);
            var advertisement = Message.Parse(Encoding.UTF8.GetBytes(SupportAdvertisementXml), 39, SupportAdvertisementXml.Length - 39);

            var request = session.ProcessIncoming(advertisement);

            Assert.NotNull(request);
            Assert.Equal(TakTransportMode.StreamingXml, session.ActiveTransportMode);
            Assert.Equal(TakNegotiationState.AwaitingResponse, session.NegotiationState);
            Assert.Equal("t-x-takp-q", request.Event.Type);
            Assert.Contains("version=\"1\"", request.ToXmlString());
        }

        [Fact]
        public void ProtocolSessionSwitchesToProtobufAfterPositiveResponse()
        {
            var session = new TakProtocolSession(TakTransportMode.StreamingProtobuf);
            var advertisement = Message.Parse(Encoding.UTF8.GetBytes(SupportAdvertisementXml), 39, SupportAdvertisementXml.Length - 39);
            var accepted = Message.Parse(Encoding.UTF8.GetBytes(NegotiationAcceptedXml), 39, NegotiationAcceptedXml.Length - 39);

            session.ProcessIncoming(advertisement);
            var followUp = session.ProcessIncoming(accepted);

            Assert.Null(followUp);
            Assert.Equal(TakTransportMode.StreamingProtobuf, session.ActiveTransportMode);
            Assert.Equal(TakNegotiationState.Complete, session.NegotiationState);
        }

        [Fact]
        public void ProtocolSessionBlocksApplicationMessagesWhileAwaitingResponse()
        {
            var session = new TakProtocolSession(TakTransportMode.StreamingProtobuf);
            var advertisement = Message.Parse(Encoding.UTF8.GetBytes(SupportAdvertisementXml), 39, SupportAdvertisementXml.Length - 39);

            session.ProcessIncoming(advertisement);

            Assert.Throws<InvalidOperationException>(() => session.Serialize(new Message
            {
                Event = new Event
                {
                    Uid = "TEST-BLOCK",
                    Type = "a-f-G-U-C",
                    How = "m-g",
                },
            }));
        }

        [Fact]
        public void XmlStreamParserConsumesDeclarationAndReturnsMessage()
        {
            var xmlBytes = Encoding.UTF8.GetBytes(SupportAdvertisementXml);

            Assert.True(TakMessageStreamParser.TryParseXml(xmlBytes, 0, xmlBytes.Length, out var message, out var consumed));
            Assert.Equal(xmlBytes.Length, consumed);
            Assert.NotNull(message.Event);
            Assert.Equal("t-x-takp-v", message.Event.Type);
        }
    }
}
