using System.Security.Cryptography;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// The AMI secret is a machine-to-machine handshake between two things this app owns both
    /// ends of: the Settings row it stores and the manager.conf it renders. There is no reason
    /// for an operator to invent one, so a box that has never had one gets a generated value
    /// at startup (D156): a fresh install is never left with a blank credential, and the first
    /// apply works without anyone visiting the Settings page first.
    /// </summary>
    public static class AmiSecret
    {
        /// <summary>
        /// Writes a generated secret into the store if — and only if — the row is missing or
        /// empty. An existing value is never touched, so a restart cannot churn the credential
        /// Asterisk is holding. Returns the value now in the store.
        /// </summary>
        public static string Ensure(SettingsRepository settings)
        {
            var current = settings.Get(SettingsKeys.AmiSecret);
            if (!string.IsNullOrWhiteSpace(current))
                return current;

            var generated = Generate();
            settings.Set(SettingsKeys.AmiSecret, generated);
            return generated;
        }

        /// <summary>
        /// 32 hex characters from the cryptographic generator. Enough entropy for a loopback
        /// service credential, and no characters that could trouble a conf file either way
        /// (ConfText.Safe is still applied at render time, as for anything else).
        /// </summary>
        public static string Generate() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}
