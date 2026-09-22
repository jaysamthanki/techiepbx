using log4net;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Web
{
    /// <summary>
    /// Keeps <c>Config/mail.json</c> in step with the <c>Mail.*</c> settings, so that the
    /// <c>voicemail-mail</c> script app_voicemail runs has the current relay details (D126).
    /// A static holder for the same reason <see cref="PbxSounds"/> is one (D22).
    ///
    /// Written at startup as well as on every settings change. Startup matters: the file lives in
    /// the install, and a re-deploy that did not preserve it, or an upgrade onto a box that never
    /// had one, would otherwise leave voicemail email quietly broken until somebody happened to
    /// re-save a mail setting.
    /// </summary>
    public static class PbxMailConfig
    {
        /// <summary>
        /// How long the generated callback token is. It is never typed by anybody — the app writes
        /// it into mail.json and the script reads it back — so it is as long as it is useful to be.
        /// </summary>
        private const int TokenLength = 40;

        private static readonly ILog Log = LogManager.GetLogger(typeof(PbxMailConfig));

        private static MailConfigFile? file;

        public static MailConfigFile Current =>
            file ?? throw new InvalidOperationException("The mail config file is not open yet; PbxMailConfig.Open runs at startup.");

        /// <summary>
        /// Points at the Config folder inside the install and writes what is stored right now.
        /// The path has to be the one the script reads — <c>/opt/tnpbx/Config/mail.json</c> on a
        /// deployed box — which it is, because the install is <c>/opt/tnpbx</c>.
        /// </summary>
        public static void Open(string contentRootPath, SettingsRepository settings)
        {
            file = new MailConfigFile(Path.Combine(contentRootPath, MailConfigFile.DirectoryName));
            Token(settings);
            Write(settings);
        }

        /// <summary>
        /// Makes sure there is a callback token for the script to prove itself with (D129), and
        /// leaves an existing one alone. Generated rather than typed: it is a machine credential
        /// between two processes on one box, and a feature that needs an admin to invent a secret
        /// before it works is a feature that does not work.
        ///
        /// Clearing it on the Settings page is how an admin switches the callback off; the next
        /// start makes a new one, which is the same bargain the ACME account key has.
        /// </summary>
        private static void Token(SettingsRepository settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Get(SettingsKeys.MailVoicemailCallbackToken)))
                return;

            try
            {
                settings.Set(SettingsKeys.MailVoicemailCallbackToken, SecretGenerator.Create(TokenLength));
                Log.Info("Generated a voicemail callback token, so voicemail email is composed by this application");
            }
            catch (Exception ex)
            {
                // A box that could not store it still emails voicemail: the script relays what
                // app_voicemail composed, exactly as it did before the callback existed (D126).
                Log.Error($"Could not generate the voicemail callback token: {ex.Message}");
            }
        }

        /// <summary>
        /// Rewrites the file from the settings as they are now. Called whenever a setting is saved
        /// or reset — every key, not only the mail ones, because that is one file write on an
        /// action an admin takes by hand, and it cannot then be missed when a key is added.
        /// </summary>
        public static void Write(SettingsRepository settings)
        {
            if (file == null)
            {
                Log.Warn("Asked to write the mail config before startup opened it; ignored");
                return;
            }

            file.Write(new MailSettings(settings.GetAll()));
        }
    }
}
