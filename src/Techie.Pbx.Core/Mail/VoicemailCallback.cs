namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// Where the <c>voicemail-mail</c> script calls this application back, and what it calls it
    /// with (D129). Constants rather than settings: there is exactly one endpoint, on the loopback
    /// address, and a configurable URL here would be a URL an admin could point anywhere.
    ///
    /// It lives in Core because two very different things have to agree on it — the controller
    /// that answers, and <see cref="MailConfigFile"/>, which writes it into the JSON the script
    /// reads so that the script has no path of its own compiled in.
    /// </summary>
    public static class VoicemailCallback
    {
        /// <summary>
        /// The loopback address and the port this app always listens on (WebBindings.BootstrapPort
        /// is 8080 in every mode, with or without a certificate). Loopback because the only caller
        /// is a script on this same machine, and plain HTTP for the same reason: the hop never
        /// leaves the box, and 8080 is the binding that has no certificate to present.
        /// </summary>
        public const string BaseUrl = "http://127.0.0.1:8080";

        /// <summary>
        /// How long the script may wait for an answer. Generous on purpose: transcription happens
        /// inside this request and is allowed 300 seconds of its own, so a shorter wait here would
        /// make the script give up on a message the app is in the middle of sending.
        /// </summary>
        public const int TimeoutSeconds = 360;

        /// <summary>The route, without a leading slash, as the controller's attribute wants it.</summary>
        public const string Route = "api/voicemail/notify";

        /// <summary>The whole URL, as the script posts to it.</summary>
        public const string Url = BaseUrl + "/" + Route;
    }
}
