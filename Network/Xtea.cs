using System;

namespace StressBotBenchmark.Network
{
    public static class Xtea
    {
        public static unsafe void Encrypt(Span<byte> buffer, uint[] key)
        {
            int pad = (8 - buffer.Length % 8) % 8;
            if (pad != 0)
            {
                throw new ArgumentException("Buffer length must be a multiple of 8 for XTEA encrypt.");
            }

            fixed (byte* ptr = buffer)
            {
                uint* words = (uint*)ptr;
                int length = buffer.Length / 4;

                for (int i = 0; i < length; i += 2)
                {
                    uint v0 = words[i], v1 = words[i + 1];
                    uint sum = 0, delta = 0x9E3779B9;

                    for (int j = 0; j < 32; j++)
                    {
                        v0 += (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (sum + key[sum & 3]);
                        sum += delta;
                        v1 += (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (sum + key[(sum >> 11) & 3]);
                    }

                    words[i] = v0;
                    words[i + 1] = v1;
                }
            }
        }

        public static unsafe void Decrypt(Span<byte> buffer, uint[] key)
        {
            if (buffer.Length % 8 != 0)
            {
                return;
            }

            fixed (byte* ptr = buffer)
            {
                uint* words = (uint*)ptr;
                int length = buffer.Length / 4;

                for (int i = 0; i < length; i += 2)
                {
                    uint v0 = words[i], v1 = words[i + 1];
                    uint delta = 0x9E3779B9, sum = 0xC6EF3720;

                    for (int j = 0; j < 32; j++)
                    {
                        v1 -= (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (sum + key[(sum >> 11) & 3]);
                        sum -= delta;
                        v0 -= (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (sum + key[sum & 3]);
                    }

                    words[i] = v0;
                    words[i + 1] = v1;
                }
            }
        }
    }
}
