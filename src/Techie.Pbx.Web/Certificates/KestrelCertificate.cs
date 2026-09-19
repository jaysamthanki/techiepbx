namespace Techie.Pbx.Web.Certificates
{
    /// <summary>
    /// Which certificate row the web server is actually serving, recorded when Kestrel was
    /// configured at startup (D99). Kestrel is given its certificate once, so a certificate
    /// ordered or renewed while the app is running does not reach the browser until the app
    /// restarts — and the certificates page says so per row rather than leaving an admin to
    /// wonder why the padlock still shows the old date.
    ///
    /// A static holder for the same reason <see cref="PbxDatabase"/> is one: it is decided once at
    /// startup and read by pages that construct everything else with <c>new</c> (D8).
    /// </summary>
    public static class KestrelCertificate
    {
        /// <summary>The expiry of the certificate Kestrel loaded, as the row stored it.</summary>
        public static string LoadedExpiresUtc { get; private set; } = "";

        /// <summary>The row Kestrel loaded, or 0 when it started without a certificate.</summary>
        public static long LoadedID { get; private set; }

        /// <summary>
        /// Whether this row is the one the web server is serving. False for a row that was ordered
        /// or renewed since startup, which is exactly the case worth telling an admin about: the
        /// expiry is compared as well as the ID, because a renewal keeps the row.
        /// </summary>
        public static bool IsLoaded(long certificateID, string expiresUtc) =>
            LoadedID != 0 &&
            LoadedID == certificateID &&
            string.Equals(LoadedExpiresUtc, expiresUtc, StringComparison.Ordinal);

        /// <summary>Called once, while Kestrel is being configured.</summary>
        public static void Loaded(long certificateID, string expiresUtc)
        {
            LoadedID = certificateID;
            LoadedExpiresUtc = expiresUtc;
        }
    }
}
