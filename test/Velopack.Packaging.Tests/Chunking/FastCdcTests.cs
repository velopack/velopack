using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Velopack.Packaging.Chunking;

namespace Velopack.Packaging.Tests.Chunking;

public class FastCdcTests
{
    private static string LengthsSha256(IEnumerable<int> lengths)
    {
        var ms = new MemoryStream();
        foreach (var len in lengths) ms.Write(BitConverter.GetBytes((uint) len));
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    [Fact]
    public void GearTableGolden()
    {
        var table = GearTable.Table;
        Assert.Equal(256, table.Length);
        Assert.Equal(0xD176D219BDB4EC54UL, table[0]);
        Assert.Equal(0xF388586F617F38B9UL, table[1]);
        Assert.Equal(0xF823673E0DBA937CUL, table[255]);

        var ms = new MemoryStream();
        foreach (var v in table) ms.Write(BitConverter.GetBytes(v));
        Assert.Equal("D82385ADFED88D11F3DD3DDB4028CFD4E1ED8ACB8C6C387E6EA7759DA39607CD", Convert.ToHexString(SHA256.HashData(ms.ToArray())));
    }

    [Fact]
    public void Vector1Golden()
    {
        var data = ChunkTestTree.RandomBytes(1, 4 * 1024 * 1024);
        Assert.Equal("AD6BC3DEF4BC89ADB670062BB9B9F48CD0AA748258F31A304D1DF877D46CC6FE", Convert.ToHexString(SHA256.HashData(data)));

        var lengths = FastCdc.Chunk(data);
        Assert.Equal(60, lengths.Count);
        Assert.Equal([78906, 110685, 19000, 82052, 85964, 91242, 21314, 68305], lengths.Take(8));
        Assert.Equal(66477, lengths[^1]);
        Assert.Equal(18387, lengths.Min());
        Assert.Equal(233193, lengths.Max());
        Assert.Equal(data.Length, lengths.Sum());
        Assert.Equal("9E0F146E99494451B545879DF7438D45DF2A5AFC4DCE5E97DE564BD444F88FA3", LengthsSha256(lengths));
        var chunk0 = Convert.ToHexString(SHA256.HashData(data.AsSpan(0, lengths[0])));
        Assert.Equal("49C947FC97C644CF031BBD1BB79ED8D43E9E1E3260C55D428C9E7993E49D198F", chunk0);
    }

    [Fact]
    public void Vector2ShiftResistance()
    {
        var baseData = ChunkTestTree.RandomBytes(1, 4 * 1024 * 1024);
        var insert = ChunkTestTree.RandomBytes(7, 1000);
        const int insertAt = 1024 * 1024;
        var data = new byte[baseData.Length + insert.Length];
        Buffer.BlockCopy(baseData, 0, data, 0, insertAt);
        Buffer.BlockCopy(insert, 0, data, insertAt, insert.Length);
        Buffer.BlockCopy(baseData, insertAt, data, insertAt + insert.Length, baseData.Length - insertAt);

        var baseLengths = FastCdc.Chunk(baseData);
        var lengths = FastCdc.Chunk(data);
        Assert.Equal(60, lengths.Count);
        Assert.Equal("302F9666750AB68CCA5C6BD933584BC27AF6DA333A08D08687BD2F0569B0844E", LengthsSha256(lengths));

        // every boundary beyond the insertion (+ one max chunk of resync) is a vector-1 boundary shifted by 1000
        var baseBoundaries = new HashSet<long>();
        long acc = 0;
        foreach (var len in baseLengths) baseBoundaries.Add(acc += len);
        acc = 0;
        int checkedBoundaries = 0;
        foreach (var len in lengths) {
            acc += len;
            if (acc > insertAt + FastCdc.MaxSize + insert.Length) {
                Assert.Contains(acc - insert.Length, baseBoundaries);
                checkedBoundaries++;
            }
        }

        Assert.True(checkedBoundaries > 40);
    }

    [Fact]
    public void Vector3ZerosForcedCuts()
    {
        var lengths = FastCdc.Chunk(new byte[1024 * 1024]);
        Assert.Equal([262144, 262144, 262144, 262144], lengths);
    }

    [Fact]
    public void EdgeSizes()
    {
        Assert.Empty(FastCdc.Chunk(Array.Empty<byte>()));
        Assert.Equal([16384], FastCdc.Chunk(ChunkTestTree.RandomBytes(3, 16384)));
        Assert.Equal([16385], FastCdc.Chunk(ChunkTestTree.RandomBytes(3, 16385)));
        Assert.Equal([1], FastCdc.Chunk(new byte[1]));
    }

    private sealed class ChoppyStream : Stream
    {
        private readonly byte[] _data;
        private readonly Func<int> _nextSize;
        private int _pos;

        public ChoppyStream(byte[] data, Func<int> nextSize)
        {
            _data = data;
            _nextSize = nextSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, _nextSize()), _data.Length - _pos);
            Buffer.BlockCopy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(0)]
    public void BoundariesIndependentOfFeedSize(int feedSize)
    {
        var data = ChunkTestTree.RandomBytes(11, 2 * 1024 * 1024 + 12345);
        var expected = FastCdc.Chunk(data);

        var rng = new Random(42);
        Func<int> next = feedSize > 0 ? () => feedSize : () => rng.Next(1, 300_000);
        var got = new List<int>();
        var ms = new MemoryStream();
        long total = FastCdc.ChunkStream(
            new ChoppyStream(data, next),
            (buf, off, len) => {
                got.Add(len);
                ms.Write(buf, off, len);
            });

        Assert.Equal(data.Length, total);
        Assert.Equal(expected, got);
        Assert.Equal(data, ms.ToArray());
    }

