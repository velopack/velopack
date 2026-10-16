#nullable enable
using System.Text;
using Velopack.Ipc;

namespace Velopack.Tests;

// The JSON itself is CompiledJson's; these tests pin the wire format the Rust library (velopack::ipc) speaks.
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

    private static string Json(IpcMessage message)
    {
        var frame = IpcFraming.Encode(message);
        Assert.Equal(frame.Length - 4, frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24));
        return Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
    }

    private static IpcMessage Decode(string json) => IpcFraming.Decode(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void SerializesTheSameShapeAsRust()
    {
        Assert.Equal(
            """{"version":1,"type":"association","items":[{"flag":"--veloapp-urlproto","value":"myapp://x"}]}""",
            Json(Association("myapp://x")));
        Assert.Equal("""{"version":1,"type":"ack"}""", Json(IpcMessage.Ack()));
        Assert.Equal("""{"version":1,"type":"error","message":"unknown message type"}""", Json(IpcMessage.Error(IpcProtocol.ErrUnknownMessageType)));
        Assert.Equal("""{"version":1,"type":"ack","id":"7"}""", Json(IpcMessage.Ack().ReplyTo(new IpcMessage { Id = "7" })));
        // empty items are omitted, like serde's skip_serializing_if
        Assert.Equal("""{"version":1,"type":"association"}""", Json(Association()));
    }

    [Fact]
    public void RoundTripsEveryField()
    {
        var original = new IpcMessage(IpcProtocol.TypeAssociation) {
            Id = "corr-\"1\"",
            Message = "tab\there\nnew\\line\u0001",
            Items = new List<IpcItem> {
                new(VelopackApp.HookCliFileAssoc, "C:\\Users\\j\u00F6rg\\My \"Docs\"\\\U0001F600.myproj"),
                new(VelopackApp.HookCliUrlProto, "myapp://open?q=\"a b\"&x=\u20AC/\\"),
            },
        };
        var json = Json(original);
        Assert.DoesNotContain("\n", json); // compact

        var parsed = Decode(json);
        Assert.Equal((original.Version, original.Type, original.Id, original.Message), (parsed.Version, parsed.Type, parsed.Id, parsed.Message));
        Assert.Equal(original.Items.Select(i => (i.Flag, i.Value)), parsed.Items!.Select(i => (i.Flag, i.Value)));
    }

    [Fact]
    public void ParsesWhatRustSendsAndIgnoresUnknownFields()
    {
        // serde_json emits raw UTF-8; any field order, escapes and unknown fields of any shape are accepted
        var m = Decode(
            """
            { "extra" : { "a" : [ 1 , 2.5e3 , true , null , { "deep" : "x" } ] } , "type" : "association" , "id" : "7" ,
              "version" : 1 , "items" : [ { "flag" : "--veloapp-urlproto" , "junk" : [ ] , "value" : "\ud83d\ude00 \/ \" \\ €" } ] }
            """);
        Assert.Equal((1, "association", "7", (string?) null), (m.Version, m.Type, m.Id, m.Message));
        var item = Assert.Single(m.Items!);
        Assert.Equal(("--veloapp-urlproto", "\U0001F600 / \" \\ \u20AC"), (item.Flag, item.Value));

        // null id/message/items are accepted as absent; a missing version decodes as 0 (rejected by ServeRequest)
        m = Decode("""{"version":1,"type":"ack","id":null,"items":null,"message":null}""");
        Assert.Equal(((string?) null, (List<IpcItem>?) null, (string?) null), (m.Id, m.Items, m.Message));
        Assert.Equal(0, Decode("""{"type":"ack"}""").Version);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":"1","type":"ack"}""")]
    [InlineData("""{"version":1.5,"type":"ack"}""")]
    [InlineData("""{"version":1,"type":"ack"} trailing""")]
    [InlineData("""{"version":1,"type":"ack",}""")]
    [InlineData("""{"version":1,"type":"ack""")]
    [InlineData("""{"version":1,"type":"a\qb"}""")]
    [InlineData("""{"version":1,"type":"association","items":[{"flag":"--veloapp-urlproto","value":null}]}""")]
    [InlineData("[1]")]
    public void RejectsMalformedMessages(string json)
    {
        Assert.Throws<FormatException>(() => Decode(json));
    }

    [Fact]
    public void RejectsInvalidUtf8AndOversizedMessages()
    {
        Assert.Throws<FormatException>(() => IpcFraming.Decode(new byte[] { 0xC3, 0x28 }));
        Assert.Throws<InvalidDataException>(() => IpcFraming.Encode(Association(new string('x', IpcProtocol.MaxMessageBytes))));
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

    private static byte[] Frame(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return BitConverter.GetBytes((uint) bytes.Length).Concat(bytes).ToArray();
    }

    private static IpcMessage? Serve(byte[] input, Func<IpcMessage, IpcMessage> handler)
    {
        var duplex = new DuplexStream(input);
        using (var conn = new IpcConnection(duplex)) {
            conn.ServeRequest(handler);
        }

        if (duplex.Output.Length == 0) {
            return null;
        }

        duplex.Output.Position = 0;
        using var reader = new IpcConnection(duplex.Output);
        var reply = reader.Receive();
        Assert.Equal(duplex.Output.Length, duplex.Output.Position); // exactly one reply frame
        return reply;
    }

    [Fact]
    public void FramesRoundTripAndBadFramesAreRejected()
    {
        var stream = new MemoryStream();
        var conn = new IpcConnection(stream);
        conn.Send(Association("myapp://x"));
        conn.Send(IpcMessage.Ack());
        stream.Position = 0;
        Assert.Equal("myapp://x", conn.Receive().Items![0].Value);
        Assert.True(conn.Receive().IsAck);

        Assert.Throws<InvalidDataException>(() => new IpcConnection(new MemoryStream(new byte[] { 0, 0, 0, 0 })).Receive());
        var huge = BitConverter.GetBytes((uint) IpcProtocol.MaxMessageBytes + 1);
        Assert.Throws<InvalidDataException>(() => new IpcConnection(new MemoryStream(huge)).Receive());
        var truncated = BitConverter.GetBytes(10u).Concat(Encoding.UTF8.GetBytes("{\"ver")).ToArray();
        Assert.Throws<EndOfStreamException>(() => new IpcConnection(new MemoryStream(truncated)).Receive());
        Assert.Throws<EndOfStreamException>(() => new IpcConnection(new MemoryStream(new byte[] { 1, 0 })).Receive());
    }

    [Fact]
    public void ServeRequestReplies()
    {
        Func<IpcMessage, IpcMessage> unreachable = _ => throw new InvalidOperationException("handler must not run");

        var reply = Serve(Frame("not json"), unreachable)!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrInvalidMessage), (reply.Type, reply.Message));

        reply = Serve(Frame("""{"version":2,"type":"ack","id":"x"}"""), unreachable)!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrUnsupportedVersion, "x"), (reply.Type, reply.Message, reply.Id));

        foreach (var invalid in new[] { """{"version":0,"type":"ack"}""", """{"type":"ack"}""", """{"version":-1,"type":"ack"}""" }) {
            reply = Serve(Frame(invalid), unreachable)!;
            Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrInvalidMessage), (reply.Type, reply.Message));
        }

        // a nesting deeper than the JSON reader allows is an invalid message, never a crash of the serving app
        var deep = """{"version":1,"type":"ack","x":""" + new string('[', 100_000) + new string(']', 100_000) + "}";
        reply = Serve(Frame(deep), unreachable)!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrInvalidMessage), (reply.Type, reply.Message));

        // the handler's reply (error or ack) passes through, and it is responsible for the id
        reply = Serve(Frame("""{"version":1,"type":"bogus","id":"abc"}"""), m => IpcMessage.Error(IpcProtocol.ErrUnknownMessageType).ReplyTo(m))!;
        Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrUnknownMessageType, "abc"), (reply.Type, reply.Message, reply.Id));

        reply = Serve(Frame("""{"version":1,"type":"association","id":"42","items":[]}"""), m => IpcMessage.Ack().ReplyTo(m))!;
        Assert.True(reply.IsAck);
        Assert.Equal("42", reply.Id);

        // a framing error gets no reply at all
        Assert.Null(Serve(new byte[] { 0, 0, 0, 0 }, unreachable));
        Assert.Null(Serve(BitConverter.GetBytes(10u).Concat(Encoding.UTF8.GetBytes("{\"ver")).ToArray(), unreachable));
    }
}
