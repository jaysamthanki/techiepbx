using System.Reflection;
using Xunit;

namespace Techie.Pbx.Tests
{
    /// <summary>
    /// The email alert template ships as an embedded resource (D114): one HTML file under
    /// Templates/, embedded as templates/AlertEmail.html the same way schema scripts are
    /// embedded as schema/NNN_name.sql. This keeps the check small: the resource is there,
    /// and it carries the placeholders the mail sender will fill in.
    /// </summary>
    public class EmailTemplateTests
    {
        private static string ReadTemplate()
        {
            var assembly = typeof(Techie.Pbx.Core.Data.Database).Assembly;
            using var stream = assembly.GetManifestResourceStream("templates/AlertEmail.html");
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
    }
}
