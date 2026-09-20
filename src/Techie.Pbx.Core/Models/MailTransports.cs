namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// How this system sends mail: the two values <see cref="Data.SettingsKeys.MailTransport"/> may
    /// name. Two and not "whatever is installed", for the same reason <see cref="Security.AcmeServers"/>
    /// is a list — a transport is handed the credential that lets this box send as somebody, so who
    /// may be handed it is code rather than configuration.
    ///
    /// Blank is a third state and deliberately not in <see cref="All"/>: it means "decide for me",
    /// which resolves to <see cref="Graph"/> where the app has an Entra app credential to send with
    /// and to no mail at all where it has not (D115).
    /// </summary>
    public static class MailTransports
    {
        /// <summary>
        /// Microsoft Graph, using this app's own Entra registration. The right answer for a site
        /// already on Microsoft 365, because it needs no second credential and no open relay.
        /// </summary>
        public const string Graph = "graph";

        /// <summary>A plain SMTP submission relay: SendGrid, Google Workspace, or the site's own.</summary>
        public const string Smtp = "smtp";

        /// <summary>Both, in the order the settings form offers them.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { Graph, Smtp };

        /// <summary>Whether this is a transport this system knows how to send with.</summary>
        public static bool IsKnown(string value) => All.Contains(value, StringComparer.Ordinal);
    }
}
