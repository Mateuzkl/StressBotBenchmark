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
            _buffer = buffer;
            _pos = start;
            _endpos = end;
        }

        public int Position => _pos;
        public int Remaining => _endpos - _pos;
        public byte[] Buffer => _buffer;
        
        public byte GetU8()
        {
            if (_pos >= _endpos) return 0;
            return _buffer[_pos++];
        }

        public ushort GetU16()
        {
            if (_pos + 1 >= _endpos) return 0;
            ushort val = (ushort)(_buffer[_pos] | (_buffer[_pos + 1] << 8));
            _pos += 2;
            return val;
        }

        public uint GetU32()
        {
            if (_pos + 3 >= _endpos) return 0;
            uint val = (uint)(_buffer[_pos] | (_buffer[_pos + 1] << 8) | (_buffer[_pos + 2] << 16) | (_buffer[_pos + 3] << 24));
            _pos += 4;
            return val;
        }

        public string GetString()
        {
            ushort len = GetU16();
            if (_pos + len > _endpos) return string.Empty;
            string s = Encoding.Latin1.GetString(_buffer, _pos, len);
            _pos += len;
            return s;
        }
        
        public byte[] GetBytes(int len)
        {
            if (_pos + len > _endpos) return Array.Empty<byte>();
            byte[] b = new byte[len];
            Array.Copy(_buffer, _pos, b, 0, len);
            _pos += len;
            return b;
        }

        public void Skip(int count)
        {
            _pos += count;
        }
    }
}
