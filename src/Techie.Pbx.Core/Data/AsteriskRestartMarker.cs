namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// "A file Asterisk only reads at startup has been written, and Asterisk has not been
    /// restarted since" (D33, D104). Raised by an apply that wrote one of those files, cleared by
    /// a restart that worked.
    ///
    /// It exists because the restart is offered and can be declined: a restart drops live calls,
    /// so an admin may well say "not now", and the system then has to keep saying that Asterisk is
    /// running config that is no longer on disk. A file beside the database, like the apply marker
    /// it sits next to (D26), so the answer survives a page reload and an app restart alike.
    /// </summary>
    public class AsteriskRestartMarker : MarkerFile
    {
        public const string FileName = "restart-pending";

        public AsteriskRestartMarker(Database database)
            : this(DirectoryOf(database))
        {
        }

        public AsteriskRestartMarker(string dataDirectory)
            : base(dataDirectory, FileName)
        {
        }
    }
}
