using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NightLights.Rgb
{
    /// <summary>
    /// Re-implementation of the AES-256 scheme Kingston's FuryControllerService.exe
    /// uses to wrap every message on its local WebSocket API (ws://127.0.0.1:55599/).
    ///
    /// This was recovered by decompiling FuryControllerService.exe's
    /// StringEncryptDecrypt.Encrypt/Decrypt methods (a public-domain "SimpleAES"-style
    /// snippet) purely to interoperate with the already-installed, already-licensed
    /// FURY CTRL service on this machine - no part of Kingston's code is reproduced here,
    /// only the wire format needed to talk to it the same way its own GUI does.
    ///
    /// Format: Base64( salt[32] || iv[32] || AES256-CBC-PKCS7(plaintext) )
    /// key = PBKDF2-SHA1(passPhrase, salt, 1000 iterations, 32 bytes)
    /// Note the 256-bit (32-byte) block size - this is Rijndael, not standard AES
    /// (which is fixed at a 128-bit block), so it requires RijndaelManaged specifically.
    /// </summary>
    internal static class FuryCrypto
    {
        private const int Keysize = 256;
        private const int DerivationIterations = 1000;

        public static string Encrypt(string plainText, string passPhrase)
        {
            byte[] saltBytes = Generate256BitsOfRandomEntropy();
            byte[] ivBytes = Generate256BitsOfRandomEntropy();
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            using (var password = new Rfc2898DeriveBytes(passPhrase, saltBytes, DerivationIterations))
            {
                byte[] keyBytes = password.GetBytes(Keysize / 8);
                using (var symmetricKey = new RijndaelManaged())
                {
                    symmetricKey.BlockSize = 256;
                    symmetricKey.Mode = CipherMode.CBC;
                    symmetricKey.Padding = PaddingMode.PKCS7;

                    using (var encryptor = symmetricKey.CreateEncryptor(keyBytes, ivBytes))
                    using (var memoryStream = new MemoryStream())
                    using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                        cryptoStream.FlushFinalBlock();

                        byte[] cipherTextBytes = saltBytes
                            .Concat(ivBytes)
                            .Concat(memoryStream.ToArray())
                            .ToArray();

                        return Convert.ToBase64String(cipherTextBytes);
                    }
                }
            }
        }

        public static string Decrypt(string cipherText, string passPhrase)
        {
            byte[] cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText);
            byte[] saltBytes = cipherTextBytesWithSaltAndIv.Take(32).ToArray();
            byte[] ivBytes = cipherTextBytesWithSaltAndIv.Skip(32).Take(32).ToArray();
            byte[] cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip(64)
                .Take(cipherTextBytesWithSaltAndIv.Length - 64).ToArray();

            using (var password = new Rfc2898DeriveBytes(passPhrase, saltBytes, DerivationIterations))
            {
                byte[] keyBytes = password.GetBytes(Keysize / 8);
                using (var symmetricKey = new RijndaelManaged())
                {
                    symmetricKey.BlockSize = 256;
                    symmetricKey.Mode = CipherMode.CBC;
                    symmetricKey.Padding = PaddingMode.PKCS7;

                    using (var decryptor = symmetricKey.CreateDecryptor(keyBytes, ivBytes))
                    using (var memoryStream = new MemoryStream(cipherTextBytes))
                    using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                    {
                        byte[] plainTextBytes = new byte[cipherTextBytes.Length];
                        int decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
                        return Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount);
                    }
                }
            }
        }

        private static byte[] Generate256BitsOfRandomEntropy()
        {
            var randomBytes = new byte[32];
            using (var rngCsp = new RNGCryptoServiceProvider())
            {
                rngCsp.GetBytes(randomBytes);
            }
            return randomBytes;
        }
    }
}
