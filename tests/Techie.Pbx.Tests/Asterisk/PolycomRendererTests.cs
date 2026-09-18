using Techie.Pbx.Asterisk.Provisioning;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The two files a Polycom phone fetches, pinned to golden files the way the Asterisk conf
    /// renderers are. These are the files a phone reads directly, so a change to any of them is a
    /// change every phone on a site picks up at its next poll: an expected file that has to be
    /// updated is the point at which somebody has to say why (D79).
    /// </summary>
    public class PolycomRendererTests
    {
        private const string Mac = "0004f2aabbcc";

        private static Extension SampleExtension() => new()
        {
            ExtensionID = 7,
            Number = "1001",
            Name = "Front Desk",
            Secret = "AAAAbbbbCCCCdddd1111",
        };

        private static PolycomConfig SampleConfig() => new()
        {
            AdminPassword = "AdminPass123",
            Extension = SampleExtension(),
            GmtOffsetSeconds = -25200,
            Phone = SamplePhone(),
            ServerAddress = "10.8.20.4",
            SipPort = 5060,
            SntpAddress = "pool.ntp.org",
            UserPassword = "UserPass123",
        };

        private static Phone SamplePhone() => new()
        {
            Firmware = "5.9.5.0614",
            LastConfig = "2026-09-17 09:31:02Z",
            LastIP = "10.8.20.31",
            Mac = Mac,
            Model = "VVX_410",
            Name = "Reception",
            PhoneID = 1,
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void The_master_file_matches_expected_file()
        {
            Assert.Equal(Expected("polycom-master.cfg"), PolycomMasterRenderer.Render(Mac));
        }

        /// <summary>The master file names the config file, which is how the phone finds it.</summary>
        [Fact]
        public void The_master_file_points_at_this_phones_config()
        {
            Assert.Contains($"CONFIG_FILES=\"exten{Mac}.cfg\"", PolycomMasterRenderer.Render(Mac));
        }

        [Theory]
        [InlineData("0004F2AABBCC")]
        [InlineData("00:04:f2:aa:bb:cc")]
        [InlineData("../../etc/passwd")]
        public void The_master_renderer_refuses_anything_that_is_not_a_mac(string mac)
        {
            Assert.Throws<InvalidOperationException>(() => PolycomMasterRenderer.Render(mac));
        }

        [Fact]
        public void A_phone_with_an_extension_matches_expected_file()
        {
            Assert.Equal(Expected("polycom-phone.cfg"), PolycomConfigRenderer.Render(SampleConfig()));
        }

        /// <summary>
        /// A phone nobody has assigned yet still gets a valid file — the time and a poll — so it
        /// can sit on a desk and start working when an extension is given to it (D78).
        /// </summary>
        [Fact]
        public void A_phone_with_no_extension_matches_expected_file()
        {
            var config = SampleConfig();
            config.Extension = null;
            config.Phone.Name = "";
            config.Phone.PhoneID = 2;

            Assert.Equal(Expected("polycom-phone-unassigned.cfg"), PolycomConfigRenderer.Render(config));
        }

        /// <summary>The thing that must never be in an unassigned phone's file: a credential.</summary>
        [Fact]
        public void A_phone_with_no_extension_is_given_no_registration()
        {
            var config = SampleConfig();
            config.Extension = null;

            var actual = PolycomConfigRenderer.Render(config);

            Assert.DoesNotContain("reg.1.", actual);
            Assert.DoesNotContain("auth.password", actual);
            Assert.DoesNotContain(SampleExtension().Secret, actual);
        }

        /// <summary>
        /// The port is derived from the PhoneID, so two phones behind one NAT are told to use
        /// different source ports (D81).
        /// </summary>
        [Fact]
        public void Each_phone_is_told_a_local_sip_port_of_its_own()
        {
            var first = SampleConfig();
            var second = SampleConfig();
            second.Phone.PhoneID = 2;

            Assert.Contains("voIpProt.SIP.local.port=\"1025\"", PolycomConfigRenderer.Render(first));
            Assert.Contains("voIpProt.SIP.local.port=\"1026\"", PolycomConfigRenderer.Render(second));
        }

        /// <summary>
        /// A name with an ampersand in it is one our own rules allow on an extension, and it would
        /// make the file unparseable to the phone if it were written as typed.
        /// </summary>
        [Fact]
        public void A_name_is_escaped_for_xml()
        {
            var config = SampleConfig();
            config.Extension!.Name = "Sales & Support";

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("reg.1.displayName=\"Sales &amp; Support\"", actual);
        }

        /// <summary>
        /// The two Polycom device account passwords, written whether or not the phone has an
        /// extension: they are phone-level, not registration-level (D84).
        /// </summary>
        [Fact]
        public void Device_web_passwords_are_written_when_set()
        {
            var actual = PolycomConfigRenderer.Render(SampleConfig());

            Assert.Contains("device.auth.localAdminPassword=\"AdminPass123\"", actual);
            Assert.Contains("device.auth.localUserPassword=\"UserPass123\"", actual);
        }

        /// <summary>
        /// Each password is independent: a site that has only set one of the two Polycom accounts
        /// does not get the other one written as an empty, and worse, working, password.
        /// </summary>
        [Fact]
        public void Only_the_device_password_that_is_set_is_written()
        {
            var config = SampleConfig();
            config.UserPassword = "";

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("device.auth.localAdminPassword=", actual);
            Assert.DoesNotContain("device.auth.localUserPassword=", actual);
        }

        /// <summary>
        /// Neither Polycom device password is a required setting, so a site that has not set
        /// either gets no <c>device</c> element at all rather than one with blank passwords.
        /// </summary>
        [Fact]
        public void Neither_device_password_set_means_no_device_element()
        {
            var config = SampleConfig();
            config.AdminPassword = "";
            config.UserPassword = "";

            Assert.DoesNotContain("device.auth", PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// The renderer re-validates rather than trusting that whatever loaded these rows checked
        /// them, exactly as the conf renderers do.
        /// </summary>
        [Fact]
        public void An_invalid_phone_is_refused_rather_than_rendered()
        {
            var config = SampleConfig();
            config.Phone.Mac = "not-a-mac";

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        [Fact]
        public void An_invalid_extension_is_refused_rather_than_rendered()
        {
            var config = SampleConfig();
            config.Extension!.Secret = "short";

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// The offset is worked out from the zone at the moment of generation, so it is a number of
        /// seconds with no notion of daylight saving in it (D82).
        /// </summary>
        [Fact]
        public void The_gmt_offset_comes_from_the_timezone_setting()
        {
            Assert.Equal(0, PolycomConfig.GmtOffsetFor("Etc/UTC"));

            // A zone this machine cannot name falls back to UTC rather than throwing.
            Assert.Equal(0, PolycomConfig.GmtOffsetFor("Mars/Olympus_Mons"));
        }

        /// <summary>
        /// A western zone must render as a negative number of seconds, not a magnitude with the
        /// sign lost somewhere in the cast to int (D84). Not pinned to -28800 alone: Los Angeles is
        /// -28800 (PST) or -25200 (PDT) depending on when the test runs, and pinning to one would
        /// make this fail for two weeks every spring and autumn.
        /// </summary>
        [Fact]
        public void The_gmt_offset_is_negative_for_a_western_zone()
        {
            var offset = PolycomConfig.GmtOffsetFor("America/Los_Angeles");

            Assert.True(offset is -28800 or -25200, $"Expected the Pacific standard or daylight offset, got {offset}.");
        }

        [Theory]
        [InlineData("0004f2aabbcc.cfg", "0004f2aabbcc")]
        [InlineData("aabbccddeeff.cfg", "aabbccddeeff")]
        public void A_master_file_name_is_read_back_as_a_mac(string fileName, string mac)
        {
            Assert.True(PolycomFiles.TryParseMaster(fileName, out var parsed));
            Assert.Equal(mac, parsed);
            Assert.False(PolycomFiles.TryParseConfig(fileName, out _));
        }

        [Fact]
        public void A_config_file_name_is_read_back_as_a_mac()
        {
            Assert.True(PolycomFiles.TryParseConfig("exten0004f2aabbcc.cfg", out var parsed));
            Assert.Equal(Mac, parsed);

            // And is not also read as a master file, or one request would mean two things.
            Assert.False(PolycomFiles.TryParseMaster("exten0004f2aabbcc.cfg", out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("000000000000.txt")]
        [InlineData("0004F2AABBCC.cfg")]
        [InlineData("0004f2aabbc.cfg")]
        [InlineData("../../../etc/passwd")]
        [InlineData("exten.cfg")]
        [InlineData("sip.ld")]
        [InlineData("0004f2aabbcc-phone.cfg")]
        public void Anything_else_is_not_a_file_we_generate(string? fileName)
        {
            Assert.False(PolycomFiles.TryParseMaster(fileName, out _));
            Assert.False(PolycomFiles.TryParseConfig(fileName, out _));
        }
    }
}
