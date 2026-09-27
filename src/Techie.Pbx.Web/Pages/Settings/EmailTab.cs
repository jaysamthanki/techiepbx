using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the System page's Email tab shows: the Mail.* settings, and — because the transport
    /// setting may be blank and blank means "decide for me" — what that decision actually came out
    /// as right now (D115). An admin should not have to send a message to find out which of the
    /// two transports their settings resolve to.
    ///
    /// Below that is one mail setup at a time (<see cref="MailSetup"/>), chosen from a dropdown, with
    /// only the settings that setup needs.
    /// </summary>
    public class EmailTab
    {
        /// <summary>Whether this server has an Entra app credential, i.e. whether Graph could send.</summary>
        public bool GraphAvailable { get; set; }

        public List<SettingSection> Sections { get; set; } = new();

        /// <summary>The mail setup being shown, or null when none has been picked yet.</summary>
        public MailSetup? Setup { get; set; }

        /// <summary>
        /// Whether the chosen setup names a transport that a send would not use right now — the
        /// admin has picked SendGrid, say, but Mail.Transport is not smtp yet.
        /// </summary>
        public bool SetupTransportDiffers =>
            this.Setup is { Transport.Length: > 0 } setup && setup.Transport != this.Transport;

        /// <summary>The transport a send would use now: graph, smtp, or blank for none.</summary>
        public string Transport { get; set; } = "";

        /// <summary>The sentence the tab leads with: what would happen if something sent mail now.</summary>
        public string TransportSummary => this.Transport switch
        {
            MailTransports.Graph =>
                "Mail is sent through Microsoft Graph, as the mailbox in the from address.",
            MailTransports.Smtp =>
                "Mail is submitted to the SMTP relay in Mail.Smtp.Host.",
            _ =>
                "This system cannot send mail yet. Choose a setup below and fill in its settings, " +
                "or configure this app's Entra client secret on the server to send through Graph.",
        };
    }
}
