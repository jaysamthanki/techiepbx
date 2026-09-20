namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// What happened when mail was sent: whether it left this box, and a sentence an admin can act
    /// on when it did not. Modelled on <c>RestartResult</c>, and with the same rule — the message
    /// is one we wrote, so it never carries a credential, a stack trace or a raw server banner.
    /// </summary>
    public class MailResult
    {
        public string Message { get; }

        public bool Success { get; }

        public MailResult(bool success, string message)
        {
            this.Message = message;
            this.Success = success;
        }

        /// <summary>A send that did not happen, and why.</summary>
        public static MailResult Failed(string message) => new(false, message);

        /// <summary>A send the transport accepted.</summary>
        public static MailResult Sent(string message) => new(true, message);
    }
}
