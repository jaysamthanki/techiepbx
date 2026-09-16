namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// An AMI connection, login or action failed. Messages never contain the AMI secret.
    /// </summary>
    public class AmiException : Exception
    {
        public AmiException(string message) : base(message)
        {
        }

        public AmiException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
