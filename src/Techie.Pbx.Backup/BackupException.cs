namespace Techie.Pbx.Backup
{
    /// <summary>
    /// A refusal or failure with a message meant for the operator: printed as it is, without a
    /// stack trace, and the process exits non-zero.
    /// </summary>
    public class BackupException : Exception
    {
        public BackupException(string message)
            : base(message)
        {
        }

        public BackupException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