    [Fact]
    public void CdcVectorsFixtureMatches()
    {
        // test/fixtures/delta-v2/cdc_vectors.json is shared with the other languages; the pack-time chunker must agree with it
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(PathHelper.GetFixture("delta-v2"), "cdc_vectors.json")))!;
        Assert.Equal(GearTable.Seed.ToString("X16"), (string) json["gear"]!["splitmix64_seed"]);
        Assert.Equal(FastCdc.MinSize, (int) json["params"]!["min"]);
        Assert.Equal(FastCdc.AvgSize, (int) json["params"]!["avg"]);
        Assert.Equal(FastCdc.MaxSize, (int) json["params"]!["max"]);
        Assert.Equal(FastCdc.MaskS.ToString("X16"), (string) json["params"]!["mask_s"]);
        Assert.Equal(FastCdc.MaskL.ToString("X16"), (string) json["params"]!["mask_l"]);

        var vectors = json["vectors"]!.AsArray();
        var byName = new Dictionary<string, byte[]>();
        foreach (var vector in vectors) {
            string name = (string) vector!["name"]!;
            int size = (int) vector["size"]!;
            byte[] data;
            if (vector["base"] != null) {
                var baseData = byName[(string) vector["base"]!];
                var insert = ChunkTestTree.RandomBytes((ulong) vector["insert_seed"]!, (int) vector["insert_length"]!);
                int at = (int) vector["insert_offset"]!;
                data = baseData.Take(at).Concat(insert).Concat(baseData.Skip(at)).ToArray();
            } else if (vector["seed"] != null) {
                data = ChunkTestTree.RandomBytes((ulong) vector["seed"]!, size);
            } else {
                data = new byte[size];
            }

            Assert.Equal(size, data.Length);
            byName[name] = data;
            if (vector["data_sha256"] != null) {
                Assert.Equal((string) vector["data_sha256"], Convert.ToHexString(SHA256.HashData(data)));
            }

            var expected = vector["lengths"]!.AsArray().Select(n => (int) n!).ToList();
            Assert.Equal(expected, FastCdc.Chunk(data));
        }

        Assert.Contains("vector1", byName.Keys);
        Assert.Contains("vector2", byName.Keys);
    }

    [Fact]
    public void ChunkLengthsWithinBounds()
    {
        foreach (ulong seed in new ulong[] { 5, 6, 7 }) {
            var data = ChunkTestTree.RandomBytes(seed, 3 * 1024 * 1024 + 777);
            var lengths = FastCdc.Chunk(data);
            Assert.Equal(data.Length, lengths.Sum());
            for (int i = 0; i < lengths.Count; i++) {
                Assert.True(lengths[i] <= FastCdc.MaxSize, $"chunk {i} is {lengths[i]} bytes");
                if (i < lengths.Count - 1) {
                    Assert.True(lengths[i] > FastCdc.MinSize, $"chunk {i} is {lengths[i]} bytes");
                }
            }
        }
    }
}
