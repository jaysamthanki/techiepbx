using Techie.Pbx.Asterisk.Provisioning;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The two files a Yealink phone fetches, pinned to golden files the way the Polycom
    /// renderers and the Asterisk conf renderers are (D88). These are the files a phone reads
    /// directly, so a change to any of them is a change every Yealink phone on a site picks up at
    /// its next poll: an expected file that has to be updated is the point at which somebody has
    /// to say why (mirrors D79).
    /// </summary>
    public class YealinkRendererTests
    {
        private const string Mac = "249ad81e83fa";

        private static Extension SampleExtension() => new()
        {
            ExtensionID = 7,
            Number = "1001",
            Name = "Front Desk",
            Secret = "AAAAbbbbCCCCdddd1111",
        };

        /// <summary>The extensions a key can name: the one it registers as, and one to watch.</summary>
        private static List<Extension> SampleExtensions() => new()
        {
            SampleExtension(),
            new Extension { ExtensionID = 8, Number = "1002", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222" },
        };

        /// <summary>
        /// The line, and two lamps out of the remaining seven keys — one extension, one parking
        /// slot — with the rest left alone, which is what a real phone looks like (D121).
        /// </summary>
        private static List<PhoneButton> SampleButtons() => new()
        {
            SampleLine(),
            new PhoneButton { Position = 2, TargetType = PhoneButtonTarget.Blf, TargetValue = "1002" },
            new PhoneButton { Position = 4, TargetType = PhoneButtonTarget.ParkingSlot, TargetValue = "3" },
        };

        /// <summary>Key 1: the extension this phone registers as (schema 020).</summary>
        private static PhoneButton SampleLine() =>
            new() { Position = 1, TargetType = PhoneButtonTarget.Line, TargetValue = "1001" };

        /// <summary>A phone that registers and has no other key on it.</summary>
        private static YealinkConfig SampleConfig() => new()
        {
            Buttons = new List<PhoneButton> { SampleLine() },
            Codecs = new List<string> { "ulaw", "alaw", "gsm" },
            Extensions = SampleExtensions(),
            NtpServer = "pool.ntp.org",
            Phone = SamplePhone(),
            ProvisioningPassword = "phonepass123",
            ProvisioningUrl = "http://10.8.20.4:8080/yealink",
            ProvisioningUsername = "phones",
            ServerAddress = "10.8.20.4",
            SipPort = 5060,

            // Pinned rather than computed, so this test does not drift with DST (mirrors how the
            // Polycom golden tests pin GmtOffsetSeconds instead of calling GmtOffsetFor).
            TimeZoneOffset = "-7",
        };

        private static Phone SamplePhone() => new()
        {
            Brand = PhoneBrand.Yealink,
            Firmware = "124.86.0.118",
            LastConfig = "2026-09-17 09:31:02Z",
            LastIP = "10.8.20.31",
            Mac = Mac,
            Model = "T33G",
            Name = "Reception",
            PhoneID = 1,
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void The_boot_file_matches_expected_file()
        {
            Assert.Equal(Expected("yealink-boot.cfg"), YealinkMasterRenderer.Render(Mac));
        }

        /// <summary>The first line of every generated file is the literal cookie or the phone refuses it.</summary>
        [Fact]
        public void Every_generated_file_starts_with_the_version_cookie()
        {
            Assert.StartsWith("#!version:1.0.0.1\n", YealinkMasterRenderer.Render(Mac));
            Assert.StartsWith("#!version:1.0.0.1\n", YealinkConfigRenderer.Render(SampleConfig()));
        }

        /// <summary>The boot file names the one config file the phone should include next.</summary>
        [Fact]
        public void The_boot_file_points_at_this_phones_config()
        {
            Assert.Contains($"include:config \"{Mac}.cfg\"", YealinkMasterRenderer.Render(Mac));
        }

        [Theory]
        [InlineData("249AD81E83FA")]
        [InlineData("24:9a:d8:1e:83:fa")]
        [InlineData("../../etc/passwd")]
        public void The_boot_renderer_refuses_anything_that_is_not_a_mac(string mac)
        {
            Assert.Throws<InvalidOperationException>(() => YealinkMasterRenderer.Render(mac));
        }

        [Fact]
        public void A_phone_with_an_extension_matches_expected_file()
        {
            Assert.Equal(Expected("yealink-phone.cfg"), YealinkConfigRenderer.Render(SampleConfig()));
        }

        /// <summary>
        /// A phone nobody has assigned yet still gets a valid file — the time and a poll — so it
        /// can sit on a desk and start working when an extension is given to it (D78, D88). No keys
        /// is what "nobody has assigned it" means now (schema 020).
        /// </summary>
        [Fact]
        public void A_phone_with_no_extension_matches_expected_file()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>();

            Assert.Equal(Expected("yealink-phone-unassigned.cfg"), YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// A phone with keys on it (D121). Unlike Polycom's resource list, the keys keep their own
        /// numbers — key 4 is <c>linekey.4</c> — because a Yealink line key is addressed by the key
        /// itself, so the gap left by key 3 hides nothing.
        /// </summary>
        [Fact]
        public void A_phone_with_keys_matches_expected_file()
        {
            var config = SampleConfig();
            config.Buttons = SampleButtons();

            Assert.Equal(Expected("yealink-phone-buttons.cfg"), YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// The keys a phone is given are the keys the admin put them on, gaps and all (D121
        /// amended again). Nothing at all is written for a blank key, which is the right answer
        /// here for the reason the golden file's key 3 shows: a Yealink line key is addressed by
        /// its own number, so an unwritten key is a key the handset leaves at its default rather
        /// than a key that shuffles the rest along. This is the same fix Polycom needed and got
        /// differently, because Polycom's resource list is read until the first missing index.
        /// </summary>
        [Fact]
        public void A_blank_key_keeps_the_keys_after_it_where_they_are()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>
            {
                new() { Position = 1, TargetType = PhoneButtonTarget.Line, TargetValue = "1001" },
                new() { Position = 6, TargetType = PhoneButtonTarget.Blf, TargetValue = "1002" },
            };

            var actual = YealinkConfigRenderer.Render(config);

            Assert.Contains("linekey.6.value = 1002\n", actual);
            Assert.Contains("linekey.6.type = 16\n", actual);

            foreach (var blank in new[] { 2, 3, 4, 5, 7, 8 })
                Assert.DoesNotContain($"linekey.{blank}.", actual);
        }

        /// <summary>
        /// Yealink is given no dial plan of its own: no <c>dialplan.</c> and no <c>dialnow</c>
        /// rules, so the handset dials on its own Send key and its own timers. The digit map that
        /// had to be redesigned is Polycom's (D124), and there is nothing here to redesign — a
        /// Yealink dial-now rule set is a piece to justify on its own, and until it exists this
        /// test is what says the file really carries none.
        /// </summary>
        [Fact]
        public void No_dial_plan_is_written_for_a_yealink()
        {
            var config = SampleConfig();
            config.Buttons = SampleButtons();

            var actual = YealinkConfigRenderer.Render(config);

            Assert.DoesNotContain("dialplan", actual);
            Assert.DoesNotContain("dialnow", actual);
        }

        /// <summary>
        /// Key 1 is the line the phone registers on and takes no value; the rest are BLFs on
        /// account 1. The two types have to differ or the handset shows a row of lines where it
        /// was meant to show one line and some lamps (schema 020).
        /// </summary>
        [Fact]
        public void The_line_key_and_the_lamps_are_different_kinds_of_key()
        {
            var config = SampleConfig();
            config.Buttons = SampleButtons();

            var actual = YealinkConfigRenderer.Render(config);

            Assert.Contains("linekey.1.type = 15\n", actual);
            Assert.DoesNotContain("linekey.1.value", actual);
            Assert.Contains("linekey.2.value = 1002\n", actual);
            Assert.Contains("linekey.2.type = 16\n", actual);
        }

        /// <summary>
        /// A phone with only its line gets one linekey parameter set, so every other key keeps the
        /// default behaviour the handset gives it.
        /// </summary>
        [Fact]
        public void A_phone_with_no_lamps_is_given_only_its_line_key()
        {
            var actual = YealinkConfigRenderer.Render(SampleConfig());

            Assert.Contains("linekey.1.line = 1\n", actual);
            Assert.DoesNotContain("linekey.2.", actual);
        }

        /// <summary>
        /// The label on a lamp is the extension's own name, so renaming an extension relabels every
        /// key that watches it at the next poll. An extension the renderer was not given falls back
        /// to the number rather than an empty key.
        /// </summary>
        [Fact]
        public void An_extension_key_is_labelled_with_the_extensions_name()
        {
            var config = SampleConfig();
            config.Buttons = SampleButtons();

            Assert.Contains("linekey.2.label = Sales", YealinkConfigRenderer.Render(config));

            // The line still has to be there, or there would be nothing to register with; only the
            // extension the lamp names is taken away.
            config.Extensions = new List<Extension> { SampleExtension() };
            Assert.Contains("linekey.2.label = 1002", YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// The renderer re-validates the keys it is given, as it does the phone and the extension:
        /// a row that reached it another way must not become a key that dials somewhere else.
        /// </summary>
        [Fact]
        public void An_invalid_key_is_refused_rather_than_rendered()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>
            {
                SampleLine(),
                new() { Position = 2, TargetType = PhoneButtonTarget.Blf, TargetValue = "not-a-number" },
            };

            Assert.Throws<InvalidOperationException>(() => YealinkConfigRenderer.Render(config));
        }

        /// <summary>The thing that must never be in an unassigned phone's file: a credential.</summary>
        [Fact]
        public void A_phone_with_no_extension_is_given_no_registration()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>();

            var actual = YealinkConfigRenderer.Render(config);

            Assert.DoesNotContain("account.1.enable = 1", actual);
            Assert.DoesNotContain("linekey.", actual);
            Assert.DoesNotContain(SampleExtension().Secret, actual);
        }

        /// <summary>
        /// A line naming an extension the renderer was not given is refused rather than written as
        /// a registration with no password: the caller filters the keys before it gets here
        /// (<c>PhoneButton.Usable</c>), so this can only be a bug on our side.
        /// </summary>
        [Fact]
        public void A_line_whose_extension_is_missing_is_refused()
        {
            var config = SampleConfig();
            config.Extensions = new List<Extension>();

            Assert.Throws<InvalidOperationException>(() => YealinkConfigRenderer.Render(config));
        }

        [Fact]
        public void An_invalid_phone_is_refused_rather_than_rendered()
        {
            var config = SampleConfig();
            config.Phone.Mac = "not-a-mac";

            Assert.Throws<InvalidOperationException>(() => YealinkConfigRenderer.Render(config));
        }

        [Fact]
        public void An_invalid_extension_is_refused_rather_than_rendered()
        {
            var config = SampleConfig();
            config.Extensions[0].Secret = "short";

            Assert.Throws<InvalidOperationException>(() => YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// The provisioning username and password are only written when set — the same
        /// "unset means left out, not blank" rule Polycom's device passwords follow (D84).
        /// </summary>
        [Fact]
        public void Provisioning_credentials_are_written_when_set()
        {
            var actual = YealinkConfigRenderer.Render(SampleConfig());

            Assert.Contains("auto_provision.server.username = phones", actual);
            Assert.Contains("auto_provision.server.password = phonepass123", actual);
        }

        [Fact]
        public void Only_the_provisioning_credential_that_is_set_is_written()
        {
            var config = SampleConfig();
            config.ProvisioningPassword = "";

            var actual = YealinkConfigRenderer.Render(config);

            Assert.Contains("auto_provision.server.username =", actual);
            Assert.DoesNotContain("auto_provision.server.password =", actual);
        }

        /// <summary>
        /// A name with an ampersand in it is one our own rules allow on an extension, and
        /// ConfText.Safe has to let it through unescaped: this is not XML, it is a plain
        /// key = value line.
        /// </summary>
        [Fact]
        public void A_name_with_special_characters_is_written_through_conf_text_safe()
        {
            var config = SampleConfig();
            config.Extensions[0].Name = "Sales & Support";

            var actual = YealinkConfigRenderer.Render(config);

            Assert.Contains("account.1.label = Sales & Support", actual);
        }

        /// <summary>A value ConfText.Safe refuses (a control character smuggled into a name) must not reach the file.</summary>
        [Fact]
        public void An_unsafe_value_is_refused_rather_than_written()
        {
            var config = SampleConfig();
            config.ProvisioningUrl = "http://host/yealink\nExtra: injected";

            Assert.Throws<InvalidOperationException>(() => YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// Every account above the phone's lines is explicitly disabled rather than left to
        /// whatever the phone defaults to — including account 1 on a phone with no line at all.
        /// </summary>
        [Fact]
        public void Every_account_above_the_lines_is_disabled()
        {
            var actual = YealinkConfigRenderer.Render(SampleConfig());

            for (var account = 2; account <= 6; account++)
                Assert.Contains($"account.{account}.enable = 0", actual);

            var unassigned = SampleConfig();
            unassigned.Buttons = new List<PhoneButton>();

            Assert.Contains("account.1.enable = 0", YealinkConfigRenderer.Render(unassigned));
        }

        [Theory]
        [InlineData("249ad81e83fa.cfg", "249ad81e83fa")]
        [InlineData("aabbccddeeff.cfg", "aabbccddeeff")]
        public void A_config_file_name_is_read_back_as_a_mac(string fileName, string mac)
        {
            Assert.True(YealinkFiles.TryParseConfig(fileName, out var parsed));
            Assert.Equal(mac, parsed);
        }

        [Fact]
        public void The_boot_file_name_is_recognised()
        {
            Assert.True(YealinkFiles.IsBootFile(YealinkFiles.BootFileName));
            Assert.False(YealinkFiles.TryParseConfig(YealinkFiles.BootFileName, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("000000000000.txt")]
        [InlineData("249AD81E83FA.cfg")]
        [InlineData("249ad81e83f.cfg")]
        [InlineData("../../../etc/passwd")]
        [InlineData("y000000000000.boot.cfg")]
        [InlineData("249ad81e83fa-phone.cfg")]
        public void Anything_else_is_not_a_file_we_generate(string? fileName)
        {
            Assert.False(YealinkFiles.IsBootFile(fileName));
            Assert.False(YealinkFiles.TryParseConfig(fileName, out _));
        }

        /// <summary>
        /// Not verified against a real handset (D92): a best-effort reading of Yealink's own
        /// parameter table for local_time.time_zone, hours east of UTC rather than the seconds
        /// Polycom takes (D82). Not pinned to one exact value: Los Angeles is -8 (PST) or -7 (PDT)
        /// depending on when the test runs, and pinning to one would make this fail every spring
        /// and autumn.
        /// </summary>
        [Fact]
        public void The_time_zone_offset_is_negative_for_a_western_zone()
        {
            var offset = YealinkConfig.TimeZoneOffsetFor("America/Los_Angeles");

            Assert.True(offset is "-8" or "-7", $"Expected the Pacific standard or daylight offset, got {offset}.");
        }

        [Fact]
        public void The_time_zone_offset_falls_back_to_zero()
        {
            Assert.Equal("0", YealinkConfig.TimeZoneOffsetFor("Etc/UTC"));

            // A zone this machine cannot name falls back to "0" rather than throwing.
            Assert.Equal("0", YealinkConfig.TimeZoneOffsetFor("Mars/Olympus_Mons"));
        }
    }
}
