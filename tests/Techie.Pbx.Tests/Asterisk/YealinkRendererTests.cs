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

        /// <summary>The extensions a key can name, which is where the label on it comes from.</summary>
        private static List<Extension> SampleButtonExtensions() => new()
        {
            SampleExtension(),
            new Extension { ExtensionID = 8, Number = "1002", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222" },
        };

        /// <summary>
        /// Two keys assigned out of eight — one extension, one parking slot — and the other six
        /// left alone, which is what a real phone looks like (D121).
        /// </summary>
        private static List<PhoneButton> SampleButtons() => new()
        {
            new PhoneButton { Position = 1, TargetType = PhoneButtonTarget.Extension, TargetValue = "1002" },
            new PhoneButton { Position = 4, TargetType = PhoneButtonTarget.ParkingSlot, TargetValue = "3" },
        };

        private static YealinkConfig SampleConfig() => new()
        {
            Codecs = new List<string> { "ulaw", "alaw", "gsm" },
            Extension = SampleExtension(),
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
        /// can sit on a desk and start working when an extension is given to it (D78, D88).
        /// </summary>
        [Fact]
        public void A_phone_with_no_extension_matches_expected_file()
        {
            var config = SampleConfig();
            config.Extension = null;

            Assert.Equal(Expected("yealink-phone-unassigned.cfg"), YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// A phone with keys on it (D121). Unlike Polycom's resource list, the keys keep their own
        /// numbers — key 4 is <c>linekey.4</c> — because a Yealink line key is addressed by the key
        /// itself, so the gap left by keys 2 and 3 hides nothing.
        /// </summary>
        [Fact]
        public void A_phone_with_keys_matches_expected_file()
        {
            var config = SampleConfig();
            config.ButtonExtensions = SampleButtonExtensions();
            config.Buttons = SampleButtons();

            Assert.Equal(Expected("yealink-phone-buttons.cfg"), YealinkConfigRenderer.Render(config));
        }

        /// <summary>
        /// A phone nobody has assigned a key on gets no linekey parameters at all, so every key
        /// keeps the default behaviour the handset gives it.
        /// </summary>
        [Fact]
        public void A_phone_with_no_keys_is_given_no_line_keys()
        {
            Assert.DoesNotContain("linekey.", YealinkConfigRenderer.Render(SampleConfig()));
        }

        /// <summary>
        /// The label on an extension key is the extension's own name, so renaming an extension
        /// relabels every key that watches it at the next poll. An extension the renderer was not
        /// given falls back to the number rather than an empty key.
        /// </summary>
        [Fact]
        public void An_extension_key_is_labelled_with_the_extensions_name()
        {
            var config = SampleConfig();
            config.ButtonExtensions = SampleButtonExtensions();
            config.Buttons = SampleButtons();

            Assert.Contains("linekey.1.label = Sales", YealinkConfigRenderer.Render(config));

            config.ButtonExtensions = new List<Extension>();
            Assert.Contains("linekey.1.label = 1002", YealinkConfigRenderer.Render(config));
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
                new() { Position = 1, TargetType = PhoneButtonTarget.Extension, TargetValue = "not-a-number" },
            };

            Assert.Throws<InvalidOperationException>(() => YealinkConfigRenderer.Render(config));
        }

        /// <summary>The thing that must never be in an unassigned phone's file: a credential.</summary>
        [Fact]
        public void A_phone_with_no_extension_is_given_no_registration()
        {
            var config = SampleConfig();
            config.Extension = null;

            var actual = YealinkConfigRenderer.Render(config);

            Assert.DoesNotContain("account.1.", actual);
            Assert.DoesNotContain(SampleExtension().Secret, actual);
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
            config.Extension!.Secret = "short";

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
            config.Extension!.Name = "Sales & Support";

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

        /// <summary>Unused accounts are explicitly disabled rather than left to whatever the phone defaults to.</summary>
        [Fact]
        public void Accounts_two_through_six_are_disabled()
        {
            var actual = YealinkConfigRenderer.Render(SampleConfig());

            for (var account = 2; account <= 6; account++)
                Assert.Contains($"account.{account}.enable = 0", actual);
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
