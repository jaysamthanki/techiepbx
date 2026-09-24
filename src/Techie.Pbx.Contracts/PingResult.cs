namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// What <c>ping</c> answers: the Helper is running, and this is the protocol it speaks. The
    /// version is here so that a web app deployed ahead of its Helper can say so rather than
    /// failing on the first message it sends.
    /// </summary>
    public class PingResult
    {
        /// <summary>The build of the Helper binary, from the product version (e.g. 0.1.1).</summary>
        public string? AppVersion { get; set; }

        public int Version { get; set; }
    }
}
