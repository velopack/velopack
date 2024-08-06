using System;

namespace Ico.Binary
{
    internal ref struct BitReader
    {
        private ByteReader reader;
        private byte currentByte;
        private uint currentBitsRemaining;

        public BitReader(ByteReader reader)
        {
            this.reader = reader;
            this.currentByte = 0;
            this.currentBitsRemaining = 0;
        }

        public uint NextBit1()
        {
            return NextBit(1);
        }

        public uint NextBit2()
        {
            return NextBit(2);
        }

        public uint NextBit4()
        {
            return NextBit(4);
        }

        public uint NextBit(uint n)
        {
            EnsureBits(n);

            var shift = (byte)(currentBitsRemaining - n);
            var mask = (1u << (int)n) - 1;
            var result = (uint)(currentByte >> shift) & mask;

            currentBitsRemaining -= n;
            return result;
        }

        private void EnsureBits(uint numBitsNeeded)
        {
            if (0 != (currentBitsRemaining % numBitsNeeded))
            {
                throw new Exception("Cannot read unaligned value");
            }

            if (currentBitsRemaining >= numBitsNeeded)
            {
                return;
            }

            currentByte = reader.NextUint8();
            currentBitsRemaining = 8;
        }
    }
}
