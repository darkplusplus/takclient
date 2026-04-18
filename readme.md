# dpp.takclient

This library provides a consistent interface in conjunction with `dpp.cot` to interface with a TAK server.

## Transport Modes

`TakClient` supports two explicit TAK server transport modes:

- `TakTransportMode.StreamingXml`
- `TakTransportMode.StreamingProtobuf`

`StreamingXml` sends the traditional XML stream format expected by TAK servers:

- XML declaration
- newline
- CoT `<event>` payload

`StreamingProtobuf` sends TAK protobuf payloads using the streaming envelope from `dpp.cot`.

When `StreamingProtobuf` is selected, the client still begins in XML mode as required by the TAK protocol negotiation flow. If the server advertises protobuf support, `TakClient` sends a `t-x-takp-q` request, waits for a `t-x-takp-r` response, and only then switches the active transport to protobuf.

## Receive Behavior

`TakClient` parses inbound TAK XML stream messages and TAK protobuf streaming frames.

The client exposes:

- `MessageReceived` for parsed inbound CoT messages
- `TransportModeChanged` when a negotiated connection switches from XML to protobuf

While a protobuf negotiation request is outstanding, application messages are blocked until the server responds.

## Dependency

This library targets `dpp.cot` `2.0.0`.
