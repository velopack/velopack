using System;
using System.IO;
using System.IO.Compression;

namespace Squirrel.Compression
{
    internal sealed class BZip2Stream : Stream
    {
        private readonly Stream stream;
        private bool isDisposed;

        /// <summary>
        /// Create a BZip2Stream
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="compressionMode">Compression Mode</param>
        /// <param name="decompressConcatenated">Decompress Concatenated</param>
        public BZip2Stream(Stream stream, CompressionMode compressionMode, bool decompressConcatenated)
        {
            Mode = compressionMode;
            if (Mode == CompressionMode.Compress) {
                this.stream = new CBZip2OutputStream(stream);
            } else {
                this.stream = new CBZip2InputStream(stream, decompressConcatenated);
            }
        }

        public void Finish() => (stream as CBZip2OutputStream)?.Finish();

        protected override void Dispose(bool disposing)
        {
            if (isDisposed) {
                return;
            }
            isDisposed = true;
            if (disposing) {
                stream.Dispose();
            }
        }

        public CompressionMode Mode { get; }

        public override bool CanRead => stream.CanRead;

        public override bool CanSeek => stream.CanSeek;

        public override bool CanWrite => stream.CanWrite;

        public override void Flush() => stream.Flush();

        public override long Length => stream.Length;

        public override long Position {
            get => stream.Position;
            set => stream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            stream.Read(buffer, offset, count);

        public override int ReadByte() => stream.ReadByte();

        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

        public override void SetLength(long value) => stream.SetLength(value);

#if !NETFRAMEWORK && !NETSTANDARD2_0

    public override int Read(Span<byte> buffer) => stream.Read(buffer);

    public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(buffer);
#endif

        public override void Write(byte[] buffer, int offset, int count) =>
            stream.Write(buffer, offset, count);

        public override void WriteByte(byte value) => stream.WriteByte(value);

        /// <summary>
        /// Consumes two bytes to test if there is a BZip2 header
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static bool IsBZip2(Stream stream)
        {
            var br = new BinaryReader(stream);
            var chars = br.ReadBytes(2);
            if (chars.Length < 2 || chars[0] != 'B' || chars[1] != 'Z') {
                return false;
            }
            return true;
        }
    }


    /*
     * Copyright 2001,2004-2005 The Apache Software Foundation
     *
     * Licensed under the Apache License, Version 2.0 (the "License");
     * you may not use this file except in compliance with the License.
     * You may obtain a copy of the License at
     *
     * http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    /*
     * This package is based on the work done by Keiron Liddle, Aftex Software
     * <keiron@aftexsw.com> to whom the Ant project is very grateful for his
     * great code.
     */

    /**
      * An input stream that decompresses from the BZip2 format (with the file
      * header chars) to be read as any other stream.
      *
      * @author <a href="mailto:keiron@aftexsw.com">Keiron Liddle</a>
      *
      * <b>NB:</b> note this class has been modified to read the leading BZ from the
      * start of the BZIP2 stream to make it compatible with other PGP programs.
      */

    internal class CBZip2InputStream : Stream
    {
        private static void Cadvise()
        {
            //System.out.Println("CRC Error");
            //throw new CCoruptionError();
        }

        private static void BadBGLengths() => Cadvise();

        private static void BitStreamEOF() => Cadvise();

        private static void CompressedStreamEOF() => Cadvise();

        private void MakeMaps()
        {
            int i;
            nInUse = 0;
            for (i = 0; i < 256; i++) {
                if (inUse[i]) {
                    seqToUnseq[nInUse] = (char) i;
                    unseqToSeq[i] = (char) nInUse;
                    nInUse++;
                }
            }
        }

        /*
        index of the last char in the block, so
        the block size == last + 1.
        */
        private int last;

        /*
        index in zptr[] of original string after sorting.
        */
        private int origPtr;

        /*
        always: in the range 0 .. 9.
        The current block size is 100000 * this number.
        */
        private int blockSize100k;

        private bool blockRandomised;

        private int bsBuff;
        private int bsLive;
        private readonly CRC mCrc = new CRC();

        private readonly bool[] inUse = new bool[256];
        private int nInUse;

        private readonly char[] seqToUnseq = new char[256];
        private readonly char[] unseqToSeq = new char[256];

        private readonly char[] selector = new char[BZip2Constants.MAX_SELECTORS];
        private readonly char[] selectorMtf = new char[BZip2Constants.MAX_SELECTORS];

        private int[] tt;
        private char[] ll8;

        /*
        freq table collected to save a pass over the data
        during decompression.
        */
        private readonly int[] unzftab = new int[256];

        private readonly int[][] limit = InitIntArray(
            BZip2Constants.N_GROUPS,
            BZip2Constants.MAX_ALPHA_SIZE
        );
        private readonly int[][] basev = InitIntArray(
            BZip2Constants.N_GROUPS,
            BZip2Constants.MAX_ALPHA_SIZE
        );
        private readonly int[][] perm = InitIntArray(
            BZip2Constants.N_GROUPS,
            BZip2Constants.MAX_ALPHA_SIZE
        );
        private readonly int[] minLens = new int[BZip2Constants.N_GROUPS];

        private Stream bsStream;

        private bool streamEnd;

        private int currentChar = -1;

        private const int START_BLOCK_STATE = 1;
        private const int RAND_PART_A_STATE = 2;
        private const int RAND_PART_B_STATE = 3;
        private const int RAND_PART_C_STATE = 4;
        private const int NO_RAND_PART_A_STATE = 5;
        private const int NO_RAND_PART_B_STATE = 6;
        private const int NO_RAND_PART_C_STATE = 7;

        private int currentState = START_BLOCK_STATE;

        private int storedBlockCRC,
            storedCombinedCRC;
        private int computedBlockCRC,
            computedCombinedCRC;
        private readonly bool decompressConcatenated;

        private int i2,
            count,
            chPrev,
            ch2;
        private int i,
            tPos;
        private int rNToGo;
        private int rTPos;
        private int j2;
        private char z;
        private bool isDisposed;

        public CBZip2InputStream(Stream zStream, bool decompressConcatenated)
        {
            this.decompressConcatenated = decompressConcatenated;
            ll8 = null;
            tt = null;
            BsSetStream(zStream);
            Initialize(true);
            InitBlock();
            SetupBlock();
        }

        protected override void Dispose(bool disposing)
        {
            if (isDisposed) {
                return;
            }
            isDisposed = true;
            base.Dispose(disposing);
            bsStream?.Dispose();
        }

        internal static int[][] InitIntArray(int n1, int n2)
        {
            var a = new int[n1][];
            for (var k = 0; k < n1; ++k) {
                a[k] = new int[n2];
            }
            return a;
        }

        internal static char[][] InitCharArray(int n1, int n2)
        {
            var a = new char[n1][];
            for (var k = 0; k < n1; ++k) {
                a[k] = new char[n2];
            }
            return a;
        }

        public override int ReadByte()
        {
            if (streamEnd) {
                return -1;
            }
            var retChar = currentChar;
            switch (currentState) {
            case START_BLOCK_STATE:
                break;
            case RAND_PART_A_STATE:
                break;
            case RAND_PART_B_STATE:
                SetupRandPartB();
                break;
            case RAND_PART_C_STATE:
                SetupRandPartC();
                break;
            case NO_RAND_PART_A_STATE:
                break;
            case NO_RAND_PART_B_STATE:
                SetupNoRandPartB();
                break;
            case NO_RAND_PART_C_STATE:
                SetupNoRandPartC();
                break;
            default:
                break;
            }
            return retChar;
        }

        private bool Initialize(bool isFirstStream)
        {
            var magic0 = bsStream.ReadByte();
            var magic1 = bsStream.ReadByte();
            var magic2 = bsStream.ReadByte();
            if (magic0 == -1 && !isFirstStream) {
                return false;
            }
            if (magic0 != 'B' || magic1 != 'Z' || magic2 != 'h') {
                throw new IOException("Not a BZIP2 marked stream");
            }
            var magic3 = bsStream.ReadByte();
            if (magic3 < '1' || magic3 > '9') {
                BsFinishedWithStream();
                streamEnd = true;
                return false;
            }

            SetDecompressStructureSizes(magic3 - '0');
            bsLive = 0;
            computedCombinedCRC = 0;
            return true;
        }

        private void InitBlock()
        {
            char magic1,
                magic2,
                magic3,
                magic4;
            char magic5,
                magic6;

            while (true) {
                magic1 = BsGetUChar();
                magic2 = BsGetUChar();
                magic3 = BsGetUChar();
                magic4 = BsGetUChar();
                magic5 = BsGetUChar();
                magic6 = BsGetUChar();
                if (
                    magic1 != 0x17
                    || magic2 != 0x72
                    || magic3 != 0x45
                    || magic4 != 0x38
                    || magic5 != 0x50
                    || magic6 != 0x90
                ) {
                    break;
                }

                if (Complete()) {
                    return;
                }
            }

            if (
                magic1 != 0x31
                || magic2 != 0x41
                || magic3 != 0x59
                || magic4 != 0x26
                || magic5 != 0x53
                || magic6 != 0x59
            ) {
                BadBlockHeader();
                streamEnd = true;
                return;
            }

            storedBlockCRC = BsGetInt32();

            if (BsR(1) == 1) {
                blockRandomised = true;
            } else {
                blockRandomised = false;
            }

            //        currBlockNo++;
            GetAndMoveToFrontDecode();

            mCrc.InitialiseCRC();
            currentState = START_BLOCK_STATE;
        }

        private void EndBlock()
        {
            computedBlockCRC = mCrc.GetFinalCRC();
            /* A bad CRC is considered a fatal error. */
            if (storedBlockCRC != computedBlockCRC) {
                CrcError();
            }

            computedCombinedCRC = (computedCombinedCRC << 1) | (int) (((uint) computedCombinedCRC) >> 31);
            computedCombinedCRC ^= computedBlockCRC;
        }

        private bool Complete()
        {
            storedCombinedCRC = BsGetInt32();
            if (storedCombinedCRC != computedCombinedCRC) {
                CrcError();
            }

            var complete = !decompressConcatenated || !Initialize(false);
            if (complete) {
                BsFinishedWithStream();
                streamEnd = true;
            }

            // Look for the next .bz2 stream if decompressing
            // concatenated files.
            return complete;
        }

        private static void BlockOverrun() => Cadvise();

        private static void BadBlockHeader() => Cadvise();

        private static void CrcError() => Cadvise();

        private void BsFinishedWithStream()
        {
            bsStream?.Dispose();
            bsStream = null;
        }

