using System.Reflection;
using Techie.Pbx.Core.Mail;
using Xunit;

namespace Techie.Pbx.Tests
{
    /// <summary>
    /// The email templates ship as embedded resources (D114, D129): HTML files under Templates/,
    /// embedded as templates/&lt;Name&gt;.html the same way schema scripts are embedded as
    /// schema/NNN_name.sql. This keeps the check small: the resource is there, and it carries the
    /// placeholders the mail senders will fill in.
    /// </summary>
    public class EmailTemplateTests
    {
        private static string ReadTemplate() => ReadTemplate("templates/AlertEmail.html");

        private static string ReadTemplate(string resourceName)
        {
            var assembly = typeof(Techie.Pbx.Core.Data.Database).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }

        [Fact]
        public void AlertEmailTemplateIsEmbedded()
        {
            var names = typeof(Techie.Pbx.Core.Data.Database).Assembly.GetManifestResourceNames();
            Assert.Contains("templates/AlertEmail.html", names);
        }

        [Fact]
        public void AlertEmailTemplateCarriesThePlaceholders()
        {
            var template = ReadTemplate();

            foreach (var placeholder in new[]
                     {
                         "{{Subject}}", "{{Timestamp}}", "{{Body}}", "{{Hostname}}",
                         "{{AccentColor}}", "{{DetailRows}}", "{{ButtonUrl}}", "{{ButtonText}}",
                     })
                Assert.Contains(placeholder, template);
        }

        [Fact]
        public void AlertEmailTemplateDropsItsOptionalBlocksCleanly()
        {
            // The optional blocks are paired markers; the sender removes the whole block
            // including its marker lines when there is nothing to show.
            var template = ReadTemplate();

            Assert.Contains("{{DetailBlockStart}}", template);
            Assert.Contains("{{DetailBlockEnd}}", template);
            Assert.Contains("{{ButtonBlockStart}}", template);
            Assert.Contains("{{ButtonBlockEnd}}", template);
        }

        [Fact]
        public void VoicemailEmailTemplateIsEmbedded()
        {
            var names = typeof(Techie.Pbx.Core.Data.Database).Assembly.GetManifestResourceNames();
            Assert.Contains(VoicemailEmailRenderer.ResourceName, names);
        }

        [Fact]
        public void VoicemailEmailTemplateCarriesThePlaceholders()
        {
            var template = ReadTemplate(VoicemailEmailRenderer.ResourceName);

            foreach (var placeholder in new[]
                     {
                         "{{Subject}}", "{{Hostname}}", "{{Caller}}", "{{Received}}",
                         "{{Duration}}", "{{Mailbox}}", "{{MailboxName}}", "{{Transcript}}",
                     })
                Assert.Contains(placeholder, template);
        }

        /// <summary>
        /// The transcript and the attachment line are both optional, and both are paired markers
        /// the renderer removes whole when there is nothing to say (D129).
        /// </summary>
        [Fact]
        public void VoicemailEmailTemplateDropsItsOptionalBlocksCleanly()
        {
            var template = ReadTemplate(VoicemailEmailRenderer.ResourceName);

            Assert.Contains("{{TranscriptBlockStart}}", template);
            Assert.Contains("{{TranscriptBlockEnd}}", template);
            Assert.Contains("{{AttachmentBlockStart}}", template);
            Assert.Contains("{{AttachmentBlockEnd}}", template);
        }
    }
}
