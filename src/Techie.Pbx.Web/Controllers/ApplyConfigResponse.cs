namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// What an apply did: one sentence for the page's toast, plus the one thing the page has to
    /// act on rather than read — whether Asterisk is still running config we have replaced on
    /// disk, and which files those are, so it can name them when it offers the restart (D104).
    /// The changed-file and reloaded-module lists are still not sent: nothing renders them, and
    /// the applier logs them.
    /// </summary>
    public class ApplyConfigResponse
    {
        /// <summary>The written files no reload can pick up, named in the restart confirm.</summary>
        public List<string> RestartFiles { get; set; } = new();

        public bool RestartRequired { get; set; }

        /// <summary>
        /// Asterisk was not reachable when the apply ran — on a fresh install it has not been
        /// started yet (D93). The offer that follows says "start", not "restart", and does not
        /// warn about dropped calls, because there are none.
        /// </summary>
        public bool AsteriskDown { get; set; }

        public string Summary { get; set; } = "";
    }
}
