using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace StudyFocus
{
    internal sealed class AdvancedCredential
    {
        public string Salt { get; set; }
        public string PasswordHash { get; set; }
        public string RecoveryHash { get; set; }

        public static bool ValidPassword(string password)
        {
            return !string.IsNullOrWhiteSpace(password) && password.Length >= 6 && password.Length <= 128;
        }

        public static AdvancedCredential Create(string password, out string recovery)
        {
            if (!ValidPassword(password)) throw new ArgumentException("密码需要 6 至 128 个字符。");
            byte[] salt = new byte[16];
            byte[] random = new byte[16];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(salt);
                generator.GetBytes(random);
            }
            string hex = BitConverter.ToString(random).Replace("-", "");
            recovery = string.Join("-", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 8, 8)));
            return new AdvancedCredential
            {
                Salt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(Derive(password, salt)),
                RecoveryHash = Convert.ToBase64String(DigestRecovery(recovery))
            };
        }

        public bool MatchesPassword(string password)
        {
            return ValidPassword(password) &&
                Equal(Convert.FromBase64String(PasswordHash), Derive(password, Convert.FromBase64String(Salt)));
        }

        public bool MatchesRecovery(string recovery)
        {
            return Equal(Convert.FromBase64String(RecoveryHash), DigestRecovery(recovery));
        }

        private static byte[] DigestRecovery(string recovery)
        {
            string normalized = new string(recovery.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.ASCII.GetBytes(normalized));
        }

        private static bool Equal(byte[] expected, byte[] received)
        {
            if (expected.Length != received.Length) return false;
            int difference = 0;
            for (int i = 0; i < expected.Length; i++) difference |= expected[i] ^ received[i];
            return difference == 0;
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            byte[] input = Encoding.UTF8.GetBytes(password);
            byte[] hash = new byte[32];
            IntPtr algorithm;
            int status = BCryptOpenAlgorithmProvider(out algorithm, "SHA256", null, 8);
            if (status != 0) throw new CryptographicException(status);
            try
            {
                status = BCryptDeriveKeyPBKDF2(algorithm, input, input.Length, salt, salt.Length,
                    600000, hash, hash.Length, 0);
                if (status != 0) throw new CryptographicException(status);
                return hash;
            }
            finally
            {
                Array.Clear(input, 0, input.Length);
                BCryptCloseAlgorithmProvider(algorithm, 0);
            }
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string id, string implementation, int flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptDeriveKeyPBKDF2(IntPtr algorithm, byte[] password, int passwordLength,
            byte[] salt, int saltLength, ulong iterations, [Out] byte[] key, int keyLength, int flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);
    }
}
