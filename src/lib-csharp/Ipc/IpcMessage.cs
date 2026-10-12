using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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

    /// <summary> One association activation inside an <see cref="IpcMessage"/>: the canonical CLI flag and its value. </summary>
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
    /// The message envelope. Unknown fields are ignored by every receiver, so new optional fields and types can be
    /// added without bumping the version.
    /// </summary>
    internal sealed class IpcMessage
    {
        /// <summary> The protocol version of the sender (required; 0 is invalid). </summary>
        public int Version { get; set; } = IpcProtocol.ProtocolVersion;
        /// <summary> The message type (<c>association</c>, <c>ack</c>, <c>error</c>, ...). </summary>
        public string Type { get; set; } = "";
        /// <summary> An opaque correlation id (at most 64 chars); a reply copies the request's id. </summary>
        public string? Id { get; set; }
        /// <summary> The association items of an <c>association</c> message. </summary>
        public List<IpcItem>? Items { get; set; }
        /// <summary> The reason of an <c>error</c> message (one of the fixed <c>IpcProtocol.Err*</c> strings). </summary>
        public string? Message { get; set; }

        public IpcMessage() { }

        public IpcMessage(string type)
        {
            Type = type;
        }

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

    /// <summary>
    /// A hand-written JSON codec for <see cref="IpcMessage"/>, the same source on every target framework (so the
    /// netstandard2.0/net472 builds run exactly the code the tests cover). Strings are escaped minimally (<c>"</c>,
    /// <c>\</c> and control characters as <c>\u00XX</c>; non-ASCII is emitted raw) and parsed with every standard
    /// escape. Unknown fields of any shape are skipped.
    /// </summary>
    internal static class IpcJson
    {
        public static string Serialize(IpcMessage message)
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":").Append(message.Version.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"type\":");
            WriteString(sb, message.Type);
            if (message.Id != null) {
                sb.Append(",\"id\":");
                WriteString(sb, message.Id);
            }

            if (message.Items != null && message.Items.Count > 0) {
                sb.Append(",\"items\":[");
                for (var i = 0; i < message.Items.Count; i++) {
                    if (i > 0) {
                        sb.Append(',');
                    }

                    sb.Append("{\"flag\":");
                    WriteString(sb, message.Items[i].Flag);
                    sb.Append(",\"value\":");
                    WriteString(sb, message.Items[i].Value);
                    sb.Append('}');
                }

                sb.Append(']');
            }

            if (message.Message != null) {
                sb.Append(",\"message\":");
                WriteString(sb, message.Message);
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary> Throws <see cref="FormatException"/> on malformed JSON, a missing/non-integer version or a missing type. </summary>
        public static IpcMessage Deserialize(string json)
        {
            var reader = new Reader(json);
            reader.SkipWhitespace();
            var message = reader.ReadMessage();
            reader.SkipWhitespace();
            if (!reader.AtEnd) {
                throw new FormatException("trailing characters after the JSON object");
            }

            return message;
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value) {
                switch (c) {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                default:
                    if (c < 0x20) {
                        sb.Append("\\u").Append(((int) c).ToString("x4", CultureInfo.InvariantCulture));
                    } else {
                        sb.Append(c);
                    }

                    break;
                }
            }

            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _pos;

            public Reader(string s)
            {
                _s = s;
            }

            public bool AtEnd => _pos >= _s.Length;

            private char Peek()
            {
                if (_pos >= _s.Length) {
                    throw new FormatException("unexpected end of JSON");
                }

                return _s[_pos];
            }

            private char Next()
            {
                var c = Peek();
                _pos++;
                return c;
            }

            private void Expect(char c)
            {
                if (Next() != c) {
                    throw new FormatException($"expected '{c}' at position {_pos - 1}");
                }
            }

            public void SkipWhitespace()
            {
                while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t' || _s[_pos] == '\n' || _s[_pos] == '\r')) {
                    _pos++;
                }
            }

            public IpcMessage ReadMessage()
            {
                var message = new IpcMessage();
                var hasVersion = false;
                var hasType = false;
                ReadObject(name => {
                    switch (name) {
                    case "version":
                        message.Version = ReadInt();
                        hasVersion = true;
                        break;
                    case "type":
                        message.Type = ReadString();
                        hasType = true;
                        break;
                    case "id":
                        message.Id = ReadNullableString();
                        break;
                    case "message":
                        message.Message = ReadNullableString();
                        break;
                    case "items":
                        message.Items = ReadItems();
                        break;
                    default:
                        SkipValue();
                        break;
                    }
                });
                if (!hasVersion || !hasType) {
                    throw new FormatException("missing version or type");
                }

                return message;
            }

            private List<IpcItem>? ReadItems()
            {
                SkipWhitespace();
                if (TryReadNull()) {
                    return null;
                }

                var items = new List<IpcItem>();
                ReadArray(() => {
                    var item = new IpcItem();
                    ReadObject(name => {
                        switch (name) {
                        case "flag":
                            item.Flag = ReadString();
                            break;
                        case "value":
                            item.Value = ReadString();
                            break;
                        default:
                            SkipValue();
                            break;
                        }
                    });
                    items.Add(item);
                });
                return items;
            }

            /// <summary> Reads <c>{ "name": value, ... }</c>, calling <paramref name="onMember"/> positioned at each value. </summary>
            private void ReadObject(Action<string> onMember)
            {
                SkipWhitespace();
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}') {
                    _pos++;
                    return;
                }

                while (true) {
                    SkipWhitespace();
                    var name = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    onMember(name);
                    SkipWhitespace();
                    var c = Next();
                    if (c == '}') {
                        return;
                    }

                    if (c != ',') {
                        throw new FormatException($"expected ',' or '}}' at position {_pos - 1}");
                    }
                }
            }

            private void ReadArray(Action onElement)
            {
                SkipWhitespace();
                Expect('[');
                SkipWhitespace();
                if (Peek() == ']') {
                    _pos++;
                    return;
                }

                while (true) {
                    SkipWhitespace();
                    onElement();
                    SkipWhitespace();
                    var c = Next();
                    if (c == ']') {
                        return;
                    }

                    if (c != ',') {
                        throw new FormatException($"expected ',' or ']' at position {_pos - 1}");
                    }
                }
            }

            private bool TryReadNull()
            {
                if (string.CompareOrdinal(_s, _pos, "null", 0, 4) == 0) {
                    _pos += 4;
                    return true;
                }

                return false;
            }

            private string? ReadNullableString()
            {
                SkipWhitespace();
                return TryReadNull() ? null : ReadString();
            }

            private int ReadInt()
            {
                SkipWhitespace();
                var start = _pos;
                while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') {
                    _pos++;
                }

                if (_pos == start || (_pos < _s.Length && (_s[_pos] == '.' || _s[_pos] == 'e' || _s[_pos] == 'E'))) {
                    throw new FormatException("expected a non-negative integer");
                }

                if (!int.TryParse(_s.Substring(start, _pos - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) {
                    throw new FormatException("integer out of range");
                }

                return value;
            }

            private string ReadString()
            {
                SkipWhitespace();
                Expect('"');
                var sb = new StringBuilder();
                while (true) {
                    var c = Next();
                    if (c == '"') {
                        return sb.ToString();
                    }

                    if (c != '\\') {
                        if (c < 0x20) {
                            throw new FormatException("unescaped control character in string");
                        }

                        sb.Append(c);
                        continue;
                    }

                    var e = Next();
                    switch (e) {
                    case '"':
                    case '\\':
                    case '/':
                        sb.Append(e);
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        // surrogate pairs arrive as two consecutive \uXXXX escapes and are appended as two chars
                        if (_pos + 4 > _s.Length) {
                            throw new FormatException("truncated \\u escape");
                        }

                        if (!int.TryParse(_s.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) {
                            throw new FormatException("invalid \\u escape");
                        }

                        _pos += 4;
                        sb.Append((char) code);
                        break;
                    default:
                        throw new FormatException($"invalid escape '\\{e}'");
                    }
                }
            }

            /// <summary> Skips one value of any shape (object, array, string, number, literal). </summary>
            private void SkipValue()
            {
                SkipWhitespace();
                var c = Peek();
                switch (c) {
                case '{':
                    ReadObject(_ => SkipValue());
                    break;
                case '[':
                    ReadArray(SkipValue);
                    break;
                case '"':
                    ReadString();
                    break;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) {
                        while (_pos < _s.Length && "+-.eE0123456789".IndexOf(_s[_pos]) >= 0) {
                            _pos++;
                        }
                    } else if (TryReadLiteral("true") || TryReadLiteral("false") || TryReadLiteral("null")) {
                        // consumed
                    } else {
                        throw new FormatException($"unexpected character '{c}' at position {_pos}");
                    }

                    break;
                }
            }

            private bool TryReadLiteral(string literal)
            {
                if (string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) == 0) {
                    _pos += literal.Length;
                    return true;
                }

                return false;
            }
        }
    }

    /// <summary> The frame layer: a u32 little-endian length (1..MaxMessageBytes) followed by that many bytes of JSON. </summary>
    internal static class IpcFraming
    {
        public static byte[] Encode(IpcMessage message)
        {
            var json = Encoding.UTF8.GetBytes(IpcJson.Serialize(message));
            if (json.Length == 0 || json.Length > IpcProtocol.MaxMessageBytes) {
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

        public static void WriteFrame(Stream stream, IpcMessage message)
        {
            var frame = Encode(message);
            stream.Write(frame, 0, frame.Length);
        }

        /// <summary> Reads one frame payload from a plain stream (no timeout); see <see cref="IpcConnection"/> for timed reads. </summary>
        public static byte[] ReadFrame(Stream stream)
        {
            var header = new byte[4];
            ReadExactly(stream, header);
            var length = DecodeLength(header);
            var payload = new byte[length];
            ReadExactly(stream, payload);
            return payload;
        }

        public static int DecodeLength(byte[] header)
        {
            var length = (uint) (header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            if (length == 0 || length > IpcProtocol.MaxMessageBytes) {
                throw new InvalidDataException($"invalid frame length {length}");
            }

            return (int) length;
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary> Throws <see cref="FormatException"/> for invalid UTF-8 as well as for malformed JSON. </summary>
        public static IpcMessage Decode(byte[] payload)
        {
            string json;
            try {
                json = StrictUtf8.GetString(payload);
            } catch (DecoderFallbackException ex) {
                throw new FormatException("the frame is not valid UTF-8", ex);
            }

            return IpcJson.Deserialize(json);
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length) {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) {
                    throw new EndOfStreamException("the connection was closed inside a frame");
                }

                offset += read;
            }
        }
    }
}
