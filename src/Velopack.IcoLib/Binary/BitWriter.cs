using System;

namespace Ico.Binary
{
    internal class BitWriter
    {
        private ByteWriter writer;
        private uint currentBitsRemaining;

        public BitWriter(ByteWriter writer)
        {
            this.writer = writer;
            this.currentBitsRemaining = 0;
        }

        public void AddBit1(byte value)
        {
            AddBits(1, value);
        }

        public void AddBit2(byte value)
        {
            AddBits(2, value);
        }

        public void AddBit4(byte value)
        {
            AddBits(4, value);
        }

        public void AddBits(uint numBits, byte value)
        {
            EnsureBits(numBits);

            if (value != 0)
            {
                var shift = (int)(currentBitsRemaining - numBits);

                var b = writer.Data[writer.Data.Count - 1];
                b = (byte)(b | (value << shift));
                writer.Data[writer.Data.Count - 1] = b;
            }

            currentBitsRemaining -= numBits;
        }

        private void EnsureBits(uint numBitsNeeded)
        {
            if (0 != (currentBitsRemaining % numBitsNeeded))
            {
                throw new Exception("Cannot write unaligned value");
            }

            if (currentBitsRemaining >= numBitsNeeded)
            {
                return;
            }

            writer.AddUint8(0);
            currentBitsRemaining = 8;
        }
    }
}
