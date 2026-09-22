using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// pjsip_notify.conf (D91, D123). Fully static content, but pinned to a golden file like the
    /// rest: what is in here is what a desk phone is asked to do, and a change to it should be a
    /// change somebody had to explain.
    /// </summary>
    public class NotifyConfRendererTests
    {
        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void It_matches_expected_file()
        {
            Assert.Equal(Expected("pjsip_notify.conf"), NotifyConfRenderer.Render());
        }

        /// <summary>
        /// Both NOTIFYs have an empty body, and a phone sent one without a Content-Length is
        /// entitled to wait for a body that never comes.
        /// </summary>
        [Fact]
        public void Every_type_says_it_has_no_body()
        {
            var lines = NotifyConfRenderer.Render()
                .Split('\n')
                .Where(line => line.StartsWith('['))
                .ToList();

            Assert.Equal(new[] { $"[{NotifyConfRenderer.PolycomReboot}]", $"[{NotifyConfRenderer.YealinkReboot}]" }, lines);
            Assert.Equal(lines.Count, NotifyConfRenderer.Render().Split("Content-Length = 0\n").Length - 1);
        }

        /// <summary>
        /// The Yealink type reboots, as its name says (D134): a Yealink phone reads the
        /// <c>reboot=</c> parameter, and the web passwords it is given only apply at boot.
        /// </summary>
        [Fact]
        public void The_yealink_type_reboots_the_phone()
        {
            var actual = NotifyConfRenderer.Render();

            Assert.Contains($"[{NotifyConfRenderer.YealinkReboot}]\nEvent = check-sync;reboot=true\n", actual);
            Assert.DoesNotContain("reboot=false", actual);
        }

        /// <summary>
        /// Asterisk 22's res_pjsip_notify refuses the whole file if it has a [general] section —
        /// found on the lab VM, and the reason there is a test for the absence of something.
        /// </summary>
        [Fact]
        public void There_is_no_general_section()
        {
            Assert.DoesNotContain("[general]", NotifyConfRenderer.Render());
        }
    }
}
