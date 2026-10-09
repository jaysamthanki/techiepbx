namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// An export this importer will not read: not a gzip tarball, an entry that would land outside
    /// the staging directory, too big, or a manifest it does not understand. The message is shown
    /// to the operator as it is.
    /// </summary>
    public class MigrationArchiveException : Exception
    {
        public MigrationArchiveException(string message)
            : base(message)
        {
        }

        public MigrationArchiveException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
