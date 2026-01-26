using System.Security.Cryptography;

namespace Consumo_App.Servicios
{

    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string password, string hash);
    }
    public class Pbkdf2Hasher : IPasswordHasher
    {
        // Formato: PBKDF2|iter|saltBase64|hashBase64
        private const int Iter = 100_000;
        private const int SaltSize = 16; // 128-bit
        private const int KeySize = 32;  // 256-bit

        public string Hash(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iter, HashAlgorithmName.SHA256, KeySize);
            return $"PBKDF2|{Iter}|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(key)}";
        }

        public bool Verify(string password, string hash)
        {
            try
            {
                var parts = hash.Split('|');
                if (parts.Length != 4 || parts[0] != "PBKDF2") return false;
                var iter = int.Parse(parts[1]);
                var salt = Convert.FromBase64String(parts[2]);
                var key = Convert.FromBase64String(parts[3]);

                var keyToCheck = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, key.Length);
                return CryptographicOperations.FixedTimeEquals(keyToCheck, key);
            }
            catch { return false; }
        }
    }
}
