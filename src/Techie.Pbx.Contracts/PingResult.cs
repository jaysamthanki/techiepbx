namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// What <c>ping</c> answers: the Helper is running, and this is the protocol it speaks. The
    /// version is here so that a web app deployed ahead of its Helper can say so rather than
    /// failing on the first message it sends.
    /// </summary>
    public class PingResult
    {
        public int Version { get; set; }
    }
}
