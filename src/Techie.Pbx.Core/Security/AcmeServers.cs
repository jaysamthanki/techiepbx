namespace Techie.Pbx.Core.Security
{
    /// <summary>
    /// The ACME directories <see cref="Data.SettingsKeys.CertAcmeServer"/> may name (D97). Two, and
    /// not "any URL": an ACME server is handed our account key and decides what certificates this
    /// machine presents, so the list of who may do that is code rather than configuration. Staging
    /// is here because testing an order against production burns a rate limit that takes a week to
    /// come back.
    /// </summary>
    public static class AcmeServers
    {
        /// <summary>Let's Encrypt, the one a real system uses.</summary>
        public const string LetsEncrypt = "https://acme-v02.api.letsencrypt.org/directory";

        /// <summary>
        /// Let's Encrypt's staging environment. It issues certificates signed by an authority no
        /// browser trusts, which is exactly what makes it safe to order from repeatedly.
        /// </summary>
        public const string LetsEncryptStaging = "https://acme-staging-v02.api.letsencrypt.org/directory";

        /// <summary>Where orders go when nobody has chosen otherwise.</summary>
        public const string Default = LetsEncrypt;

        /// <summary>Both, in the order the settings page offers them.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { LetsEncrypt, LetsEncryptStaging };

        /// <summary>Whether this is a directory we will send an account key to.</summary>
        public static bool IsKnown(string url) => All.Contains(url, StringComparer.Ordinal);
    }
}
