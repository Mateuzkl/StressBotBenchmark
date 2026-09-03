using System;
using System.Text;

namespace StressBotBenchmark.Network
{
    public class InputMessage
    {
        private readonly byte[] _buffer;
        private int _pos;
        private readonly int _endpos;

        public InputMessage(byte[] buffer)
        {
            _buffer = buffer;
            _pos = 0;
            _endpos = buffer.Length;
        }

        public InputMessage(byte[] buffer, int start, int end)
        {
            if (start < 0 || end < start || end > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(end));
            _buffer = buffer;
            _pos = start;
            _endpos = end;
        }

        public int Position => _pos;
        public int Remaining => _endpos - _pos;
        public byte[] Buffer => _buffer;
        
        public byte GetU8()
        {
            Require(1);
            return _buffer[_pos++];
        }

        public ushort GetU16()
        {
            Require(2);
            ushort val = (ushort)(_buffer[_pos] | (_buffer[_pos + 1] << 8));
            _pos += 2;
            return val;
        }

        public uint GetU32()
        {
            Require(4);
            uint val = (uint)(_buffer[_pos] | (_buffer[_pos + 1] << 8) | (_buffer[_pos + 2] << 16) | (_buffer[_pos + 3] << 24));
            _pos += 4;
            return val;
        }

        public string GetString()
        {
            ushort len = GetU16();
            Require(len);
            string s = Encoding.Latin1.GetString(_buffer, _pos, len);
            _pos += len;
            return s;
        }
        
        public byte[] GetBytes(int len)
        {
            Require(len);
            byte[] b = new byte[len];
            Array.Copy(_buffer, _pos, b, 0, len);
            _pos += len;
            return b;
        }

        public void Skip(int count)
        {
            Require(count);
            _pos += count;
        }

        /// <summary>Read a map position: u16 x, u16 y, u8 z.</summary>
        public (ushort X, ushort Y, byte Z) GetPosition()
        {
            ushort x = GetU16();
            ushort y = GetU16();
            byte z = GetU8();
            return (x, y, z);
        }

        /// <summary>Peek at the next u16 without advancing the cursor.</summary>
        public ushort PeekU16()
        {
            Require(2);
            return (ushort)(_buffer[_pos] | (_buffer[_pos + 1] << 8));
        }

        /// <summary>Read a signed 32-bit integer (little-endian).</summary>
        public int GetI32()
        {
            Require(4);
            int val = _buffer[_pos] | (_buffer[_pos + 1] << 8) | (_buffer[_pos + 2] << 16) | (_buffer[_pos + 3] << 24);
            _pos += 4;
            return val;
        }

        /// <summary>Check whether at least <paramref name="count"/> bytes remain.</summary>
        public bool CanRead(int count) => count >= 0 && count <= Remaining;

        private void Require(int count)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidDataException("Truncated server message.");
        }
    }
}
