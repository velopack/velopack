using System.Text;
using Velopack.Ipc;

namespace Velopack.Tests;

public class IpcProtocolTests
{
    private static IpcMessage Association(params string[] values)
    {
        var m = new IpcMessage(IpcProtocol.TypeAssociation) { Items = new List<IpcItem>() };
        foreach (var v in values) {
            m.Items.Add(new IpcItem(VelopackApp.HookCliUrlProto, v));
        }

        return m;
    }

    [Fact]
    public void SerializesTheSameShapeAsRust()
    {
        Assert.Equal(
            """{"version":1,"type":"association","items":[{"flag":"--veloapp-urlproto","value":"myapp://x"}]}""",
            IpcJson.Serialize(Association("myapp://x")));
        Assert.Equal("""{"version":1,"type":"ack"}""", IpcJson.Serialize(IpcMessage.Ack()));
        Assert.Equal(
            """{"version":1,"type":"error","message":"unknown message type"}""",
            IpcJson.Serialize(IpcMessage.Error(IpcProtocol.ErrUnknownMessageType)));
        Assert.Equal("""{"version":1,"type":"ack","id":"7"}""", IpcJson.Serialize(IpcMessage.Ack().ReplyTo(new IpcMessage { Id = "7" })));
        // empty items are omitted, like serde's skip_serializing_if
        Assert.Equal("""{"version":1,"type":"association"}""", IpcJson.Serialize(Association()));
    }

    [Fact]
    public void RoundTripsEveryFieldAndEscapes()
    {
        var original = new IpcMessage(IpcProtocol.TypeAssociation) {
            Id = "corr-\"1\"",
            Message = "tab\there\nnew\\line\u0001",
            Items = new List<IpcItem> {
                new(VelopackApp.HookCliFileAssoc, "C:\\Users\\j\u00F6rg\\My \"Docs\"\\\U0001F600.myproj"),
                new(VelopackApp.HookCliUrlProto, "myapp://open?q=\"a b\"&x=\u20AC/\\"),
            },
        };
        var json = IpcJson.Serialize(original);
        // non-ASCII is raw, control characters are \u00XX, quotes and backslashes are escaped
        Assert.Contains("j\u00F6rg", json);
        Assert.Contains("\\u0009", json);
        Assert.Contains("\\u0001", json);
        Assert.Contains("\\\"Docs\\\"", json);
        Assert.DoesNotContain("\n", json);

        var parsed = IpcJson.Deserialize(json);
        Assert.Equal(original.Version, parsed.Version);
        Assert.Equal(original.Type, parsed.Type);
        Assert.Equal(original.Id, parsed.Id);
        Assert.Equal(original.Message, parsed.Message);
        Assert.Equal(original.Items.Select(i => (i.Flag, i.Value)), parsed.Items!.Select(i => (i.Flag, i.Value)));
    }