        private void BsSetStream(Stream f)
        {
            bsStream = f;
            bsLive = 0;
            bsBuff = 0;
        }

        private int BsR(int n)
        {
            int v;
            while (bsLive < n) {
                int zzi;
                int thech = '\0';
                try {
                    thech = (char) bsStream.ReadByte();
                } catch (IOException) {
                    CompressedStreamEOF();
                }
                if (thech == '\uffff') {
                    CompressedStreamEOF();
                }
                zzi = thech;
                bsBuff = (bsBuff << 8) | (zzi & 0xff);
                bsLive += 8;
            }

            v = (bsBuff >> (bsLive - n)) & ((1 << n) - 1);
            bsLive -= n;
            return v;
        }

        private char BsGetUChar() => (char) BsR(8);

        private int BsGetint()
        {
            var u = 0;
            u = (u << 8) | BsR(8);
            u = (u << 8) | BsR(8);
            u = (u << 8) | BsR(8);
            u = (u << 8) | BsR(8);
            return u;
        }

        private int BsGetIntVS(int numBits) => BsR(numBits);

        private int BsGetInt32() => BsGetint();

        private void HbCreateDecodeTables(
            int[] limit,
            int[] basev,
            int[] perm,
            char[] length,
            int minLen,
            int maxLen,
            int alphaSize
        )
        {
            int pp,
                i,
                j,
                vec;

            pp = 0;
            for (i = minLen; i <= maxLen; i++) {
                for (j = 0; j < alphaSize; j++) {
                    if (length[j] == i) {
                        perm[pp] = j;
                        pp++;
                    }
                }
            }

            for (i = 0; i < BZip2Constants.MAX_CODE_LEN; i++) {
                basev[i] = 0;
            }
            for (i = 0; i < alphaSize; i++) {
                basev[length[i] + 1]++;
            }

            for (i = 1; i < BZip2Constants.MAX_CODE_LEN; i++) {
                basev[i] += basev[i - 1];
            }

            for (i = 0; i < BZip2Constants.MAX_CODE_LEN; i++) {
                limit[i] = 0;
            }
            vec = 0;

            for (i = minLen; i <= maxLen; i++) {
                vec += (basev[i + 1] - basev[i]);
                limit[i] = vec - 1;
                vec <<= 1;
            }
            for (i = minLen + 1; i <= maxLen; i++) {
                basev[i] = ((limit[i - 1] + 1) << 1) - basev[i];
            }
        }

        private void RecvDecodingTables()
        {
            var len = InitCharArray(BZip2Constants.N_GROUPS, BZip2Constants.MAX_ALPHA_SIZE);
            int i,
                j,
                t,
                nGroups,
                nSelectors,
                alphaSize;
            int minLen,
                maxLen;
            var inUse16 = new bool[16];

            /* Receive the mapping table */
            for (i = 0; i < 16; i++) {
                if (BsR(1) == 1) {
                    inUse16[i] = true;
                } else {
                    inUse16[i] = false;
                }
            }

            for (i = 0; i < 256; i++) {
                inUse[i] = false;
            }

            for (i = 0; i < 16; i++) {
                if (inUse16[i]) {
                    for (j = 0; j < 16; j++) {
                        if (BsR(1) == 1) {
                            inUse[(i * 16) + j] = true;
                        }
                    }
                }
            }

            MakeMaps();
            alphaSize = nInUse + 2;

            /* Now the selectors */
            nGroups = BsR(3);
            nSelectors = BsR(15);
            for (i = 0; i < nSelectors; i++) {
                j = 0;
                while (BsR(1) == 1) {
                    j++;
                }
                selectorMtf[i] = (char) j;
            }

            /* Undo the MTF values for the selectors. */
            {
                var pos = new char[BZip2Constants.N_GROUPS];
                char tmp,
                    v;
                for (v = '\0'; v < nGroups; v++) {
                    pos[v] = v;
                }

                for (i = 0; i < nSelectors; i++) {
                    v = selectorMtf[i];
                    tmp = pos[v];
                    while (v > 0) {
                        pos[v] = pos[v - 1];
                        v--;
                    }
                    pos[0] = tmp;
                    selector[i] = tmp;
                }
            }

            /* Now the coding tables */
            for (t = 0; t < nGroups; t++) {
                var curr = BsR(5);
                for (i = 0; i < alphaSize; i++) {
                    while (BsR(1) == 1) {
                        if (BsR(1) == 0) {
                            curr++;
                        } else {
                            curr--;
                        }
                    }
                    len[t][i] = (char) curr;
                }
            }

            /* Create the Huffman decoding tables */
            for (t = 0; t < nGroups; t++) {
                minLen = 32;
                maxLen = 0;
                for (i = 0; i < alphaSize; i++) {
                    if (len[t][i] > maxLen) {
                        maxLen = len[t][i];
                    }
                    if (len[t][i] < minLen) {
                        minLen = len[t][i];
                    }
                }
                HbCreateDecodeTables(limit[t], basev[t], perm[t], len[t], minLen, maxLen, alphaSize);
                minLens[t] = minLen;
            }
        }

        private void GetAndMoveToFrontDecode()
        {
            var yy = new char[256];
            int i,
                j,
                nextSym,
                limitLast;
            int EOB,
                groupNo,
                groupPos;

            limitLast = BZip2Constants.baseBlockSize * blockSize100k;
            origPtr = BsGetIntVS(24);

            RecvDecodingTables();
            EOB = nInUse + 1;
            groupNo = -1;
            groupPos = 0;

            /*
            Setting up the unzftab entries here is not strictly
            necessary, but it does save having to do it later
            in a separate pass, and so saves a block's worth of
            cache misses.
            */
            for (i = 0; i <= 255; i++) {
                unzftab[i] = 0;
            }

            for (i = 0; i <= 255; i++) {
                yy[i] = (char) i;
            }

            last = -1;

            {
                int zt,
                    zn,
                    zvec,
                    zj;
                if (groupPos == 0) {
                    groupNo++;
                    groupPos = BZip2Constants.G_SIZE;
                }
                groupPos--;
                zt = selector[groupNo];
                zn = minLens[zt];
                zvec = BsR(zn);
                while (zvec > limit[zt][zn]) {
                    zn++;
                    {
                        {
                            while (bsLive < 1) {
                                int zzi;
                                var thech = '\0';
                                try {
                                    thech = (char) bsStream.ReadByte();
                                } catch (IOException) {
                                    CompressedStreamEOF();
                                }
                                if (thech == '\uffff') {
                                    CompressedStreamEOF();
                                }
                                zzi = thech;
                                bsBuff = (bsBuff << 8) | (zzi & 0xff);
                                bsLive += 8;
                            }
                        }
                        zj = (bsBuff >> (bsLive - 1)) & 1;
                        bsLive--;
                    }
                    zvec = (zvec << 1) | zj;
                }
                nextSym = perm[zt][zvec - basev[zt][zn]];
            }

            while (true) {
                if (nextSym == EOB) {
                    break;
                }

                if (nextSym == BZip2Constants.RUNA || nextSym == BZip2Constants.RUNB) {
                    char ch;
                    var s = -1;
                    var N = 1;
                    do {
                        if (nextSym == BZip2Constants.RUNA) {
                            s += (0 + 1) * N;
                        } else if (nextSym == BZip2Constants.RUNB) {
                            s += (1 + 1) * N;
                        }
                        N *= 2;
                        {
                            int zt,
                                zn,
                                zvec,
                                zj;
                            if (groupPos == 0) {
                                groupNo++;
                                groupPos = BZip2Constants.G_SIZE;
                            }
                            groupPos--;
                            zt = selector[groupNo];
                            zn = minLens[zt];
                            zvec = BsR(zn);
                            while (zvec > limit[zt][zn]) {
                                zn++;
                                {
                                    {
                                        while (bsLive < 1) {
                                            int zzi;
                                            var thech = '\0';
                                            try {
                                                thech = (char) bsStream.ReadByte();
                                            } catch (IOException) {
                                                CompressedStreamEOF();
                                            }
                                            if (thech == '\uffff') {
                                                CompressedStreamEOF();
                                            }
                                            zzi = thech;
                                            bsBuff = (bsBuff << 8) | (zzi & 0xff);
                                            bsLive += 8;
                                        }
                                    }
                                    zj = (bsBuff >> (bsLive - 1)) & 1;
                                    bsLive--;
                                }
                                zvec = (zvec << 1) | zj;
                            }
                            nextSym = perm[zt][zvec - basev[zt][zn]];
                        }
                    } while (nextSym == BZip2Constants.RUNA || nextSym == BZip2Constants.RUNB);

                    s++;
                    ch = seqToUnseq[yy[0]];
                    unzftab[ch] += s;

                    while (s > 0) {
                        last++;
                        ll8[last] = ch;
                        s--;
                    }

                    if (last >= limitLast) {
                        BlockOverrun();
                    }
                } else {
                    char tmp;
                    last++;
                    if (last >= limitLast) {
                        BlockOverrun();
                    }

                    tmp = yy[nextSym - 1];
                    unzftab[seqToUnseq[tmp]]++;
                    ll8[last] = seqToUnseq[tmp];

                    /*
                    This loop is hammered during decompression,
                    hence the unrolling.

                    for (j = nextSym-1; j > 0; j--) yy[j] = yy[j-1];
                    */

                    j = nextSym - 1;
                    for (; j > 3; j -= 4) {
                        yy[j] = yy[j - 1];
                        yy[j - 1] = yy[j - 2];
                        yy[j - 2] = yy[j - 3];
                        yy[j - 3] = yy[j - 4];
                    }
                    for (; j > 0; j--) {
                        yy[j] = yy[j - 1];
                    }

                    yy[0] = tmp;
                    {
                        int zt,
                            zn,
                            zvec,
                            zj;
                        if (groupPos == 0) {
                            groupNo++;
                            groupPos = BZip2Constants.G_SIZE;
                        }
                        groupPos--;
                        zt = selector[groupNo];
                        zn = minLens[zt];
                        zvec = BsR(zn);
                        while (zvec > limit[zt][zn]) {
                            zn++;
                            {
                                {
                                    while (bsLive < 1) {
                                        int zzi;
                                        var thech = '\0';
                                        try {
                                            thech = (char) bsStream.ReadByte();
                                        } catch (IOException) {
                                            CompressedStreamEOF();
                                        }
                                        zzi = thech;
                                        bsBuff = (bsBuff << 8) | (zzi & 0xff);
                                        bsLive += 8;
                                    }
                                }
                                zj = (bsBuff >> (bsLive - 1)) & 1;
                                bsLive--;
                            }
                            zvec = (zvec << 1) | zj;
                        }
                        nextSym = perm[zt][zvec - basev[zt][zn]];
                    }
                }
            }
        }

