using System;
using System.IO;
using System.Text;

namespace StressBotBenchmark.Network
{
    public class OutputMessage
    {
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;

        public OutputMessage()
        {
            _stream = new MemoryStream();
            _writer = new BinaryWriter(_stream);
        }

        public void AddU8(byte v) => _writer.Write(v);
        public void AddU16(ushort v) => _writer.Write(v);
        public void AddU32(uint v) => _writer.Write(v);
        
        public void AddString(string s)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(s);
            AddU16((ushort)bytes.Length);
            _writer.Write(bytes);
        }

        public void AddBytes(byte[] bytes) => _writer.Write(bytes);

        public byte[] GetBuffer()
        {
            _writer.Flush();
            return _stream.ToArray();
        }

        public int Length => (int)_stream.Length;
    }
}
