using System.Security.Cryptography;

namespace Techie.Pbx.Core.Security
{
    public static class SecretGenerator
    {
        // No look-alike characters (0/O, 1/l/I) since people type these into phones.
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

        public static string Create(int length = 24)
        {
            return RandomNumberGenerator.GetString(Alphabet, length);
        }
    }
}