        private void SetupBlock()
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            Span<int> cftab = stackalloc int[257];
#else
            int[] cftab = new int[257];
#endif

            char ch;

            cftab[0] = 0;
            for (i = 1; i <= 256; i++) {
                cftab[i] = unzftab[i - 1];
            }
            for (i = 1; i <= 256; i++) {
                cftab[i] += cftab[i - 1];
            }

            for (i = 0; i <= last; i++) {
                ch = ll8[i];
                tt[cftab[ch]] = i;
                cftab[ch]++;
            }

            tPos = tt[origPtr];

            count = 0;
            i2 = 0;
            ch2 = 256; /* not a char and not EOF */

            if (blockRandomised) {
                rNToGo = 0;
                rTPos = 0;
                SetupRandPartA();
            } else {
                SetupNoRandPartA();
            }
        }

        private void SetupRandPartA()
        {
            if (i2 <= last) {
                chPrev = ch2;
                ch2 = ll8[tPos];
                tPos = tt[tPos];
                if (rNToGo == 0) {
                    rNToGo = BZip2Constants.rNums[rTPos];
                    rTPos++;
                    if (rTPos == 512) {
                        rTPos = 0;
                    }
                }
                rNToGo--;
                ch2 ^= (rNToGo == 1) ? 1 : 0;
                i2++;

                currentChar = ch2;
                currentState = RAND_PART_B_STATE;
                mCrc.UpdateCRC(ch2);
            } else {
                EndBlock();
                InitBlock();
                SetupBlock();
            }
        }

        private void SetupNoRandPartA()
        {
            if (i2 <= last) {
                chPrev = ch2;
                ch2 = ll8[tPos];
                tPos = tt[tPos];
                i2++;

                currentChar = ch2;
                currentState = NO_RAND_PART_B_STATE;
                mCrc.UpdateCRC(ch2);
            } else {
                EndBlock();
                InitBlock();
                SetupBlock();
            }
        }

        private void SetupRandPartB()
        {
            if (ch2 != chPrev) {
                currentState = RAND_PART_A_STATE;
                count = 1;
                SetupRandPartA();
            } else {
                count++;
                if (count >= 4) {
                    z = ll8[tPos];
                    tPos = tt[tPos];
                    if (rNToGo == 0) {
                        rNToGo = BZip2Constants.rNums[rTPos];
                        rTPos++;
                        if (rTPos == 512) {
                            rTPos = 0;
                        }
                    }
                    rNToGo--;
                    z ^= (char) ((rNToGo == 1) ? 1 : 0);
                    j2 = 0;
                    currentState = RAND_PART_C_STATE;
                    SetupRandPartC();
                } else {
                    currentState = RAND_PART_A_STATE;
                    SetupRandPartA();
                }
            }
        }

        private void SetupRandPartC()
        {
            if (j2 < z) {
                currentChar = ch2;
                mCrc.UpdateCRC(ch2);
                j2++;
            } else {
                currentState = RAND_PART_A_STATE;
                i2++;
                count = 0;
                SetupRandPartA();
            }
        }

        private void SetupNoRandPartB()
        {
            if (ch2 != chPrev) {
                currentState = NO_RAND_PART_A_STATE;
                count = 1;
                SetupNoRandPartA();
            } else {
                count++;
                if (count >= 4) {
                    z = ll8[tPos];
                    tPos = tt[tPos];
                    currentState = NO_RAND_PART_C_STATE;
                    j2 = 0;
                    SetupNoRandPartC();
                } else {
                    currentState = NO_RAND_PART_A_STATE;
                    SetupNoRandPartA();
                }
            }
        }

        private void SetupNoRandPartC()
        {
            if (j2 < z) {
                currentChar = ch2;
                mCrc.UpdateCRC(ch2);
                j2++;
            } else {
                currentState = NO_RAND_PART_A_STATE;
                i2++;
                count = 0;
                SetupNoRandPartA();
            }
        }

        private void SetDecompressStructureSizes(int newSize100k)
        {
            if (!(0 <= newSize100k && newSize100k <= 9 && 0 <= blockSize100k && blockSize100k <= 9)) {
                // throw new IOException("Invalid block size");
            }

            blockSize100k = newSize100k;

            if (newSize100k == 0) {
                return;
            }

            var n = BZip2Constants.baseBlockSize * newSize100k;
            ll8 = new char[n];
            tt = new int[n];
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var c = -1;
            int k;
            for (k = 0; k < count; ++k) {
                c = ReadByte();
                if (c == -1) {
                    break;
                }
                buffer[k + offset] = (byte) c;
            }
            return k;
        }

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value) { }

        public override void Write(byte[] buffer, int offset, int count) { }

