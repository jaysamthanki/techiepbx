using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the System page's Email tab shows: the Mail.* settings, and — because the transport
    /// setting may be blank and blank means "decide for me" — what that decision actually came out
    /// as right now (D115). An admin should not have to send a message to find out which of the
    /// two transports their settings resolve to.
    /// </summary>
    public class EmailTab
    {
        /// <summary>Whether this server has an Entra app credential, i.e. whether Graph could send.</summary>
        public bool GraphAvailable { get; set; }

        public List<SettingSection> Sections { get; set; } = new();

        /// <summary>The transport a send would use now: graph, smtp, or blank for none.</summary>
        public string Transport { get; set; } = "";

        /// <summary>The sentence the tab leads with: what would happen if something sent mail now.</summary>
        public string TransportSummary => this.Transport switch
        {
            MailTransports.Graph =>
                "Mail is sent through Microsoft Graph, as the mailbox in the from address.",
            MailTransports.Smtp =>
                "Mail is submitted to the SMTP relay below.",
            _ =>
                "This system cannot send mail yet. Set the transport to SMTP and fill in the relay below, " +
                "or configure this app's Entra client secret on the server to send through Graph.",
        };
    }
}
