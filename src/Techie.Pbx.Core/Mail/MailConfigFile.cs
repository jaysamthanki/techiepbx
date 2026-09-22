using System.Text;
using System.Text.Json;
using log4net;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// The one file this application writes for something else to send mail with: the SMTP relay
    /// details, in the app's own directory tree, read by the <c>voicemail-mail</c> script that
    /// app_voicemail's <c>mailcmd</c> points at (D126).
    ///
    /// It exists because the two senders are in different processes and cannot share a settings
    /// row: the test button reads <see cref="MailSettings"/> out of the database, and a script
    /// running as the asterisk user has no business opening that database. The file is the whole
    /// interface between them, and it is written from those same settings, so there is still one
    /// credential on the box.
    ///
    /// It also carries the callback token and URL the script uses to ask this application to
    /// compose and send the email itself (D129) — same file, because the interface between these
    /// two processes should stay one file rather than two.
    ///
    /// It carries the SMTP password, so: mode 0640, owned by the web user, group-readable by
    /// asterisk through the setgid directory the installer creates. Never logged, and a settings
    /// change that leaves the relay unusable <b>removes</b> it rather than leaving a stale
    /// credential behind — the script then fails closed and says what is missing.
    /// </summary>
    public class MailConfigFile
    {
        /// <summary>
        /// The directory, inside the install, that this file lives in. Named rather than derived
        /// so that the installer and the application agree: the script's path is a fixed
        /// <c>/opt/tnpbx/Config/mail.json</c>, and on a deployed box this is that.
        /// </summary>
        public const string DirectoryName = "Config";

        public const string FileName = "mail.json";

        /// <summary>
        /// Owner read/write, group read, nothing for anyone else — the same rule the generated
        /// conf files follow, and for the same reason: a process in the asterisk group has to
        /// read it, and nobody else may.
        /// </summary>
        private const UnixFileMode SecretFileMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

        private static readonly ILog Log = LogManager.GetLogger(typeof(MailConfigFile));

        /// <summary>The directory the file lives in, created on the first write if it is missing.</summary>
        public string DirectoryPath { get; }

        /// <summary>Where the file is, whether or not it exists.</summary>
        public string FilePath { get; }

        public MailConfigFile(string directoryPath)
        {
            this.DirectoryPath = directoryPath;
            this.FilePath = Path.Combine(directoryPath, FileName);
        }

        /// <summary>
        /// Why voicemail email would not work with these settings, or null if it would. Only the
        /// SMTP half is checked: the script is an SMTP relay client and nothing else, so a site
        /// whose transport is Graph has no way to deliver voicemail (D126).
        /// </summary>
        public static string? Problem(MailSettings settings)
        {
            if (settings.SmtpHost.Length == 0)
                return "no SMTP host is set";

            if (settings.SmtpPort is < 1 or > 65535)
                return "the SMTP port is not a usable port";

            if (settings.FromAddress.Length == 0)
                return "no from address is set";

            return null;
        }

        /// <summary>
        /// Writes the relay details, or removes the file when there are none worth writing, and
        /// answers whether a usable file is there afterwards.
        ///
        /// Nothing here throws at the caller: this runs off the back of saving a setting and off
        /// the back of startup, and neither should fail because a file could not be written. A
        /// failure is logged with the path and the reason, and never with the password.
        /// </summary>
        public bool Write(MailSettings settings)
        {
            var problem = Problem(settings);

            if (problem != null)
            {
                this.Remove(problem);
                return false;
            }

            var json = JsonSerializer.Serialize(
                new
                {
                    // Where the script asks this application to compose and send the email itself,
                    // and what it proves itself with (D129). A blank token switches the callback
                    // off rather than opening it: the script then relays what app_voicemail
                    // composed, which is exactly the D126 behaviour.
                    CallbackToken = settings.VoicemailCallbackToken,
                    CallbackUrl = VoicemailCallback.Url,
                    From = settings.FromAddress,
                    FromName = settings.FromName,
                    Host = settings.SmtpHost,
                    Password = settings.SmtpPassword,
                    Port = settings.SmtpPort,
                    Username = settings.SmtpUsername,
                },
                new JsonSerializerOptions { WriteIndented = true });

            var temp = Path.Combine(this.DirectoryPath, $".{FileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                Directory.CreateDirectory(this.DirectoryPath);

                // Created empty, restricted, and only then filled. Unlike a conf file this one
                // must never exist even briefly at whatever mode the umask felt like, because
                // what goes into it is a password.
                File.WriteAllText(temp, "");
                SetMode(temp);
                File.WriteAllText(temp, json + Environment.NewLine, new UTF8Encoding(false));

                File.Move(temp, this.FilePath, overwrite: true);
                SetMode(this.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not write {this.FilePath}, so voicemail cannot be emailed: {ex.Message}");
                return false;
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            Log.Info($"Wrote {this.FilePath}: voicemail is emailed through {settings.SmtpHost}:{settings.SmtpPort} as {settings.FromAddress}");
            return true;
        }

        /// <summary>
        /// Takes the file away, so that a system whose relay details have been cleared stops
        /// emailing voicemail rather than carrying on with the credentials it used to have.
        /// </summary>
        private void Remove(string problem)
        {
            try
            {
                if (File.Exists(this.FilePath))
                {
                    File.Delete(this.FilePath);
                    Log.Warn($"Removed {this.FilePath}: {problem}, so voicemail cannot be emailed");
                }
                else
                {
                    Log.Info($"Voicemail is not emailed: {problem}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not remove {this.FilePath}: {ex.Message}");
            }
        }

        private static void SetMode(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, SecretFileMode);
        }
    }
}