        public override void WriteByte(byte value) { }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position {
            get => 0;
            set { }
        }
    }

    /**
  * An output stream that compresses into the BZip2 format (with the file
  * header chars) into another stream.
  *
  * @author <a href="mailto:keiron@aftexsw.com">Keiron Liddle</a>
  *
  * TODO:    Update to BZip2 1.0.1
  * <b>NB:</b> note this class has been modified to add a leading BZ to the
  * start of the BZIP2 stream to make it compatible with other PGP programs.
  */

    internal sealed class CBZip2OutputStream : Stream
    {
        private const int SETMASK = (1 << 21);
        private const int CLEARMASK = (~SETMASK);
        private const int GREATER_ICOST = 15;
        private const int LESSER_ICOST = 0;
        private const int SMALL_THRESH = 20;
        private const int DEPTH_THRESH = 10;

        /*
        If you are ever unlucky/improbable enough
        to get a stack overflow whilst sorting,
        increase the following constant and try
        again.  In practice I have never seen the
        stack go above 27 elems, so the following
        limit seems very generous.
        */
        private const int QSORT_STACK_SIZE = 1000;
        private bool finished;

        private static void Panic()
        {
            //System.out.Println("panic");
            //throw new CError();
        }

        private void MakeMaps()
        {
            int i;
            nInUse = 0;
            for (i = 0; i < 256; i++) {
                if (inUse[i]) {
                    seqToUnseq[nInUse] = (char) i;
                    unseqToSeq[i] = (char) nInUse;
                    nInUse++;
                }
            }
        }

        private static void HbMakeCodeLengths(char[] len, int[] freq, int alphaSize, int maxLen)
        {
            /*
            Nodes and heap entries run from 1.  Entry 0
            for both the heap and nodes is a sentinel.
            */
            int nNodes,
                nHeap,
                n1,
                n2,
                i,
                j,
                k;
            bool tooLong;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            Span<int> heap = stackalloc int[BZip2Constants.MAX_ALPHA_SIZE + 2]; // 1040 bytes
            Span<int> weight = stackalloc int[BZip2Constants.MAX_ALPHA_SIZE * 2]; // 1040 bytes
            Span<int> parent = stackalloc int[BZip2Constants.MAX_ALPHA_SIZE * 2]; // 1040 bytes
#else
            int[] heap = new int[BZip2Constants.MAX_ALPHA_SIZE + 2];
            int[] weight = new int[BZip2Constants.MAX_ALPHA_SIZE * 2];
            int[] parent = new int[BZip2Constants.MAX_ALPHA_SIZE * 2];
#endif

            for (i = 0; i < alphaSize; i++) {
                weight[i + 1] = (freq[i] == 0 ? 1 : freq[i]) << 8;
            }

            while (true) {
                nNodes = alphaSize;
                nHeap = 0;

                heap[0] = 0;
                weight[0] = 0;
                parent[0] = -2;

                for (i = 1; i <= alphaSize; i++) {
                    parent[i] = -1;
                    nHeap++;
                    heap[nHeap] = i;
                    {
                        int zz,
                            tmp;
                        zz = nHeap;
                        tmp = heap[zz];
                        while (weight[tmp] < weight[heap[zz >> 1]]) {
                            heap[zz] = heap[zz >> 1];
                            zz >>= 1;
                        }
                        heap[zz] = tmp;
                    }
                }
                if (!(nHeap < (BZip2Constants.MAX_ALPHA_SIZE + 2))) {
                    Panic();
                }

                while (nHeap > 1) {
                    n1 = heap[1];
                    heap[1] = heap[nHeap];
                    nHeap--;
                    {
                        int zz = 0,
                            yy = 0,
                            tmp = 0;
                        zz = 1;
                        tmp = heap[zz];
                        while (true) {
                            yy = zz << 1;
                            if (yy > nHeap) {
                                break;
                            }
                            if (yy < nHeap && weight[heap[yy + 1]] < weight[heap[yy]]) {
                                yy++;
                            }
                            if (weight[tmp] < weight[heap[yy]]) {
                                break;
                            }
                            heap[zz] = heap[yy];
                            zz = yy;
                        }
                        heap[zz] = tmp;
                    }
                    n2 = heap[1];
                    heap[1] = heap[nHeap];
                    nHeap--;
                    {
                        int zz = 0,
                            yy = 0,
                            tmp = 0;
                        zz = 1;
                        tmp = heap[zz];
                        while (true) {
                            yy = zz << 1;
                            if (yy > nHeap) {
                                break;
                            }
                            if (yy < nHeap && weight[heap[yy + 1]] < weight[heap[yy]]) {
                                yy++;
                            }
                            if (weight[tmp] < weight[heap[yy]]) {
                                break;
                            }
                            heap[zz] = heap[yy];
                            zz = yy;
                        }
                        heap[zz] = tmp;
                    }
                    nNodes++;
                    parent[n1] = parent[n2] = nNodes;

                    weight[nNodes] = (int) (
                        (uint) ((weight[n1] & 0xffffff00) + (weight[n2] & 0xffffff00))
                        | (uint) (
                            1
                            + (
                                ((weight[n1] & 0x000000ff) > (weight[n2] & 0x000000ff))
                                    ? (weight[n1] & 0x000000ff)
                                    : (weight[n2] & 0x000000ff)
                            )
                        )
                    );

                    parent[nNodes] = -1;
                    nHeap++;
                    heap[nHeap] = nNodes;
                    {
                        int zz = 0,
                            tmp = 0;
                        zz = nHeap;
                        tmp = heap[zz];
                        while (weight[tmp] < weight[heap[zz >> 1]]) {
                            heap[zz] = heap[zz >> 1];
                            zz >>= 1;
                        }
                        heap[zz] = tmp;
                    }
                }
                if (!(nNodes < (BZip2Constants.MAX_ALPHA_SIZE * 2))) {
                    Panic();
                }

                tooLong = false;
                for (i = 1; i <= alphaSize; i++) {
                    j = 0;
                    k = i;
                    while (parent[k] >= 0) {
                        k = parent[k];
                        j++;
                    }
                    len[i - 1] = (char) j;
                    if (j > maxLen) {
                        tooLong = true;
                    }
                }

                if (!tooLong) {
                    break;
                }

                for (i = 1; i < alphaSize; i++) {
                    j = weight[i] >> 8;
                    j = 1 + (j / 2);
                    weight[i] = j << 8;
                }
            }
        }

        /*
        index of the last char in the block, so
        the block size == last + 1.
        */
        private int last;

        /*
        index in zptr[] of original string after sorting.
        */
        private int origPtr;

        /*
        always: in the range 0 .. 9.
        The current block size is 100000 * this number.
        */
        private readonly int blockSize100k;

        private bool blockRandomised;

        private int bytesOut;
        private int bsBuff;
        private int bsLive;
        private readonly CRC mCrc = new CRC();

        private readonly bool[] inUse = new bool[256];
        private int nInUse;

        private readonly char[] seqToUnseq = new char[256];
        private readonly char[] unseqToSeq = new char[256];

        private readonly char[] selector = new char[BZip2Constants.MAX_SELECTORS];
        private readonly char[] selectorMtf = new char[BZip2Constants.MAX_SELECTORS];

        private char[] block;
        private int[] quadrant;
        private int[] zptr;
        private short[] szptr;
        private int[] ftab;

        private int nMTF;

        private readonly int[] mtfFreq = new int[BZip2Constants.MAX_ALPHA_SIZE];

        /*
        * Used when sorting.  If too many long comparisons
        * happen, we stop sorting, randomise the block
        * slightly, and try again.
        */
        private readonly int workFactor;
        private int workDone;
        private int workLimit;
        private bool firstAttempt;
        private int nBlocksRandomised;

        private int currentChar = -1;
        private int runLength;

        public CBZip2OutputStream(Stream inStream)
            : this(inStream, 9) { }

        public CBZip2OutputStream(Stream inStream, int inBlockSize)
        {
            block = null;
            quadrant = null;
            zptr = null;
            ftab = null;

            inStream.WriteByte((byte) 'B');
            inStream.WriteByte((byte) 'Z');

            BsSetStream(inStream);

            workFactor = 50;
            if (inBlockSize > 9) {
                inBlockSize = 9;
            }
            if (inBlockSize < 1) {
                inBlockSize = 1;
            }
            blockSize100k = inBlockSize;
            AllocateCompressStructures();
            Initialize();
            InitBlock();
        }

        /**
        *
        * modified by Oliver Merkel, 010128
        *
        */

        public override void WriteByte(byte bv)
        {
            var b = (256 + bv) % 256;
            if (currentChar != -1) {
                if (currentChar == b) {
                    runLength++;
                    if (runLength > 254) {
                        WriteRun();
                        currentChar = -1;
                        runLength = 0;
                    }
                } else {
                    WriteRun();
                    runLength = 1;
                    currentChar = b;
                }
            } else {
                currentChar = b;
                runLength++;
            }
        }

        private void WriteRun()
        {
            if (last < allowableBlockSize) {
                inUse[currentChar] = true;
                for (var i = 0; i < runLength; i++) {
                    mCrc.UpdateCRC((char) currentChar);
                }
                switch (runLength) {
                case 1:
                    last++;
                    block[last + 1] = (char) currentChar;
                    break;
                case 2:
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) currentChar;
                    break;
                case 3:
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) currentChar;
                    break;
                default:
                    inUse[runLength - 4] = true;
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) currentChar;
                    last++;
                    block[last + 1] = (char) (runLength - 4);
                    break;
                }
            } else {
                EndBlock();
                InitBlock();
                WriteRun();
            }
        }

        private bool disposed;

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                if (disposed) {
                    return;
                }

                Finish();

                disposed = true;
                Dispose();
                bsStream?.Dispose();
                bsStream = null;
            }
        }

        public void Finish()
        {
            if (finished) {
                return;
            }

            if (runLength > 0) {
                WriteRun();
            }
            currentChar = -1;
            EndBlock();
            EndCompression();
            finished = true;
            Flush();
        }

        public override void Flush() => bsStream.Flush();

        private int blockCRC,
            combinedCRC;

        private void Initialize()
        {
            bytesOut = 0;
            nBlocksRandomised = 0;

            /* Write `magic' bytes h indicating file-format == huffmanised,
            followed by a digit indicating blockSize100k.
            */
            BsPutUChar('h');
            BsPutUChar('0' + blockSize100k);

            combinedCRC = 0;
        }

        private int allowableBlockSize;

        private void InitBlock()
        {
            //        blockNo++;
            mCrc.InitialiseCRC();
            last = -1;

            //        ch = 0;

            for (var i = 0; i < 256; i++) {
                inUse[i] = false;
            }

            /* 20 is just a paranoia constant */
            allowableBlockSize = (BZip2Constants.baseBlockSize * blockSize100k) - 20;
        }

        private void EndBlock()
        {
            blockCRC = mCrc.GetFinalCRC();
            combinedCRC = (combinedCRC << 1) | (int) (((uint) combinedCRC) >> 31);
            combinedCRC ^= blockCRC;

            /* sort the block and establish posn of original string */
            DoReversibleTransformation();

            /*
            A 6-byte block header, the value chosen arbitrarily
            as 0x314159265359 :-).  A 32 bit value does not really
            give a strong enough guarantee that the value will not
            appear by chance in the compressed datastream.  Worst-case
            probability of this event, for a 900k block, is about
            2.0e-3 for 32 bits, 1.0e-5 for 40 bits and 4.0e-8 for 48 bits.
            For a compressed file of size 100Gb -- about 100000 blocks --
            only a 48-bit marker will do.  NB: normal compression/
            decompression do *not* rely on these statistical properties.
            They are only important when trying to recover blocks from
            damaged files.
            */
            BsPutUChar(0x31);
            BsPutUChar(0x41);
            BsPutUChar(0x59);
            BsPutUChar(0x26);
            BsPutUChar(0x53);
            BsPutUChar(0x59);

            /* Now the block's CRC, so it is in a known place. */
            BsPutint(blockCRC);

            /* Now a single bit indicating randomisation. */
            if (blockRandomised) {
                BsW(1, 1);
                nBlocksRandomised++;
            } else {
                BsW(1, 0);
            }

            /* Finally, block's contents proper. */
            MoveToFrontCodeAndSend();
        }

        private void EndCompression()
        {
            /*
            Now another magic 48-bit number, 0x177245385090, to
            indicate the end of the last block.  (Sqrt(pi), if
            you want to know.  I did want to use e, but it contains
            too much repetition -- 27 18 28 18 28 46 -- for me
            to feel statistically comfortable.  Call me paranoid.)
            */
            BsPutUChar(0x17);
            BsPutUChar(0x72);
            BsPutUChar(0x45);
            BsPutUChar(0x38);
            BsPutUChar(0x50);
            BsPutUChar(0x90);

            BsPutint(combinedCRC);

            BsFinishedWithStream();
        }

        private void HbAssignCodes(int[] code, char[] length, int minLen, int maxLen, int alphaSize)
        {
            int n,
                vec,
                i;

            vec = 0;
            for (n = minLen; n <= maxLen; n++) {
                for (i = 0; i < alphaSize; i++) {
                    if (length[i] == n) {
                        code[i] = vec;
                        vec++;
                    }
                }
                ;
                vec <<= 1;
            }
        }

        private void BsSetStream(Stream f)
        {
            bsStream = f;
            bsLive = 0;
            bsBuff = 0;
            bytesOut = 0;
        }

        private void BsFinishedWithStream()
        {
            while (bsLive > 0) {
                var ch = (bsBuff >> 24);
                bsStream.WriteByte((byte) ch); // write 8-bit
                bsBuff <<= 8;
                bsLive -= 8;
                bytesOut++;
            }
        }

        private void BsW(int n, int v)
        {
            while (bsLive >= 8) {
                var ch = (bsBuff >> 24);
                bsStream.WriteByte((byte) ch); // write 8-bit
                bsBuff <<= 8;
                bsLive -= 8;
                bytesOut++;
            }
            bsBuff |= (v << (32 - bsLive - n));
            bsLive += n;
        }

        private void BsPutUChar(int c) => BsW(8, c);

        private void BsPutint(int u)
        {
            BsW(8, (u >> 24) & 0xff);
            BsW(8, (u >> 16) & 0xff);
            BsW(8, (u >> 8) & 0xff);
            BsW(8, u & 0xff);
        }

        private void BsPutIntVS(int numBits, int c) => BsW(numBits, c);

        private void SendMTFValues()
        {
            var len = CBZip2InputStream.InitCharArray(
                BZip2Constants.N_GROUPS,
                BZip2Constants.MAX_ALPHA_SIZE
            );

            int v,
                t,
                i,
                j,
                gs,
                ge,
                totc,
                bt,
                bc,
                iter;
            int nSelectors = 0,
                alphaSize,
                minLen,
                maxLen,
                selCtr;
            int nGroups; //, nBytes;

            alphaSize = nInUse + 2;
            for (t = 0; t < BZip2Constants.N_GROUPS; t++) {
                for (v = 0; v < alphaSize; v++) {
                    len[t][v] = (char) GREATER_ICOST;
                }
            }

            /* Decide how many coding tables to use */
            if (nMTF <= 0) {
                Panic();
            }

            if (nMTF < 200) {
                nGroups = 2;
            } else if (nMTF < 600) {
                nGroups = 3;
            } else if (nMTF < 1200) {
                nGroups = 4;
            } else if (nMTF < 2400) {
                nGroups = 5;
            } else {
                nGroups = 6;
            }

            /* Generate an initial set of coding tables */
            {
                int nPart,
                    remF,
                    tFreq,
                    aFreq;

                nPart = nGroups;
                remF = nMTF;
                gs = 0;
                while (nPart > 0) {
                    tFreq = remF / nPart;
                    ge = gs - 1;
                    aFreq = 0;
                    while (aFreq < tFreq && ge < alphaSize - 1) {
                        ge++;
                        aFreq += mtfFreq[ge];
                    }

                    if (ge > gs && nPart != nGroups && nPart != 1 && ((nGroups - nPart) % 2 == 1)) {
                        aFreq -= mtfFreq[ge];
                        ge--;
                    }

                    for (v = 0; v < alphaSize; v++) {
                        if (v >= gs && v <= ge) {
                            len[nPart - 1][v] = (char) LESSER_ICOST;
                        } else {
                            len[nPart - 1][v] = (char) GREATER_ICOST;
                        }
                    }

                    nPart--;
                    gs = ge + 1;
                    remF -= aFreq;
                }
            }

            var rfreq = CBZip2InputStream.InitIntArray(
                BZip2Constants.N_GROUPS,
                BZip2Constants.MAX_ALPHA_SIZE
            );
            var fave = new int[BZip2Constants.N_GROUPS];
            var cost = new short[BZip2Constants.N_GROUPS];
            /*
            Iterate up to N_ITERS times to improve the tables.
            */
            for (iter = 0; iter < BZip2Constants.N_ITERS; iter++) {
                for (t = 0; t < nGroups; t++) {
                    fave[t] = 0;
                }

                for (t = 0; t < nGroups; t++) {
                    for (v = 0; v < alphaSize; v++) {
                        rfreq[t][v] = 0;
                    }
                }

                nSelectors = 0;
                totc = 0;
                gs = 0;
                while (true) {
                    /* Set group start & end marks. */
                    if (gs >= nMTF) {
                        break;
                    }
                    ge = gs + BZip2Constants.G_SIZE - 1;
                    if (ge >= nMTF) {
                        ge = nMTF - 1;
                    }

                    /*
                    Calculate the cost of this group as coded
                    by each of the coding tables.
                    */
                    for (t = 0; t < nGroups; t++) {
                        cost[t] = 0;
                    }

                    if (nGroups == 6) {
                        short cost0,
                            cost1,
                            cost2,
                            cost3,
                            cost4,
                            cost5;
                        cost0 = cost1 = cost2 = cost3 = cost4 = cost5 = 0;
                        for (i = gs; i <= ge; i++) {
                            var icv = szptr[i];
                            cost0 += (short) len[0][icv];
                            cost1 += (short) len[1][icv];
                            cost2 += (short) len[2][icv];
                            cost3 += (short) len[3][icv];
                            cost4 += (short) len[4][icv];
                            cost5 += (short) len[5][icv];
                        }
                        cost[0] = cost0;
                        cost[1] = cost1;
                        cost[2] = cost2;
                        cost[3] = cost3;
                        cost[4] = cost4;
                        cost[5] = cost5;
                    } else {
                        for (i = gs; i <= ge; i++) {
                            var icv = szptr[i];
                            for (t = 0; t < nGroups; t++) {
                                cost[t] += (short) len[t][icv];
                            }
                        }
                    }

                    /*
                    Find the coding table which is best for this group,
                    and record its identity in the selector table.
                    */
                    bc = 999999999;
                    bt = -1;
                    for (t = 0; t < nGroups; t++) {
                        if (cost[t] < bc) {
                            bc = cost[t];
                            bt = t;
                        }
                    }
                    ;
                    totc += bc;
                    fave[bt]++;
                    selector[nSelectors] = (char) bt;
                    nSelectors++;

                    /*
                    Increment the symbol frequencies for the selected table.
                    */
                    for (i = gs; i <= ge; i++) {
                        rfreq[bt][szptr[i]]++;
                    }

                    gs = ge + 1;
                }

                /*
                Recompute the tables based on the accumulated frequencies.
                */
                for (t = 0; t < nGroups; t++) {
                    HbMakeCodeLengths(len[t], rfreq[t], alphaSize, 20);
                }
            }

            rfreq = null;
            fave = null;
            cost = null;

            if (!(nGroups < 8)) {
                Panic();
            }
            if (!(nSelectors < 32768 && nSelectors <= (2 + (900000 / BZip2Constants.G_SIZE)))) {
                Panic();
            }

            /* Compute MTF values for the selectors. */
            {
                var pos = new char[BZip2Constants.N_GROUPS];
                char ll_i,
                    tmp2,
                    tmp;
                for (i = 0; i < nGroups; i++) {
                    pos[i] = (char) i;
                }
                for (i = 0; i < nSelectors; i++) {
                    ll_i = selector[i];
                    j = 0;
                    tmp = pos[j];
                    while (ll_i != tmp) {
                        j++;
                        tmp2 = tmp;
                        tmp = pos[j];
                        pos[j] = tmp2;
                    }
                    pos[0] = tmp;
                    selectorMtf[i] = (char) j;
                }
            }

            var code = CBZip2InputStream.InitIntArray(
                BZip2Constants.N_GROUPS,
                BZip2Constants.MAX_ALPHA_SIZE
            );

            /* Assign actual codes for the tables. */
            for (t = 0; t < nGroups; t++) {
                minLen = 32;
                maxLen = 0;
                for (i = 0; i < alphaSize; i++) {
                    if (len[t][i] > maxLen) {
                        maxLen = len[t][i];
                    }
                    if (len[t][i] < minLen) {
                        minLen = len[t][i];
                    }
                }
                if (maxLen > 20) {
                    Panic();
                }
                if (minLen < 1) {
                    Panic();
                }
                HbAssignCodes(code[t], len[t], minLen, maxLen, alphaSize);
            }

            /* Transmit the mapping table. */
            {
                var inUse16 = new bool[16];
                for (i = 0; i < 16; i++) {
                    inUse16[i] = false;
                    for (j = 0; j < 16; j++) {
                        if (inUse[(i * 16) + j]) {
                            inUse16[i] = true;
                        }
                    }
                }

                //nBytes = bytesOut;
                for (i = 0; i < 16; i++) {
                    if (inUse16[i]) {
                        BsW(1, 1);
                    } else {
                        BsW(1, 0);
                    }
                }

                for (i = 0; i < 16; i++) {
                    if (inUse16[i]) {
                        for (j = 0; j < 16; j++) {
                            if (inUse[(i * 16) + j]) {
                                BsW(1, 1);
                            } else {
                                BsW(1, 0);
                            }
                        }
                    }
                }
            }

            /* Now the selectors. */
            //nBytes = bytesOut;
            BsW(3, nGroups);
            BsW(15, nSelectors);
            for (i = 0; i < nSelectors; i++) {
                for (j = 0; j < selectorMtf[i]; j++) {
                    BsW(1, 1);
                }
                BsW(1, 0);
            }

            /* Now the coding tables. */
            //nBytes = bytesOut;

            for (t = 0; t < nGroups; t++) {
                int curr = len[t][0];
                BsW(5, curr);
                for (i = 0; i < alphaSize; i++) {
                    while (curr < len[t][i]) {
                        BsW(2, 2);
                        curr++; /* 10 */
                    }
                    while (curr > len[t][i]) {
                        BsW(2, 3);
                        curr--; /* 11 */
                    }
                    BsW(1, 0);
                }
            }

            /* And finally, the block data proper */
            //nBytes = bytesOut;
            selCtr = 0;
            gs = 0;
            while (true) {
                if (gs >= nMTF) {
                    break;
                }
                ge = gs + BZip2Constants.G_SIZE - 1;
                if (ge >= nMTF) {
                    ge = nMTF - 1;
                }
                for (i = gs; i <= ge; i++) {
                    BsW(len[selector[selCtr]][szptr[i]], code[selector[selCtr]][szptr[i]]);
                }

                gs = ge + 1;
                selCtr++;
            }
            if (!(selCtr == nSelectors)) {
                Panic();
            }
        }

        private void MoveToFrontCodeAndSend()
        {
            BsPutIntVS(24, origPtr);
            GenerateMTFValues();
            SendMTFValues();
        }

        private Stream bsStream;

        private void SimpleSort(int lo, int hi, int d)
        {
            int i,
                j,
                h,
                bigN,
                hp;
            int v;

            bigN = hi - lo + 1;
            if (bigN < 2) {
                return;
            }

            hp = 0;
            while (incs[hp] < bigN) {
                hp++;
            }
            hp--;

            for (; hp >= 0; hp--) {
                h = incs[hp];

                i = lo + h;
                while (true) {
                    /* copy 1 */
                    if (i > hi) {
                        break;
                    }
                    v = zptr[i];
                    j = i;
                    while (FullGtU(zptr[j - h] + d, v + d)) {
                        zptr[j] = zptr[j - h];
                        j -= h;
                        if (j <= (lo + h - 1)) {
                            break;
                        }
                    }
                    zptr[j] = v;
                    i++;

                    /* copy 2 */
                    if (i > hi) {
                        break;
                    }
                    v = zptr[i];
                    j = i;
                    while (FullGtU(zptr[j - h] + d, v + d)) {
                        zptr[j] = zptr[j - h];
                        j -= h;
                        if (j <= (lo + h - 1)) {
                            break;
                        }
                    }
                    zptr[j] = v;
                    i++;

                    /* copy 3 */
                    if (i > hi) {
                        break;
                    }
                    v = zptr[i];
                    j = i;
                    while (FullGtU(zptr[j - h] + d, v + d)) {
                        zptr[j] = zptr[j - h];
                        j -= h;
                        if (j <= (lo + h - 1)) {
                            break;
                        }
                    }
                    zptr[j] = v;
                    i++;

                    if (workDone > workLimit && firstAttempt) {
                        return;
                    }
                }
            }
        }

        private void Vswap(int p1, int p2, int n)
        {
            var temp = 0;
            while (n > 0) {
                temp = zptr[p1];
                zptr[p1] = zptr[p2];
                zptr[p2] = temp;
                p1++;
                p2++;
                n--;
            }
        }

        private char Med3(char a, char b, char c)
        {
            char t;
            if (a > b) {
                t = a;
                a = b;
                b = t;
            }
            if (b > c) {
                t = b;
                b = c;
                c = t;
            }
            if (a > b) {
                b = a;
            }
            return b;
        }

        internal class StackElem
        {
            internal int ll;
            internal int hh;
            internal int dd;
        }

        private void QSort3(int loSt, int hiSt, int dSt)
        {
            int unLo,
                unHi,
                ltLo,
                gtHi,
                med,
                n,
                m;
            int sp,
                lo,
                hi,
                d;
            var stack = new StackElem[QSORT_STACK_SIZE];
            for (var count = 0; count < QSORT_STACK_SIZE; count++) {
                stack[count] = new StackElem();
            }

            sp = 0;

            stack[sp].ll = loSt;
            stack[sp].hh = hiSt;
            stack[sp].dd = dSt;
            sp++;

            while (sp > 0) {
                if (sp >= QSORT_STACK_SIZE) {
                    Panic();
                }

                sp--;
                lo = stack[sp].ll;
                hi = stack[sp].hh;
                d = stack[sp].dd;

                if (hi - lo < SMALL_THRESH || d > DEPTH_THRESH) {
                    SimpleSort(lo, hi, d);
                    if (workDone > workLimit && firstAttempt) {
                        return;
                    }
                    continue;
                }

                med = Med3(
                    block[zptr[lo] + d + 1],
                    block[zptr[hi] + d + 1],
                    block[zptr[(lo + hi) >> 1] + d + 1]
                );

                unLo = ltLo = lo;
                unHi = gtHi = hi;

                while (true) {
                    while (true) {
                        if (unLo > unHi) {
                            break;
                        }
                        n = block[zptr[unLo] + d + 1] - med;
                        if (n == 0) {
                            var temp = 0;
                            temp = zptr[unLo];
                            zptr[unLo] = zptr[ltLo];
                            zptr[ltLo] = temp;
                            ltLo++;
                            unLo++;
                            continue;
                        }
                        ;
                        if (n > 0) {
                            break;
                        }
                        unLo++;
                    }
                    while (true) {
                        if (unLo > unHi) {
                            break;
                        }
                        n = block[zptr[unHi] + d + 1] - med;
                        if (n == 0) {
                            var temp = 0;
                            temp = zptr[unHi];
                            zptr[unHi] = zptr[gtHi];
                            zptr[gtHi] = temp;
                            gtHi--;
                            unHi--;
                            continue;
                        }
                        ;
                        if (n < 0) {
                            break;
                        }
                        unHi--;
                    }
                    if (unLo > unHi) {
                        break;
                    }
                    var tempx = zptr[unLo];
                    zptr[unLo] = zptr[unHi];
                    zptr[unHi] = tempx;
                    unLo++;
                    unHi--;
                }

                if (gtHi < ltLo) {
                    stack[sp].ll = lo;
                    stack[sp].hh = hi;
                    stack[sp].dd = d + 1;
                    sp++;
                    continue;
                }

                n = ((ltLo - lo) < (unLo - ltLo)) ? (ltLo - lo) : (unLo - ltLo);
                Vswap(lo, unLo - n, n);
                m = ((hi - gtHi) < (gtHi - unHi)) ? (hi - gtHi) : (gtHi - unHi);
                Vswap(unLo, hi - m + 1, m);

                n = lo + unLo - ltLo - 1;
                m = hi - (gtHi - unHi) + 1;

                stack[sp].ll = lo;
                stack[sp].hh = n;
                stack[sp].dd = d;
                sp++;

                stack[sp].ll = n + 1;
                stack[sp].hh = m - 1;
                stack[sp].dd = d + 1;
                sp++;

                stack[sp].ll = m;
                stack[sp].hh = hi;
                stack[sp].dd = d;
                sp++;
            }
        }

        private void MainSort()
        {
            int i,
                j,
                ss,
                sb;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            Span<int> runningOrder = stackalloc int[256];
            Span<int> copy = stackalloc int[256];
#else
            int[] runningOrder = new int[256];
            int[] copy = new int[256];
#endif
            var bigDone = new bool[256];
            int c1,
                c2;
            int numQSorted;

            /*
            In the various block-sized structures, live data runs
            from 0 to last+NUM_OVERSHOOT_BYTES inclusive.  First,
            set up the overshoot area for block.
            */

            //   if (verbosity >= 4) fprintf ( stderr, "   sort initialise ...\n" );
            for (i = 0; i < BZip2Constants.NUM_OVERSHOOT_BYTES; i++) {
                block[last + i + 2] = block[(i % (last + 1)) + 1];
            }
            for (i = 0; i <= last + BZip2Constants.NUM_OVERSHOOT_BYTES; i++) {
                quadrant[i] = 0;
            }

            block[0] = block[last + 1];

            if (last < 4000) {
                /*
                Use SimpleSort(), since the full sorting mechanism
                has quite a large constant overhead.
                */
                for (i = 0; i <= last; i++) {
                    zptr[i] = i;
                }
                firstAttempt = false;
                workDone = workLimit = 0;
                SimpleSort(0, last, 0);
            } else {
                numQSorted = 0;
                for (i = 0; i <= 255; i++) {
                    bigDone[i] = false;
                }

                for (i = 0; i <= 65536; i++) {
                    ftab[i] = 0;
                }

                c1 = block[0];
                for (i = 0; i <= last; i++) {
                    c2 = block[i + 1];
                    ftab[(c1 << 8) + c2]++;
                    c1 = c2;
                }

                for (i = 1; i <= 65536; i++) {
                    ftab[i] += ftab[i - 1];
                }

                c1 = block[1];
                for (i = 0; i < last; i++) {
                    c2 = block[i + 2];
                    j = (c1 << 8) + c2;
                    c1 = c2;
                    ftab[j]--;
                    zptr[ftab[j]] = i;
                }

                j = ((block[last + 1]) << 8) + (block[1]);
                ftab[j]--;
                zptr[ftab[j]] = last;

                /*
                Now ftab contains the first loc of every small bucket.
                Calculate the running order, from smallest to largest
                big bucket.
                */

                for (i = 0; i <= 255; i++) {
                    runningOrder[i] = i;
                }

                {
                    int vv;
                    var h = 1;
                    do {
                        h = (3 * h) + 1;
                    } while (h <= 256);
                    do {
                        h /= 3;
                        for (i = h; i <= 255; i++) {
                            vv = runningOrder[i];
                            j = i;
                            while (
                                (
                                    ftab[((runningOrder[j - h]) + 1) << 8]
                                    - ftab[(runningOrder[j - h]) << 8]
                                ) > (ftab[((vv) + 1) << 8] - ftab[(vv) << 8])
                            ) {
                                runningOrder[j] = runningOrder[j - h];
                                j -= h;
                                if (j <= (h - 1)) {
                                    break;
                                }
                            }
                            runningOrder[j] = vv;
                        }
                    } while (h != 1);
                }

                /*
                The main sorting loop.
                */
                for (i = 0; i <= 255; i++) {
                    /*
                    Process big buckets, starting with the least full.
                    */
                    ss = runningOrder[i];

                    /*
                    Complete the big bucket [ss] by quicksorting
                    any unsorted small buckets [ss, j].  Hopefully
                    previous pointer-scanning phases have already
                    completed many of the small buckets [ss, j], so
                    we don't have to sort them at all.
                    */
                    for (j = 0; j <= 255; j++) {
                        sb = (ss << 8) + j;
                        if (!((ftab[sb] & SETMASK) == SETMASK)) {
                            var lo = ftab[sb] & CLEARMASK;
                            var hi = (ftab[sb + 1] & CLEARMASK) - 1;
                            if (hi > lo) {
                                QSort3(lo, hi, 2);
                                numQSorted += (hi - lo + 1);
                                if (workDone > workLimit && firstAttempt) {
                                    return;
                                }
                            }
                            ftab[sb] |= SETMASK;
                        }
                    }

                    /*
                    The ss big bucket is now done.  Record this fact,
                    and update the quadrant descriptors.  Remember to
                    update quadrants in the overshoot area too, if
                    necessary.  The "if (i < 255)" test merely skips
                    this updating for the last bucket processed, since
                    updating for the last bucket is pointless.
                    */
                    bigDone[ss] = true;

                    if (i < 255) {
                        var bbStart = ftab[ss << 8] & CLEARMASK;
                        var bbSize = (ftab[(ss + 1) << 8] & CLEARMASK) - bbStart;
                        var shifts = 0;

                        while ((bbSize >> shifts) > 65534) {
                            shifts++;
                        }

                        for (j = 0; j < bbSize; j++) {
                            var a2update = zptr[bbStart + j];
                            var qVal = (j >> shifts);
                            quadrant[a2update] = qVal;
                            if (a2update < BZip2Constants.NUM_OVERSHOOT_BYTES) {
                                quadrant[a2update + last + 1] = qVal;
                            }
                        }

                        if (!(((bbSize - 1) >> shifts) <= 65535)) {
                            Panic();
                        }
                    }

                    /*
                    Now scan this big bucket so as to synthesise the
                    sorted order for small buckets [t, ss] for all t != ss.
                    */
                    for (j = 0; j <= 255; j++) {
                        copy[j] = ftab[(j << 8) + ss] & CLEARMASK;
                    }

                    for (j = ftab[ss << 8] & CLEARMASK; j < (ftab[(ss + 1) << 8] & CLEARMASK); j++) {
                        c1 = block[zptr[j]];
                        if (!bigDone[c1]) {
                            zptr[copy[c1]] = zptr[j] == 0 ? last : zptr[j] - 1;
                            copy[c1]++;
                        }
                    }

                    for (j = 0; j <= 255; j++) {
                        ftab[(j << 8) + ss] |= SETMASK;
                    }
                }
            }
        }

        private void RandomiseBlock()
        {
            int i;
            var rNToGo = 0;
            var rTPos = 0;
            for (i = 0; i < 256; i++) {
                inUse[i] = false;
            }

            for (i = 0; i <= last; i++) {
                if (rNToGo == 0) {
                    rNToGo = (char) BZip2Constants.rNums[rTPos];
                    rTPos++;
                    if (rTPos == 512) {
                        rTPos = 0;
                    }
                }
                rNToGo--;
                block[i + 1] ^= (char) ((rNToGo == 1) ? 1 : 0);

                // handle 16 bit signed numbers
                block[i + 1] &= (char) 0xFF;

                inUse[block[i + 1]] = true;
            }
        }

        private void DoReversibleTransformation()
        {
            int i;

            workLimit = workFactor * last;
            workDone = 0;
            blockRandomised = false;
            firstAttempt = true;

            MainSort();

            if (workDone > workLimit && firstAttempt) {
                RandomiseBlock();
                workLimit = workDone = 0;
                blockRandomised = true;
                firstAttempt = false;
                MainSort();
            }

            origPtr = -1;
            for (i = 0; i <= last; i++) {
                if (zptr[i] == 0) {
                    origPtr = i;
                    break;
                }
            }
            ;

            if (origPtr == -1) {
                Panic();
            }
        }

        private bool FullGtU(int i1, int i2)
        {
            int k;
            char c1,
                c2;
            int s1,
                s2;

            c1 = block[i1 + 1];
            c2 = block[i2 + 1];
            if (c1 != c2) {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = block[i1 + 1];
            c2 = block[i2 + 1];
            if (c1 != c2) {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = block[i1 + 1];
            c2 = block[i2 + 1];
            if (c1 != c2) {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = block[i1 + 1];
            c2 = block[i2 + 1];
            if (c1 != c2) {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = block[i1 + 1];
            c2 = block[i2 + 1];
            if (c1 != c2) {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = block[i1 + 1];
            c2 = block[i2 + 1];
            if (c1 != c2) {
                return (c1 > c2);
            }
            i1++;
            i2++;

            k = last + 1;

            do {
                c1 = block[i1 + 1];
                c2 = block[i2 + 1];
                if (c1 != c2) {
                    return (c1 > c2);
                }
                s1 = quadrant[i1];
                s2 = quadrant[i2];
                if (s1 != s2) {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                c1 = block[i1 + 1];
                c2 = block[i2 + 1];
                if (c1 != c2) {
                    return (c1 > c2);
                }
                s1 = quadrant[i1];
                s2 = quadrant[i2];
                if (s1 != s2) {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                c1 = block[i1 + 1];
                c2 = block[i2 + 1];
                if (c1 != c2) {
                    return (c1 > c2);
                }
                s1 = quadrant[i1];
                s2 = quadrant[i2];
                if (s1 != s2) {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                c1 = block[i1 + 1];
                c2 = block[i2 + 1];
                if (c1 != c2) {
                    return (c1 > c2);
                }
                s1 = quadrant[i1];
                s2 = quadrant[i2];
                if (s1 != s2) {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                if (i1 > last) {
                    i1 -= last;
                    i1--;
                }
                ;
                if (i2 > last) {
                    i2 -= last;
                    i2--;
                }
                ;

                k -= 4;
                workDone++;
            } while (k >= 0);

            return false;
        }

        /*
        Knuth's increments seem to work better
        than Incerpi-Sedgewick here.  Possibly
        because the number of elems to sort is
        usually small, typically <= 20.
        */

        private readonly int[] incs =
        {
        1,
        4,
        13,
        40,
        121,
        364,
        1093,
        3280,
        9841,
        29524,
        88573,
        265720,
        797161,
        2391484
    };

        private void AllocateCompressStructures()
        {
            var n = BZip2Constants.baseBlockSize * blockSize100k;
            block = new char[(n + 1 + BZip2Constants.NUM_OVERSHOOT_BYTES)];
            quadrant = new int[(n + BZip2Constants.NUM_OVERSHOOT_BYTES)];
            zptr = new int[n];
            ftab = new int[65537];

            if (block is null || quadrant is null || zptr is null || ftab is null) {
                //int totalDraw = (n + 1 + NUM_OVERSHOOT_BYTES) + (n + NUM_OVERSHOOT_BYTES) + n + 65537;
                //compressOutOfMemory ( totalDraw, n );
            }

            /*
            The back end needs a place to store the MTF values
            whilst it calculates the coding tables.  We could
            put them in the zptr array.  However, these values
            will fit in a short, so we overlay szptr at the
            start of zptr, in the hope of reducing the number
            of cache misses induced by the multiple traversals
            of the MTF values when calculating coding tables.
            Seems to improve compression speed by about 1%.
            */
            //    szptr = zptr;

            szptr = new short[2 * n];
        }

        private void GenerateMTFValues()
        {
            var yy = new char[256];
            int i,
                j;
            char tmp;
            char tmp2;
            int zPend;
            int wr;
            int EOB;

            MakeMaps();
            EOB = nInUse + 1;

            for (i = 0; i <= EOB; i++) {
                mtfFreq[i] = 0;
            }

            wr = 0;
            zPend = 0;
            for (i = 0; i < nInUse; i++) {
                yy[i] = (char) i;
            }

            for (i = 0; i <= last; i++) {
                char ll_i;

                ll_i = unseqToSeq[block[zptr[i]]];

                j = 0;
                tmp = yy[j];
                while (ll_i != tmp) {
                    j++;
                    tmp2 = tmp;
                    tmp = yy[j];
                    yy[j] = tmp2;
                }
                ;
                yy[0] = tmp;

                if (j == 0) {
                    zPend++;
                } else {
                    if (zPend > 0) {
                        zPend--;
                        while (true) {
                            switch (zPend % 2) {
                            case 0:
                                szptr[wr] = BZip2Constants.RUNA;
                                wr++;
                                mtfFreq[BZip2Constants.RUNA]++;
                                break;
                            case 1:
                                szptr[wr] = BZip2Constants.RUNB;
                                wr++;
                                mtfFreq[BZip2Constants.RUNB]++;
                                break;
                            }
                            ;
                            if (zPend < 2) {
                                break;
                            }
                            zPend = (zPend - 2) / 2;
                        }
                        ;
                        zPend = 0;
                    }
                    szptr[wr] = (short) (j + 1);
                    wr++;
                    mtfFreq[j + 1]++;
                }
            }

            if (zPend > 0) {
                zPend--;
                while (true) {
                    switch (zPend % 2) {
                    case 0:
                        szptr[wr] = BZip2Constants.RUNA;
                        wr++;
                        mtfFreq[BZip2Constants.RUNA]++;
                        break;
                    case 1:
                        szptr[wr] = BZip2Constants.RUNB;
                        wr++;
                        mtfFreq[BZip2Constants.RUNB]++;
                        break;
                    }
                    if (zPend < 2) {
                        break;
                    }
                    zPend = (zPend - 2) / 2;
                }
            }

            szptr[wr] = (short) EOB;
            wr++;
            mtfFreq[EOB]++;

            nMTF = wr;
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override int ReadByte() => -1;

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value) { }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (var k = 0; k < count; ++k) {
                WriteByte(buffer[k + offset]);
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position {
            get => 0;
            set { }
        }
    }

    /**
* Base class for both the compress and decompress classes.
* Holds common arrays, and static data.
*
* @author <a href="mailto:keiron@aftexsw.com">Keiron Liddle</a>
*/

    internal class BZip2Constants
    {
        public const int baseBlockSize = 100000;
        public const int MAX_ALPHA_SIZE = 258;
        public const int MAX_CODE_LEN = 23;
        public const int RUNA = 0;
        public const int RUNB = 1;
        public const int N_GROUPS = 6;
        public const int G_SIZE = 50;
        public const int N_ITERS = 4;
        public const int MAX_SELECTORS = (2 + (900000 / G_SIZE));
        public const int NUM_OVERSHOOT_BYTES = 20;

        public static int[] rNums =
        {
            619,
            720,
            127,
            481,
            931,
            816,
            813,
            233,
            566,
            247,
            985,
            724,
            205,
            454,
            863,
            491,
            741,
            242,
            949,
            214,
            733,
            859,
            335,
            708,
            621,
            574,
            73,
            654,
            730,
            472,
            419,
            436,
            278,
            496,
            867,
            210,
            399,
            680,
            480,
            51,
            878,
            465,
            811,
            169,
            869,
            675,
            611,
            697,
            867,
            561,
            862,
            687,
            507,
            283,
            482,
            129,
            807,
            591,
            733,
            623,
            150,
            238,
            59,
            379,
            684,
            877,
            625,
            169,
            643,
            105,
            170,
            607,
            520,
            932,
            727,
            476,
            693,
            425,
            174,
            647,
            73,
            122,
            335,
            530,
            442,
            853,
            695,
            249,
            445,
            515,
            909,
            545,
            703,
            919,
            874,
            474,
            882,
            500,
            594,
            612,
            641,
            801,
            220,
            162,
            819,
            984,
            589,
            513,
            495,
            799,
            161,
            604,
            958,
            533,
            221,
            400,
            386,
            867,
            600,
            782,
            382,
            596,
            414,
            171,
            516,
            375,
            682,
            485,
            911,
            276,
            98,
            553,
            163,
            354,
            666,
            933,
            424,
            341,
            533,
            870,
            227,
            730,
            475,
            186,
            263,
            647,
            537,
            686,
            600,
            224,
            469,
            68,
            770,
            919,
            190,
            373,
            294,
            822,
            808,
            206,
            184,
            943,
            795,
            384,
            383,
            461,
            404,
            758,
            839,
            887,
            715,
            67,
            618,
            276,
            204,
            918,
            873,
            777,
            604,
            560,
            951,
            160,
            578,
            722,
            79,
            804,
            96,
            409,
            713,
            940,
            652,
            934,
            970,
            447,
            318,
            353,
            859,
            672,
            112,
            785,
            645,
            863,
            803,
            350,
            139,
            93,
            354,
            99,
            820,
            908,
            609,
            772,
            154,
            274,
            580,
            184,
            79,
            626,
            630,
            742,
            653,
            282,
            762,
            623,
            680,
            81,
            927,
            626,
            789,
            125,
            411,
            521,
            938,
            300,
            821,
            78,
            343,
            175,
            128,
            250,
            170,
            774,
            972,
            275,
            999,
            639,
            495,
            78,
            352,
            126,
            857,
            956,
            358,
            619,
            580,
            124,
            737,
            594,
            701,
            612,
            669,
            112,
            134,
            694,
            363,
            992,
            809,
            743,
            168,
            974,
            944,
            375,
            748,
            52,
            600,
            747,
            642,
            182,
            862,
            81,
            344,
            805,
            988,
            739,
            511,
            655,
            814,
            334,
            249,
            515,
            897,
            955,
            664,
            981,
            649,
            113,
            974,
            459,
            893,
            228,
            433,
            837,
            553,
            268,
            926,
            240,
            102,
            654,
            459,
            51,
            686,
            754,
            806,
            760,
            493,
            403,
            415,
            394,
            687,
            700,
            946,
            670,
            656,
            610,
            738,
            392,
            760,
            799,
            887,
            653,
            978,
            321,
            576,
            617,
            626,
            502,
            894,
            679,
            243,
            440,
            680,
            879,
            194,
            572,
            640,
            724,
            926,
            56,
            204,
            700,
            707,
            151,
            457,
            449,
            797,
            195,
            791,
            558,
            945,
            679,
            297,
            59,
            87,
            824,
            713,
            663,
            412,
            693,
            342,
            606,
            134,
            108,
            571,
            364,
            631,
            212,
            174,
            643,
            304,
            329,
            343,
            97,
            430,
            751,
            497,
            314,
            983,
            374,
            822,
            928,
            140,
            206,
            73,
            263,
            980,
            736,
            876,
            478,
            430,
            305,
            170,
            514,
            364,
            692,
            829,
            82,
            855,
            953,
            676,
            246,
            369,
            970,
            294,
            750,
            807,
            827,
            150,
            790,
            288,
            923,
            804,
            378,
            215,
            828,
            592,
            281,
            565,
            555,
            710,
            82,
            896,
            831,
            547,
            261,
            524,
            462,
            293,
            465,
            502,
            56,
            661,
            821,
            976,
            991,
            658,
            869,
            905,
            758,
            745,
            193,
            768,
            550,
            608,
            933,
            378,
            286,
            215,
            979,
            792,
            961,
            61,
            688,
            793,
            644,
            986,
            403,
            106,
            366,
            905,
            644,
            372,
            567,
            466,
            434,
            645,
            210,
            389,
            550,
            919,
            135,
            780,
            773,
            635,
            389,
            707,
            100,
            626,
            958,
            165,
            504,
            920,
            176,
            193,
            713,
            857,
            265,
            203,
            50,
            668,
            108,
            645,
            990,
            626,
            197,
            510,
            357,
            358,
            850,
            858,
            364,
            936,
            638
        };
    }

    /**
  * A simple class the hold and calculate the CRC for sanity checking
  * of the data.
  *
  * @author <a href="mailto:keiron@aftexsw.com">Keiron Liddle</a>
  */

    internal class CRC
    {
        public static int[] crc32Table =
        {
            0x00000000,
            0x04c11db7,
            0x09823b6e,
            0x0d4326d9,
            0x130476dc,
            0x17c56b6b,
            0x1a864db2,
            0x1e475005,
            0x2608edb8,
            0x22c9f00f,
            0x2f8ad6d6,
            0x2b4bcb61,
            0x350c9b64,
            0x31cd86d3,
            0x3c8ea00a,
            0x384fbdbd,
            0x4c11db70,
            0x48d0c6c7,
            0x4593e01e,
            0x4152fda9,
            0x5f15adac,
            0x5bd4b01b,
            0x569796c2,
            0x52568b75,
            0x6a1936c8,
            0x6ed82b7f,
            0x639b0da6,
            0x675a1011,
            0x791d4014,
            0x7ddc5da3,
            0x709f7b7a,
            0x745e66cd,
            unchecked((int)0x9823b6e0),
            unchecked((int)0x9ce2ab57),
            unchecked((int)0x91a18d8e),
            unchecked((int)0x95609039),
            unchecked((int)0x8b27c03c),
            unchecked((int)0x8fe6dd8b),
            unchecked((int)0x82a5fb52),
            unchecked((int)0x8664e6e5),
            unchecked((int)0xbe2b5b58),
            unchecked((int)0xbaea46ef),
            unchecked((int)0xb7a96036),
            unchecked((int)0xb3687d81),
            unchecked((int)0xad2f2d84),
            unchecked((int)0xa9ee3033),
            unchecked((int)0xa4ad16ea),
            unchecked((int)0xa06c0b5d),
            unchecked((int)0xd4326d90),
            unchecked((int)0xd0f37027),
            unchecked((int)0xddb056fe),
            unchecked((int)0xd9714b49),
            unchecked((int)0xc7361b4c),
            unchecked((int)0xc3f706fb),
            unchecked((int)0xceb42022),
            unchecked((int)0xca753d95),
            unchecked((int)0xf23a8028),
            unchecked((int)0xf6fb9d9f),
            unchecked((int)0xfbb8bb46),
            unchecked((int)0xff79a6f1),
            unchecked((int)0xe13ef6f4),
            unchecked((int)0xe5ffeb43),
            unchecked((int)0xe8bccd9a),
            unchecked((int)0xec7dd02d),
            0x34867077,
            0x30476dc0,
            0x3d044b19,
            0x39c556ae,
            0x278206ab,
            0x23431b1c,
            0x2e003dc5,
            0x2ac12072,
            0x128e9dcf,
            0x164f8078,
            0x1b0ca6a1,
            0x1fcdbb16,
            0x018aeb13,
            0x054bf6a4,
            0x0808d07d,
            0x0cc9cdca,
            0x7897ab07,
            0x7c56b6b0,
            0x71159069,
            0x75d48dde,
            0x6b93dddb,
            0x6f52c06c,
            0x6211e6b5,
            0x66d0fb02,
            0x5e9f46bf,
            0x5a5e5b08,
            0x571d7dd1,
            0x53dc6066,
            0x4d9b3063,
            0x495a2dd4,
            0x44190b0d,
            0x40d816ba,
            unchecked((int)0xaca5c697),
            unchecked((int)0xa864db20),
            unchecked((int)0xa527fdf9),
            unchecked((int)0xa1e6e04e),
            unchecked((int)0xbfa1b04b),
            unchecked((int)0xbb60adfc),
            unchecked((int)0xb6238b25),
            unchecked((int)0xb2e29692),
            unchecked((int)0x8aad2b2f),
            unchecked((int)0x8e6c3698),
            unchecked((int)0x832f1041),
            unchecked((int)0x87ee0df6),
            unchecked((int)0x99a95df3),
            unchecked((int)0x9d684044),
            unchecked((int)0x902b669d),
            unchecked((int)0x94ea7b2a),
            unchecked((int)0xe0b41de7),
            unchecked((int)0xe4750050),
            unchecked((int)0xe9362689),
            unchecked((int)0xedf73b3e),
            unchecked((int)0xf3b06b3b),
            unchecked((int)0xf771768c),
            unchecked((int)0xfa325055),
            unchecked((int)0xfef34de2),
            unchecked((int)0xc6bcf05f),
            unchecked((int)0xc27dede8),
            unchecked((int)0xcf3ecb31),
            unchecked((int)0xcbffd686),
            unchecked((int)0xd5b88683),
            unchecked((int)0xd1799b34),
            unchecked((int)0xdc3abded),
            unchecked((int)0xd8fba05a),
            0x690ce0ee,
            0x6dcdfd59,
            0x608edb80,
            0x644fc637,
            0x7a089632,
            0x7ec98b85,
            0x738aad5c,
            0x774bb0eb,
            0x4f040d56,
            0x4bc510e1,
            0x46863638,
            0x42472b8f,
            0x5c007b8a,
            0x58c1663d,
            0x558240e4,
            0x51435d53,
            0x251d3b9e,
            0x21dc2629,
            0x2c9f00f0,
            0x285e1d47,
            0x36194d42,
            0x32d850f5,
            0x3f9b762c,
            0x3b5a6b9b,
            0x0315d626,
            0x07d4cb91,
            0x0a97ed48,
            0x0e56f0ff,
            0x1011a0fa,
            0x14d0bd4d,
            0x19939b94,
            0x1d528623,
            unchecked((int)0xf12f560e),
            unchecked((int)0xf5ee4bb9),
            unchecked((int)0xf8ad6d60),
            unchecked((int)0xfc6c70d7),
            unchecked((int)0xe22b20d2),
            unchecked((int)0xe6ea3d65),
            unchecked((int)0xeba91bbc),
            unchecked((int)0xef68060b),
            unchecked((int)0xd727bbb6),
            unchecked((int)0xd3e6a601),
            unchecked((int)0xdea580d8),
            unchecked((int)0xda649d6f),
            unchecked((int)0xc423cd6a),
            unchecked((int)0xc0e2d0dd),
            unchecked((int)0xcda1f604),
            unchecked((int)0xc960ebb3),
            unchecked((int)0xbd3e8d7e),
            unchecked((int)0xb9ff90c9),
            unchecked((int)0xb4bcb610),
            unchecked((int)0xb07daba7),
            unchecked((int)0xae3afba2),
            unchecked((int)0xaafbe615),
            unchecked((int)0xa7b8c0cc),
            unchecked((int)0xa379dd7b),
            unchecked((int)0x9b3660c6),
            unchecked((int)0x9ff77d71),
            unchecked((int)0x92b45ba8),
            unchecked((int)0x9675461f),
            unchecked((int)0x8832161a),
            unchecked((int)0x8cf30bad),
            unchecked((int)0x81b02d74),
            unchecked((int)0x857130c3),
            0x5d8a9099,
            0x594b8d2e,
            0x5408abf7,
            0x50c9b640,
            0x4e8ee645,
            0x4a4ffbf2,
            0x470cdd2b,
            0x43cdc09c,
            0x7b827d21,
            0x7f436096,
            0x7200464f,
            0x76c15bf8,
            0x68860bfd,
            0x6c47164a,
            0x61043093,
            0x65c52d24,
            0x119b4be9,
            0x155a565e,
            0x18197087,
            0x1cd86d30,
            0x029f3d35,
            0x065e2082,
            0x0b1d065b,
            0x0fdc1bec,
            0x3793a651,
            0x3352bbe6,
            0x3e119d3f,
            0x3ad08088,
            0x2497d08d,
            0x2056cd3a,
            0x2d15ebe3,
            0x29d4f654,
            unchecked((int)0xc5a92679),
            unchecked((int)0xc1683bce),
            unchecked((int)0xcc2b1d17),
            unchecked((int)0xc8ea00a0),
            unchecked((int)0xd6ad50a5),
            unchecked((int)0xd26c4d12),
            unchecked((int)0xdf2f6bcb),
            unchecked((int)0xdbee767c),
            unchecked((int)0xe3a1cbc1),
            unchecked((int)0xe760d676),
            unchecked((int)0xea23f0af),
            unchecked((int)0xeee2ed18),
            unchecked((int)0xf0a5bd1d),
            unchecked((int)0xf464a0aa),
            unchecked((int)0xf9278673),
            unchecked((int)0xfde69bc4),
            unchecked((int)0x89b8fd09),
            unchecked((int)0x8d79e0be),
            unchecked((int)0x803ac667),
            unchecked((int)0x84fbdbd0),
            unchecked((int)0x9abc8bd5),
            unchecked((int)0x9e7d9662),
            unchecked((int)0x933eb0bb),
            unchecked((int)0x97ffad0c),
            unchecked((int)0xafb010b1),
            unchecked((int)0xab710d06),
            unchecked((int)0xa6322bdf),
            unchecked((int)0xa2f33668),
            unchecked((int)0xbcb4666d),
            unchecked((int)0xb8757bda),
            unchecked((int)0xb5365d03),
            unchecked((int)0xb1f740b4)
        };

        public CRC() => InitialiseCRC();

        internal void InitialiseCRC() => globalCrc = unchecked((int) 0xffffffff);

        internal int GetFinalCRC() => ~globalCrc;

        internal int GetGlobalCRC() => globalCrc;

        internal void SetGlobalCRC(int newCrc) => globalCrc = newCrc;

        internal void UpdateCRC(int inCh)
        {
            var temp = (globalCrc >> 24) ^ inCh;
            if (temp < 0) {
                temp = 256 + temp;
            }
            globalCrc = (globalCrc << 8) ^ crc32Table[temp];
        }

        internal int globalCrc;
    }
}