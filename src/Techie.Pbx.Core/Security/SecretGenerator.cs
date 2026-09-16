using System.Security.Cryptography;

namespace Techie.Pbx.Core.Security
{
    public static class SecretGenerator
    {
        // No look-alike characters (0/O, 1/l/I) since people type these into phones.
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

        private const string Digits = "0123456789";

        public static string Create(int length = 24)
        {
            return RandomNumberGenerator.GetString(Alphabet, length);
        }

        /// <summary>
        /// A voicemail PIN. Digits only, because it is typed on a phone keypad. Offered as the
        /// default on a new extension so that nobody has to think of one and picks 1234.
        /// </summary>
        public static string CreatePin(int length = 6)
        {
            return RandomNumberGenerator.GetString(Digits, length);
        }
    }
}
