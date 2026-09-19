namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// What happened when Asterisk was asked to restart: whether it worked, and a sentence an
    /// admin can act on when it did not. Never carries the raw command line.
    /// </summary>
    public class RestartResult
    {
        public string Message { get; }
        public bool Success { get; }

        public RestartResult(bool success, string message)
        {
            this.Message = message;
            this.Success = success;
        }
    }
}
