using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Velopack.Util;
#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#else
using Newtonsoft.Json;
#endif

namespace Velopack.Ipc
{
    /// <summary>
    /// The protocol constants shared with the Rust library (<c>velopack::ipc</c>). Both speak the identical protocol:
    /// a u32 little-endian length prefix followed by compact UTF-8 JSON, carrying an envelope with a <c>version</c>,
    /// a <c>type</c>, an optional correlation <c>id</c> and type-specific fields.
    /// </summary>
    internal static class IpcProtocol
    {
        /// <summary> The protocol version this library speaks. Receivers accept any version up to and including this one. </summary>
        public const int ProtocolVersion = 1;
        /// <summary> The largest frame payload either side accepts. </summary>
        public const int MaxMessageBytes = 1_048_576;
        /// <summary> How long a client waits for its request to be sent and answered. </summary>
        public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(5);
        /// <summary> How long an association launch keeps trying to reach the running instance. </summary>
        public static readonly TimeSpan ConnectWindow = TimeSpan.FromSeconds(5);
        /// <summary> The pause between connection attempts within <see cref="ConnectWindow"/>. </summary>
        public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
        /// <summary> The per-connection read/write timeout on the server side. </summary>
        public static readonly TimeSpan ServerIoTimeout = TimeSpan.FromSeconds(5);

        public const string TypeAssociation = "association";
        public const string TypeAck = "ack";
        public const string TypeError = "error";

        public const string ErrInvalidMessage = "invalid message";
        public const string ErrUnsupportedVersion = "unsupported version";
        public const string ErrUnknownMessageType = "unknown message type";
        public const string ErrUnhandledAssociation = "unhandled association";
    }

    /// <summary> One association activation (from the command line, or inside an <see cref="IpcMessage"/>): the CLI flag and its value. </summary>
    internal sealed class IpcItem
    {
        /// <summary> <c>--veloapp-fileassoc</c> or <c>--veloapp-urlproto</c>. </summary>
        public string Flag { get; set; } = "";
        /// <summary> The file path or URL. Never logged. </summary>
        public string Value { get; set; } = "";

        public IpcItem() { }

        public IpcItem(string flag, string value)
        {
            Flag = flag;
            Value = value;
        }
    }

    /// <summary>
    /// The message envelope, serialized by <see cref="CompiledJson"/>. Unknown fields are ignored by every receiver, so
    /// new optional fields and types can be added without bumping the version.
    /// </summary>
    internal sealed class IpcMessage
    {
        /// <summary> The protocol version of the sender (required; 0 or missing is invalid). </summary>
        public int Version { get; set; }
        /// <summary> The message type (<c>association</c>, <c>ack</c>, <c>error</c>, ...); null when missing (invalid). </summary>
        public string? Type { get; set; }
        /// <summary> An opaque correlation id (at most 64 chars); a reply copies the request's id. </summary>
        public string? Id { get; set; }
        /// <summary> The association items of an <c>association</c> message. </summary>
        public List<IpcItem>? Items { get; set; }
        /// <summary> The reason of an <c>error</c> message (one of the fixed <c>IpcProtocol.Err*</c> strings). </summary>
        public string? Message { get; set; }

        /// <summary> For the deserializer: a missing version stays 0. </summary>
        public IpcMessage() { }

        public IpcMessage(string type)
        {
            Version = IpcProtocol.ProtocolVersion;
            Type = type;
        }

        [JsonIgnore]
        public bool IsAck => Type == IpcProtocol.TypeAck;

        public static IpcMessage Ack() => new IpcMessage(IpcProtocol.TypeAck);

        public static IpcMessage Error(string message) => new IpcMessage(IpcProtocol.TypeError) { Message = message };

        /// <summary> Copies the correlation id of <paramref name="request"/> into this reply. </summary>
        public IpcMessage ReplyTo(IpcMessage request)
        {
            Id = request.Id;
            return this;
        }
    }

    /// <summary> The frame layer: a u32 little-endian length (1..MaxMessageBytes) followed by that many bytes of JSON. </summary>
    internal static class IpcFraming
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(IpcMessage message)
        {
            if (message.Items != null && message.Items.Count == 0) {
                message.Items = null; // omitted like serde's skip_serializing_if
            }

            var json = StrictUtf8.GetBytes(CompiledJson.SerializeIpcMessage(message));
            if (json.Length > IpcProtocol.MaxMessageBytes) {
                throw new InvalidDataException("message too large");
            }

            var frame = new byte[4 + json.Length];
            frame[0] = (byte) json.Length;
            frame[1] = (byte) (json.Length >> 8);
            frame[2] = (byte) (json.Length >> 16);
            frame[3] = (byte) (json.Length >> 24);
            Buffer.BlockCopy(json, 0, frame, 4, json.Length);
            return frame;
        }

        public static int DecodeLength(byte[] header)
        {
            var length = (uint) (header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            if (length == 0 || length > IpcProtocol.MaxMessageBytes) {
                throw new InvalidDataException($"invalid frame length {length}");
            }

            return (int) length;
        }

        /// <summary>
        /// Throws <see cref="FormatException"/> for invalid UTF-8, malformed JSON, a missing type or an item without a
        /// flag or value. A missing version decodes as 0, which receivers reject.
        /// </summary>
        public static IpcMessage Decode(byte[] payload)
        {
            IpcMessage? message;
            try {
                message = CompiledJson.DeserializeIpcMessage(StrictUtf8.GetString(payload));
            } catch (Exception ex) {
                throw new FormatException("malformed IPC message: " + ex.Message, ex);
            }

            if (message?.Type == null || (message.Items?.Exists(i => i?.Flag == null || i.Value == null) ?? false)) {
                throw new FormatException("malformed IPC message: missing type, flag or value");
            }

            return message;
        }
    }
}
