using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;

string pem = File.ReadAllText(@"c:\Users\Mateus\Desktop\moviebr\forgottenserver-downgrade-1.8-8.60\key.pem");
var rsa = RSA.Create();
rsa.ImportFromPem(pem);
var p = rsa.ExportParameters(false);
byte[] mod = p.Modulus!;
byte[] le = new byte[mod.Length + 1];
for (int i = 0; i < mod.Length; i++) le[mod.Length - 1 - i] = mod[i];
le[mod.Length] = 0;
var bi = new BigInteger(le);
Console.WriteLine(bi.ToString());
