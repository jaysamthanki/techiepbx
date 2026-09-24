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

        /// <summary>
        /// A phone that registers and has no other key on it, on a site with parking switched off:
        /// so its one on-call soft key is Blind Xfer (D144). The parking-on case is the keyed
        /// phone, which has a parking slot lamp and so wants the Park key to go with it.
        /// </summary>
        private static PolycomConfig SampleConfig() => new()
        {
            AdminPassword = "AdminPass123",
            Buttons = new List<PhoneButton> { SampleLine() },
            Extensions = SampleExtensions(),
            // The standard offset, as GmtOffsetFor would produce it for this zone (D150).
            GmtOffsetSeconds = -28800,
            ParkDtmfCode = "*3",
            ParkEnabled = false,
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
        /// can sit on a desk and start working when an extension is given to it (D78). No keys is
        /// what "nobody has assigned it" means now (schema 020).
        /// </summary>
        [Fact]
        public void A_phone_with_no_extension_matches_expected_file()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>();
            config.Phone.Name = "";
            config.Phone.PhoneID = 2;

            Assert.Equal(Expected("polycom-phone-unassigned.cfg"), PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// A site with a background image (D145, D151): the same file as a phone with no image,
        /// plus the <c>bg</c> element — enabled, custom selection 1, and the gated URL.
        /// </summary>
        [Fact]
        public void A_phone_on_a_site_with_a_background_matches_expected_file()
        {
            var config = SampleConfig();
            config.BackgroundUrl = "http://pbx.example.com/polycom/background.png";

            Assert.Equal(Expected("polycom-phone-background.cfg"), PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// No image means no <c>bg</c> element at all (D145): the file is byte for byte what it
        /// was before the feature existed, which the unchanged golden files also pin.
        /// </summary>
        [Fact]
        public void No_background_means_no_bg_element()
        {
            var actual = PolycomConfigRenderer.Render(SampleConfig());

            Assert.DoesNotContain("<bg", actual);
            Assert.DoesNotContain("bg.", actual);
            Assert.Equal(Expected("polycom-phone.cfg"), actual);
        }

        /// <summary>
        /// The background is phone-level like the time, so a phone nobody has assigned yet shows
        /// the site's image while it waits for an extension.
        /// </summary>
        [Fact]
        public void An_unassigned_phone_still_gets_the_background()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>();
            config.BackgroundUrl = "https://pbx.example.com/polycom/background.jpg";

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("bg.color.bm.1.name=\"https://pbx.example.com/polycom/background.jpg\"\n", actual);
            Assert.DoesNotContain("reg.1.", actual);
        }

        /// <summary>The URL is re-checked like any other value a renderer is handed: absolute http or https only.</summary>
        [Theory]
        [InlineData("background.png")]
        [InlineData("/polycom/background.png")]
        [InlineData("ftp://pbx.example.com/polycom/background.png")]
        [InlineData("file:///etc/passwd")]
        [InlineData("http://pbx.example.com/polycom/background.png\"/><x a=\"")]
        public void A_background_url_that_is_not_absolute_http_is_refused(string url)
        {
            var config = SampleConfig();
            config.BackgroundUrl = url;

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        [Theory]
        [InlineData("background.png", BackgroundImageFormat.Png)]
        [InlineData("background.jpg", BackgroundImageFormat.Jpeg)]
        public void A_background_file_name_is_read_back_as_its_format(string fileName, BackgroundImageFormat format)
        {
            Assert.True(PolycomFiles.TryParseBackground(fileName, out var parsed));
            Assert.Equal(format, parsed);
            Assert.Equal(fileName, PolycomFiles.BackgroundFileName(format));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("background.jpeg")]
        [InlineData("Background.png")]
        [InlineData("background.gif")]
        [InlineData("../Data/polycom-background.png")]
        [InlineData("polycom-background.png")]
        [InlineData("logo.png")]
        public void Anything_else_is_not_the_background(string? fileName)
        {
            Assert.False(PolycomFiles.TryParseBackground(fileName, out _));
        }

        /// <summary>
        /// A site with a logo and no background (D153): the same file as a phone with neither,
        /// plus a <c>bg</c> element holding only <c>bg.logo</c> — the factory background stays.
        /// </summary>
        [Fact]
        public void A_phone_on_a_site_with_a_logo_matches_expected_file()
        {
            var config = SampleConfig();
            config.LogoUrl = "http://pbx.example.com/polycom/logo.png";

            Assert.Equal(Expected("polycom-phone-logo.cfg"), PolycomConfigRenderer.Render(config));
        }

        /// <summary>Both images: one <c>bg</c> element, the background's three lines and then the logo's.</summary>
        [Fact]
        public void A_phone_on_a_site_with_a_background_and_a_logo_matches_expected_file()
        {
            var config = SampleConfig();
            config.BackgroundUrl = "http://pbx.example.com/polycom/background.png";
            config.LogoUrl = "http://pbx.example.com/polycom/logo.png";

            Assert.Equal(Expected("polycom-phone-background-logo.cfg"), PolycomConfigRenderer.Render(config));
        }

        /// <summary>No logo means no <c>bg.logo</c> line, whether or not there is a background.</summary>
        [Fact]
        public void No_logo_means_no_logo_line()
        {
            var config = SampleConfig();
            config.BackgroundUrl = "http://pbx.example.com/polycom/background.png";

            var actual = PolycomConfigRenderer.Render(config);

            Assert.DoesNotContain("bg.logo", actual);
            Assert.Equal(Expected("polycom-phone-background.cfg"), actual);
        }

        /// <summary>The logo's URL is re-checked exactly as the background's is.</summary>
        [Theory]
        [InlineData("logo.png")]
        [InlineData("/polycom/logo.png")]
        [InlineData("ftp://pbx.example.com/polycom/logo.png")]
        [InlineData("file:///etc/passwd")]
        [InlineData("http://pbx.example.com/polycom/logo.png\"/><x a=\"")]
        public void A_logo_url_that_is_not_absolute_http_is_refused(string url)
        {
            var config = SampleConfig();
            config.LogoUrl = url;

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        [Theory]
        [InlineData("logo.png", BackgroundImageFormat.Png)]
        [InlineData("logo.jpg", BackgroundImageFormat.Jpeg)]
        public void A_logo_file_name_is_read_back_as_its_format(string fileName, BackgroundImageFormat format)
        {
            Assert.True(PolycomFiles.TryParseLogo(fileName, out var parsed));
            Assert.Equal(format, parsed);
            Assert.Equal(fileName, PolycomFiles.LogoFileName(format));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("logo.jpeg")]
        [InlineData("Logo.png")]
        [InlineData("logo.gif")]
        [InlineData("../Data/polycom-logo.png")]
        [InlineData("polycom-logo.png")]
        [InlineData("background.png")]
        public void Anything_else_is_not_the_logo(string? fileName)
        {
            Assert.False(PolycomFiles.TryParseLogo(fileName, out _));
        }

        /// <summary>
        /// A phone with keys on it (D121). Key 1 is the registration and does not appear in the
        /// resource list at all; the lamps on keys 2 and 4 are resources 1 and 3, because the
        /// registration has taken the first line key and a resource lands on the key its index
        /// says. Key 3 was left blank, so resource 2 is written with an empty address — the list
        /// cannot skip it, since a phone reads it until the first index it cannot find.
        /// </summary>
        [Fact]
        public void A_phone_with_keys_matches_expected_file()
        {
            var config = SampleConfig();
            config.Buttons = SampleButtons();
            config.ParkEnabled = true;

            Assert.Equal(Expected("polycom-phone-buttons.cfg"), PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// The Park soft key sends whatever the site's park feature code is (D144): change the
        /// setting and every phone's key follows at its next poll, because the EFK carries the
        /// live value rather than a copy of the default.
        /// </summary>
        [Fact]
        public void The_park_key_sends_the_sites_park_code()
        {
            var config = SampleConfig();
            config.ParkDtmfCode = "*70";
            config.ParkEnabled = true;

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("efk.efkList.1.mname=\"park\"\n", actual);
            Assert.Contains("efk.efkList.1.action.string=\"*70\"\n", actual);
            Assert.Contains("efk.efkList.2.mname=\"blindxfer\"\n", actual);
            Assert.Contains("efk.efkList.2.action.string=\"$P1N4$$Trefer$\"\n", actual);
            Assert.Contains("softkey.1.label=\"Park\"\n", actual);
            Assert.Contains("softkey.1.action=\"!park\"\n", actual);
            Assert.Contains("softkey.2.label=\"Blind Xfer\"\n", actual);
            Assert.Contains("softkey.2.action=\"!blindxfer\"\n", actual);
        }

        /// <summary>
        /// With parking off there is no Park key and no macro for it — a key that sends a code
        /// Asterisk ignores is worse than no key — and Blind Xfer moves up to be the first macro
        /// and the first soft key. Enhanced feature keys stay on, because a macro needs them.
        /// </summary>
        [Fact]
        public void Parking_off_means_no_park_key_and_blind_transfer_first()
        {
            var actual = PolycomConfigRenderer.Render(SampleConfig());

            Assert.DoesNotContain("park", actual);
            Assert.DoesNotContain("Park", actual);
            Assert.Contains("feature.enhancedFeatureKeys.enabled=\"1\"\n", actual);
            Assert.Contains("efk.efkList.1.mname=\"blindxfer\"\n", actual);
            Assert.Contains("efk.efkList.1.action.string=\"$P1N4$$Trefer$\"\n", actual);
            Assert.Contains("efk.efkprompt.1.label=\"Transfer to:\"\n", actual);
            Assert.DoesNotContain("efk.efkList.2", actual);
            Assert.Contains("softkey.1.label=\"Blind Xfer\"\n", actual);
            Assert.Contains("softkey.1.action=\"!blindxfer\"\n", actual);
            Assert.DoesNotContain("softkey.2", actual);
        }

        /// <summary>
        /// The Blind Xfer prompt collects as many digits as the longest extension number on the
        /// system (D144, amended), so it is derived from the extension set at each render rather
        /// than being a setting: renumber the site and every phone's prompt follows at its next
        /// poll.
        /// </summary>
        [Fact]
        public void The_blind_transfer_prompt_collects_the_extension_length()
        {
            var config = SampleConfig();
            config.Extensions.Add(new Extension { ExtensionID = 9, Number = "100003", Name = "Warehouse", Secret = "IIIIjjjjKKKKllll3333" });

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("efk.efkList.1.action.string=\"$P1N6$$Trefer$\"\n", actual);
        }

        /// <summary>
        /// The park code is re-checked before it is written as a macro, as every renderer re-checks
        /// what it is given: something that is not a star and one or two digits is refused rather
        /// than sent into somebody's call as DTMF.
        /// </summary>
        [Fact]
        public void An_invalid_park_code_is_refused_rather_than_rendered()
        {
            var config = SampleConfig();
            config.ParkDtmfCode = "1001";
            config.ParkEnabled = true;

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// On-call soft keys are for a phone that can be on a call: an unassigned phone registers
        /// as nothing, and its file stays the time and a poll (D78).
        /// </summary>
        [Fact]
        public void A_phone_with_no_extension_is_given_no_soft_keys()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>();
            config.ParkEnabled = true;

            var actual = PolycomConfigRenderer.Render(config);

            Assert.DoesNotContain("softkey", actual);
            Assert.DoesNotContain("efk", actual);
        }

        /// <summary>
        /// The gap is the point (D121 amended again): the user left key 5 blank on a Poly Edge 450
        /// and expected key 6 to stay where it was put. A blank key is a resource with nothing on
        /// it rather than a resource that is not written, so every key after it keeps its place.
        /// </summary>
        [Fact]
        public void A_blank_key_keeps_the_keys_after_it_where_they_are()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>
            {
                SampleLine(),
                new() { Position = 4, TargetType = PhoneButtonTarget.Blf, TargetValue = "1002" },
            };

            var actual = PolycomConfigRenderer.Render(config);

            // One line key is taken by the registration, so key 4 is resource 3.
            Assert.Contains("attendant.resourceList.1.address=\"\"\n", actual);
            Assert.Contains("attendant.resourceList.2.address=\"\"\n", actual);
            Assert.Contains("attendant.resourceList.3.address=\"1002\"\n", actual);
            Assert.Contains("attendant.resourceList.3.label=\"Sales\"\n", actual);

            // A blank key is blank all three ways, the FreePBX module's shape: the phone leaves
            // the key unassigned instead of skipping it and shuffling the rest up (D121).
            Assert.Contains("attendant.resourceList.1.label=\"\"\n", actual);
            Assert.Contains("attendant.resourceList.2.type=\"\"\n", actual);
            Assert.DoesNotContain("attendant.resourceList.4", actual);
        }

        /// <summary>
        /// Two registrations take the first two line keys, so a lamp on key 3 is resource 1: the
        /// resource index counts from the first key the registrations left free.
        /// </summary>
        [Fact]
        public void The_lamps_start_after_the_lines_the_phone_registers_as()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>
            {
                SampleLine(),
                new() { Position = 2, TargetType = PhoneButtonTarget.Line, TargetValue = "1002" },
                new() { Position = 3, TargetType = PhoneButtonTarget.ParkingSlot, TargetValue = "3" },
            };

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("attendant.resourceList.1.address=\"3\"\n", actual);
            Assert.DoesNotContain("attendant.resourceList.2", actual);
        }

        /// <summary>
        /// A phone whose only key is its line gets no attendant element at all, so the rest of its
        /// line keys stay whatever the handset does with them.
        /// </summary>
        [Fact]
        public void A_phone_with_no_lamps_is_given_no_attendant_list()
        {
            Assert.DoesNotContain("attendant", PolycomConfigRenderer.Render(SampleConfig()));
        }

        /// <summary>
        /// Two lines, which is a phone registering twice: reg.1 and reg.2, each taking a line key,
        /// and the lamps then land on the keys left over.
        /// </summary>
        [Fact]
        public void A_second_line_is_a_second_registration()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>
            {
                SampleLine(),
                new() { Position = 2, TargetType = PhoneButtonTarget.Line, TargetValue = "1002" },
            };

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("reg.1.address=\"1001\"", actual);
            Assert.Contains("reg.2.address=\"1002\"", actual);
            Assert.Contains("reg.2.auth.password=\"EEEEffffGGGGhhhh2222\"", actual);

            // The message waiting lamp follows the phone's own extension, not the second line's.
            Assert.Contains("msg.mwi.1.subscribe=\"1001\"", actual);
            Assert.DoesNotContain("attendant", actual);
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

            Assert.Contains("attendant.resourceList.1.label=\"Sales\"", PolycomConfigRenderer.Render(config));

            // The line still has to be there, or there would be nothing to register with; only the
            // extension the lamp names is taken away.
            config.Extensions = new List<Extension> { SampleExtension() };
            Assert.Contains("attendant.resourceList.1.label=\"1002\"", PolycomConfigRenderer.Render(config));
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

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
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

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        /// <summary>The thing that must never be in an unassigned phone's file: a credential.</summary>
        [Fact]
        public void A_phone_with_no_extension_is_given_no_registration()
        {
            var config = SampleConfig();
            config.Buttons = new List<PhoneButton>();

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
            config.Extensions[0].Name = "Sales & Support";

            var actual = PolycomConfigRenderer.Render(config);

            Assert.Contains("reg.1.displayName=\"1001 - Sales &amp; Support\"", actual);
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
            config.Extensions[0].Secret = "short";

            Assert.Throws<InvalidOperationException>(() => PolycomConfigRenderer.Render(config));
        }

        /// <summary>
        /// The offset is the zone's standard one, read from fixed January and July dates so it never
        /// drifts with the season (D150, amending D82): the phone adds its own daylight-saving hour,
        /// so the number this system writes must not already contain it.
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
        /// sign lost somewhere in the cast to int (D84). It is the STANDARD offset now, so
        /// Los Angeles is -28800 year-round and the assertion can pin it exactly (D150) — which is
        /// also the point: the Pacific daylight offset -25200 must never come back, because the
        /// phone adds that hour itself and a config that carries it runs the clock fast (D149).
        /// </summary>
        [Fact]
        public void The_gmt_offset_is_negative_for_a_western_zone()
        {
            var offset = PolycomConfig.GmtOffsetFor("America/Los_Angeles");

            Assert.Equal(-28800, offset);
        }

        /// <summary>
        /// The smaller of the January and July offsets is the standard one in either hemisphere
        /// (D150): a southern zone's daylight saving falls in its January, so Sydney's standard
        /// +10:00 is what comes back, never the summer +11:00.
        /// </summary>
        [Fact]
        public void The_gmt_offset_is_the_standard_offset_in_either_hemisphere()
        {
            Assert.Equal(36000, PolycomConfig.GmtOffsetFor("Australia/Sydney"));
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
