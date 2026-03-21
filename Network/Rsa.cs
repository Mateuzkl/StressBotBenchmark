using System;
using System.Numerics;

namespace StressBotBenchmark.Network
{
    public static class Rsa
    {
        private static readonly BigInteger Modulus = BigInteger.Parse("109120132967399429278860960508995541528237502902798129123468757937266291492576446330739696001110603907230888610072655818825358503429057592827629436413108566029093628212635953836686562675849720620786279431090218017681061521755056710823876476444260558147179707119674283982419152118103759076030616683978566631413");
        
        public static byte[] Encrypt(byte[] data)
        {
            if (data.Length != 128)
                throw new ArgumentException("RSA Input data must be exactly 128 bytes.");
                
            byte[] mBytes = new byte[129];
            for(int i = 0; i < 128; i++) 
            {
                mBytes[128 - 1 - i] = data[i]; // Reverse to Little Endian
            }
            mBytes[128] = 0x00; // Sign bit, ensures positive integer
            
            BigInteger m = new BigInteger(mBytes);
            BigInteger c = BigInteger.ModPow(m, 65537, Modulus);
            
            byte[] cBytes = c.ToByteArray();
            byte[] result = new byte[128];
            int count = Math.Min(128, cBytes.Length);
            for(int i = 0; i < count; i++) 
            {
                result[128 - 1 - i] = cBytes[i];
            }
            return result;
        }
    }
}