    [Fact]
    public void ParsesEscapesSurrogatePairsUnknownFieldsAndWhitespace()
    {
        var json = """
                   { "extra" : { "a" : [ 1 , 2.5e3 , true , null , { "deep" : "x" } ] , "b" : "\u0041" } ,
                     "type" : "association" , "id" : "7" , "version" : 1 ,
                     "items" : [ { "flag" : "--veloapp-urlproto" , "junk" : [ ] , "value" : "\ud83d\ude00 \/ \b\f\n\r\t \" \\ \u20ac" } ] }
                   """;
        var m = IpcJson.Deserialize(json);
        Assert.Equal(1, m.Version);
        Assert.Equal("association", m.Type);
        Assert.Equal("7", m.Id);
        Assert.Null(m.Message);
        var item = Assert.Single(m.Items!);
        Assert.Equal("--veloapp-urlproto", item.Flag);
        Assert.Equal("\U0001F600 / \b\f\n\r\t \" \\ \u20AC", item.Value);

        // null id/message/items are accepted as absent
        m = IpcJson.Deserialize("""{"version":1,"type":"ack","id":null,"items":null,"message":null}""");
        Assert.Null(m.Id);
        Assert.Null(m.Items);
        Assert.Null(m.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"type":"ack"}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":"1","type":"ack"}""")]
    [InlineData("""{"version":1.0,"type":"ack"}""")]
    [InlineData("""{"version":-1,"type":"ack"}""")]
    [InlineData("""{"version":1,"type":"ack"} trailing""")]
    [InlineData("""{"version":1,"type":"ack",}""")]
    [InlineData("""{"version":1,"type":"ack""")]
    [InlineData("""{"version":1,"type":"a\qb"}""")]
    [InlineData("""{"version":1,"type":"a\u12"}""")]
    [InlineData("[1]")]
    public void RejectsMalformedMessages(string json)
    {
        Assert.Throws<FormatException>(() => IpcJson.Deserialize(json));
    }

    [Fact]
    public void DeeplyNestedUnknownValuesAreSkippedWithoutRecursion()
    {
        // a recursive skip would overflow a thread-pool thread's stack at a few thousand levels and kill the serving
        // app; serde_json ignores unknown values iteratively at any depth, and so does IpcJson
        string Nested(int depth, string open, string close) =>
            string.Concat(Enumerable.Repeat(open, depth)) + "0" + string.Concat(Enumerable.Repeat(close, depth));
        var arrays = """{"version":1,"type":"ack","x":""" + Nested(100_000, "[", "]") + "}";
        var objects = """{"version":1,"type":"ack","x":""" + Nested(100_000, """{"a":""", "}") + ""","id":"7"}""";
        var inItems = """{"version":1,"type":"association","items":[{"flag":"f","junk":""" + Nested(100_000, """[{"a":""", "}]") +
                      ""","value":"v"}]}""";
        Assert.True(IpcJson.Deserialize(arrays).IsAck);
        Assert.Equal("7", IpcJson.Deserialize(objects).Id);
        Assert.Equal("v", Assert.Single(IpcJson.Deserialize(inItems).Items!).Value);
        Assert.True(Serve(arrays, m => IpcMessage.Ack().ReplyTo(m))!.IsAck);

        // the skipped value is still validated
        foreach (var bad in new[] { "[1,]", """{"a":1,}""", "[1 2]", """{"a" 1}""", "[}", "{]", "[", "{\"a\":" }) {
            Assert.Throws<FormatException>(() => IpcJson.Deserialize("""{"version":1,"type":"ack","x":""" + bad + "}"));
        }
    }

    [Fact]
    public void FrameRoundTripsThroughAStream()
    {
        var message = Association("myapp://x");
        var frame = IpcFraming.Encode(message);
        var json = Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
        Assert.Equal(IpcJson.Serialize(message), json);
        Assert.Equal(json.Length, frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24));

        var stream = new MemoryStream();
        IpcFraming.WriteFrame(stream, message);
        IpcFraming.WriteFrame(stream, IpcMessage.Ack());
        stream.Position = 0;
        Assert.Equal("myapp://x", IpcFraming.Decode(IpcFraming.ReadFrame(stream)).Items![0].Value);
        Assert.True(IpcFraming.Decode(IpcFraming.ReadFrame(stream)).IsAck);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void RejectsBadFrameLengthsAndTruncation()
    {
        Assert.Throws<InvalidDataException>(() => IpcFraming.ReadFrame(new MemoryStream(new byte[] { 0, 0, 0, 0 })));
        var huge = BitConverter.GetBytes((uint) IpcProtocol.MaxMessageBytes + 1);
        Assert.Throws<InvalidDataException>(() => IpcFraming.ReadFrame(new MemoryStream(huge)));
        var truncated = BitConverter.GetBytes(10u).Concat(Encoding.UTF8.GetBytes("{\"ver")).ToArray();
        Assert.Throws<EndOfStreamException>(() => IpcFraming.ReadFrame(new MemoryStream(truncated)));
        Assert.Throws<EndOfStreamException>(() => IpcFraming.ReadFrame(new MemoryStream(new byte[] { 1, 0 })));
        // the connection applies the same rules (via a plain stream)
        using var conn = new IpcConnection(new MemoryStream(truncated));
        Assert.Throws<EndOfStreamException>(() => conn.Receive());
        // invalid UTF-8 inside a frame is a malformed message
        var badUtf8 = BitConverter.GetBytes(2u).Concat(new byte[] { 0xC3, 0x28 }).ToArray();
        Assert.Throws<FormatException>(() => IpcFraming.Decode(IpcFraming.ReadFrame(new MemoryStream(badUtf8))));
    }

    /// <summary> An in-memory duplex: reads come from the request bytes, writes land in Output. </summary>
    private sealed class DuplexStream : Stream
    {
        private readonly MemoryStream _input;
        public MemoryStream Output { get; } = new();

        public DuplexStream(byte[] input) => _input = new MemoryStream(input);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Output.Write(buffer, offset, count);
    }

    private static IpcMessage? Serve(string payload, Func<IpcMessage, IpcMessage> handler)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var input = BitConverter.GetBytes((uint) bytes.Length).Concat(bytes).ToArray();
        return ServeRaw(input, handler);
    }

    private static IpcMessage? ServeRaw(byte[] input, Func<IpcMessage, IpcMessage> handler)
    {
        var duplex = new DuplexStream(input);
        using (var conn = new IpcConnection(duplex)) {
            conn.ServeRequest(handler);
        }

        if (duplex.Output.Length == 0) {
            return null;
        }

        duplex.Output.Position = 0;
        var reply = IpcFraming.Decode(IpcFraming.ReadFrame(duplex.Output));
        Assert.Equal(duplex.Output.Length, duplex.Output.Position); // exactly one reply frame
        return reply;
    }

    [Fact]
    public void ServeRequestReplies()
    {
        Func<IpcMessage, IpcMessage> unreachable = _ => throw new InvalidOperationException("handler must not run");

        var reply = Serve("not json", unreachable)!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrInvalidMessage), (reply.Type, reply.Message));

        reply = Serve("""{"version":2,"type":"ack","id":"x"}""", unreachable)!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrUnsupportedVersion), (reply.Type, reply.Message));
        Assert.Equal("x", reply.Id);

        reply = Serve("""{"version":0,"type":"ack"}""", unreachable)!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrInvalidMessage), (reply.Type, reply.Message));

        // the handler's reply (error or ack) passes through, and it is responsible for the id
        reply = Serve("""{"version":1,"type":"bogus","id":"abc"}""", m => IpcMessage.Error(IpcProtocol.ErrUnknownMessageType).ReplyTo(m))!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrUnknownMessageType), (reply.Type, reply.Message));
        Assert.Equal("abc", reply.Id);

        reply = Serve("""{"version":1,"type":"association","id":"42","items":[]}""", m => IpcMessage.Ack().ReplyTo(m))!;
        Assert.True(reply.IsAck);
        Assert.Equal("42", reply.Id);

        // a framing error gets no reply at all
        Assert.Null(ServeRaw(new byte[] { 0, 0, 0, 0 }, unreachable));
        Assert.Null(ServeRaw(BitConverter.GetBytes(10u).Concat(Encoding.UTF8.GetBytes("{\"ver")).ToArray(), unreachable));
    }
}
