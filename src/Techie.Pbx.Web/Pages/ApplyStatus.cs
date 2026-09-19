namespace Techie.Pbx.Web.Pages
{
    /// <summary>
    /// The two things the navbar polls for (D43, D104): whether the database has changed since the
    /// last apply, and whether Asterisk is still running config an apply has already replaced on
    /// disk. Both are file existence checks, so asking every few seconds costs nothing (D26).
    ///
    /// They are independent. An apply that wrote asterisk.conf clears the first and raises the
    /// second, so the usual sight is one or the other, not both.
    /// </summary>
    public class ApplyStatus
    {
        /// <summary>The database has changed and Asterisk has not been given it yet.</summary>
        public bool ConfigPending { get; set; }

        /// <summary>A file Asterisk only reads at startup was written, and it has not restarted since.</summary>
        public bool RestartRequired { get; set; }
    }
}
