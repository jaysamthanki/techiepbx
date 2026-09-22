namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// What the notify endpoint answers with when it sent the email (D129). The script decides on
    /// the status code alone — 200 means "sent, do nothing more", anything else means "relay what
    /// app_voicemail composed" — so this is there for a person reading a curl session rather than
    /// for the script.
    /// </summary>
    public class VoicemailNotifyResponse
    {
        public bool Sent { get; set; }

        public VoicemailNotifyResponse(bool sent)
        {
            this.Sent = sent;
        }
    }
}
