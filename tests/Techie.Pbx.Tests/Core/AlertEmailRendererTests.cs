using Techie.Pbx.Core.Mail;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Filling in the D114 template. A pure function like the conf renderers, so these are the same
    /// kind of tests: data in, text out, and the things that must never appear in the output.
    /// </summary>
    public class AlertEmailRendererTests
    {
        [Fact]
        public void Nothing_is_left_unfilled()
        {
            var html = AlertEmailRenderer.Render(Alert());

            // Any {{Placeholder}} still in the output is one the renderer forgot.
            Assert.DoesNotContain("{{", html);
        }

        [Fact]
        public void The_values_it_was_given_are_in_the_output()
        {
            var html = AlertEmailRenderer.Render(Alert());

            Assert.Contains("A test subject", html);
            Assert.Contains("first line", html);
            Assert.Contains("pbx-lab", html);
            Assert.Contains(AlertEmail.AccentInfo, html);
        }

        /// <summary>
        /// The values are not ours, the template is. A subject with markup in it must arrive as
        /// text, the same idea ConfText.Safe applies to conf files.
        /// </summary>
        [Fact]
        public void Everything_it_is_given_is_html_encoded()
        {
            var alert = Alert();
            alert.Subject = "<script>alert('x')</script>";
            alert.Body = new List<string> { "a & b <tag>" };

            var html = AlertEmailRenderer.Render(alert);

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("a &amp; b &lt;tag&gt;", html);
        }

        [Fact]
        public void An_alert_with_no_details_carries_no_detail_table()
        {
            var alert = Alert();
            alert.Details.Clear();

            var html = AlertEmailRenderer.Render(alert);

            Assert.DoesNotContain("{{DetailBlockStart}}", html);
            Assert.DoesNotContain("{{DetailRows}}", html);

            // The row markup from the kept block is gone with it.
            Assert.DoesNotContain("white-space:nowrap", html);
        }

        [Fact]
        public void An_alert_with_details_keeps_the_table_and_loses_the_markers()
        {
            var html = AlertEmailRenderer.Render(Alert());

            Assert.DoesNotContain("{{DetailBlockStart}}", html);
            Assert.DoesNotContain("{{DetailBlockEnd}}", html);
            Assert.Contains("Extension", html);
            Assert.Contains("201", html);
        }

        /// <summary>A button needs both halves; one without the other is no button at all.</summary>
        [Theory]
        [InlineData("", "")]
        [InlineData("https://pbx.example.com/", "")]
        [InlineData("", "Open it")]
        public void A_button_without_both_halves_is_dropped(string url, string text)
        {
            var alert = Alert();
            alert.ButtonText = text;
            alert.ButtonUrl = url;

            var html = AlertEmailRenderer.Render(alert);

            Assert.DoesNotContain("{{Button", html);
            Assert.DoesNotContain("text-decoration:none", html);
        }

        [Fact]
        public void A_button_with_both_halves_is_kept()
        {
            var alert = Alert();
            alert.ButtonText = "Open it";
            alert.ButtonUrl = "https://pbx.example.com/";

            var html = AlertEmailRenderer.Render(alert);

            Assert.Contains("https://pbx.example.com/", html);
            Assert.Contains("Open it", html);
        }

        private static AlertEmail Alert() => new()
        {
            AccentColor = AlertEmail.AccentInfo,
            Body = new List<string> { "first line", "", "second line" },
            Details = { new AlertEmailDetail("Extension", "201") },
            Hostname = "pbx-lab",
            Subject = "A test subject",
            Timestamp = "Friday 19 September 2026, 10:00:00 +01:00",
        };
    }
}
