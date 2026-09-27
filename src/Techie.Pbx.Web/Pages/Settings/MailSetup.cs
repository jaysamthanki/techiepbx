using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// One of the ways of sending mail the Email tab offers in its dropdown: which Mail.* keys that
    /// way needs, and what the usual values for them are. Nothing here is stored — a setup is a way
    /// of looking at the same seven keys, and which one the tab opens on is worked out from them.
    ///
    /// The suggested values only ever reach the edit form, as what is in the box before the admin
    /// saves. Choosing a setup writes nothing: the settings are saved one at a time, through the
    /// same form as everywhere else, so there is still exactly one place a setting is saved.
    /// </summary>
    public class MailSetup
    {
        public const string AdvancedKey = "all";

        public const string GoogleKey = "google";

        public const string GraphKey = "graph";

        public const string SendGridKey = "sendgrid";

        public const string SmtpKey = "smtp";

        /// <summary>Google's two SMTP endpoints: app-password submission, and the IP-allowed relay.</summary>
        private static readonly string[] GoogleHosts = { "smtp.gmail.com", "smtp-relay.gmail.com" };

        private const string SendGridHost = "smtp.sendgrid.net";

        private static readonly string[] RelayKeys =
        {
            SettingsKeys.MailSmtpHost, SettingsKeys.MailSmtpPort, SettingsKeys.MailSmtpUsername, SettingsKeys.MailSmtpPassword,
        };

        private static readonly string[] SmtpSendingKeys =
        {
            SettingsKeys.MailTransport, SettingsKeys.MailFromAddress, SettingsKeys.MailFromName,
        };

        /// <summary>Every setup, in the order the dropdown lists them.</summary>
        public static IReadOnlyList<MailSetup> All { get; } = new[]
        {
            new MailSetup
            {
                Groups = new[]
                {
                    Group("Sending", "Graph sends as the from address, so it has to be a real mailbox in the tenant.",
                        SettingsKeys.MailTransport, SettingsKeys.MailFromAddress),
                    Group("SMTP relay for voicemail (optional)",
                        "Voicemail to email only ever goes through an SMTP relay, never Graph (D126). Leave this " +
                        "blank if mailboxes do not need to email their messages; otherwise fill in a relay here too.",
                        RelayKeys),
                },
                Key = GraphKey,
                Name = "Microsoft 365 (Graph)",
                Suggestions = new Dictionary<string, string> { [SettingsKeys.MailTransport] = MailTransports.Graph },
                Summary = "Sends as a mailbox in your Microsoft 365 tenant, using the Entra app this system already signs you in with.",
                Transport = MailTransports.Graph,
            },
            new MailSetup
            {
                Groups = new[]
                {
                    Group("Sending", "The from address has to be a verified sender in SendGrid.", SmtpSendingKeys),
                    Group("SMTP relay", "The username is the word apikey and the password is the API key itself.", RelayKeys),
                },
                Key = SendGridKey,
                Name = "SendGrid",
                Suggestions = new Dictionary<string, string>
                {
                    [SettingsKeys.MailSmtpHost] = SendGridHost,
                    [SettingsKeys.MailSmtpUsername] = "apikey",
                    [SettingsKeys.MailTransport] = MailTransports.Smtp,
                },
                Summary = "Submits to SendGrid's SMTP relay with an API key.",
                Transport = MailTransports.Smtp,
            },
            new MailSetup
            {
                Groups = new[]
                {
                    Group("Sending", "The from address is the Workspace account or an alias it is allowed to send as.", SmtpSendingKeys),
                    Group("SMTP relay", "smtp.gmail.com with an app password, or smtp-relay.gmail.com with no username at all.", RelayKeys),
                },
                Key = GoogleKey,
                Name = "Google Workspace",
                Suggestions = new Dictionary<string, string>
                {
                    [SettingsKeys.MailSmtpHost] = GoogleHosts[0],
                    [SettingsKeys.MailTransport] = MailTransports.Smtp,
                },
                Summary = "Submits to Google's SMTP server with an app password, or to its relay service by IP.",
                Transport = MailTransports.Smtp,
            },
            new MailSetup
            {
                Groups = new[]
                {
                    Group("Sending", "Whatever address your relay lets this server send as.", SmtpSendingKeys),
                    Group("SMTP relay", "Submission is always over STARTTLS, so a relay that only offers plain text will not work.", RelayKeys),
                },
                Key = SmtpKey,
                Name = "Other SMTP relay",
                Suggestions = new Dictionary<string, string> { [SettingsKeys.MailTransport] = MailTransports.Smtp },
                Summary = "Submits to any SMTP relay: a smarthost, your own mail server, or another hosted service.",
                Transport = MailTransports.Smtp,
            },
            new MailSetup
            {
                Groups = new[]
                {
                    Group("Sending", "Who mail comes from. Both transports need an address; the name is used by SMTP only.", SmtpSendingKeys),
                    Group("SMTP relay",
                        "The relay, when the transport is SMTP. Voicemail to email uses this relay whatever the " +
                        "transport above says, so fill it in even on Graph if mailboxes should email their messages (D126).",
                        RelayKeys),
                },
                Key = AdvancedKey,
                Name = "Show all mail settings",
                Summary = "Every mail setting, with no suggestions.",
            },
        };

        private IReadOnlyList<(string Title, string Help, string[] Keys)> Groups { get; init; } =
            Array.Empty<(string, string, string[])>();

        public string Key { get; private init; } = "";

        public string Name { get; private init; } = "";

        private IReadOnlyDictionary<string, string> Suggestions { get; init; } = new Dictionary<string, string>();

        /// <summary>The line under the dropdown saying what this setup is.</summary>
        public string Summary { get; private init; } = "";

        /// <summary>The transport this setup sends over, or blank when it does not pick one.</summary>
        public string Transport { get; private init; } = "";

        /// <summary>The setup with this key, or null for anything else — including blank.</summary>
        public static MailSetup? For(string? key) =>
            All.FirstOrDefault(setup => setup.Key == key);

        /// <summary>
        /// Which setup the stored settings look like, so the tab opens on the one already in use.
        /// Whatever a send would actually go over wins: Graph is Graph even with a relay filled in
        /// for voicemail. Otherwise the relay's host says which service it is, and an unrecognised
        /// one is just a relay. Blank when nothing has been set up yet, so the admin picks.
        /// </summary>
        public static string Guess(MailSettings settings, string transport)
        {
            if (transport == MailTransports.Graph)
                return GraphKey;

            if (transport != MailTransports.Smtp && settings.SmtpHost.Length == 0)
                return "";

            if (string.Equals(settings.SmtpHost, SendGridHost, StringComparison.OrdinalIgnoreCase))
                return SendGridKey;

            if (GoogleHosts.Contains(settings.SmtpHost, StringComparer.OrdinalIgnoreCase))
                return GoogleKey;

            return SmtpKey;
        }

        /// <summary>This setup's tables, built from what is stored, for the shared sections partial.</summary>
        public List<SettingSection> Sections(IReadOnlyDictionary<string, string> stored) =>
            this.Groups.Select(group => new SettingSection
            {
                Help = group.Help,
                Rows = group.Keys.Select(key => SettingRow.For(SettingsCatalog.For(key), stored)).ToList(),
                Setup = this.Suggestions.Count > 0 ? this.Key : "",
                Title = group.Title,
            }).ToList();

        /// <summary>The value this setup would normally have for a setting, or blank for no opinion.</summary>
        public string Suggestion(string key) =>
            this.Suggestions.TryGetValue(key, out var value) ? value : "";

        private static (string Title, string Help, string[] Keys) Group(string title, string help, params string[] keys) =>
            (title, help, keys);
    }
}
