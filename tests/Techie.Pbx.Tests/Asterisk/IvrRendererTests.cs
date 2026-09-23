using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What an IVR turns into: one way in from the internal context, and a context of its own
    /// holding the menu — greeting, keys, retry handling and the final destination (D58, D59).
    /// </summary>
    public class IvrRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            new Extension
            {
                Number = "1002",
                Name = "Sales",
                Secret = "EEEEffffGGGGhhhh2222",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
            },
            new Extension { Number = "1003", Name = "Disabled Phone", Secret = "IIIIjjjjKKKKllll3333", Enabled = false },
        };

        /// <summary>
        /// One announcement anyone can dial, two that exist only to be greetings, and the two
        /// kinds an IVR cannot greet with: no audio, and switched off.
        /// </summary>
        private static List<Announcement> SampleAnnouncements() => new()
        {
            new Announcement
            {
                AnnouncementID = 1,
                Name = "Welcome message",
                Description = "Played to callers before the menu",
                PlayExtension = "700",
                AudioFile = "welcome-message.wav",
            },
            new Announcement { AnnouncementID = 2, Name = "Menu greeting", AudioFile = "menu-greeting.wav" },
            new Announcement { AnnouncementID = 3, Name = "After hours greeting", AudioFile = "after-hours.wav" },
            new Announcement { AnnouncementID = 4, Name = "No audio yet" },
            new Announcement { AnnouncementID = 5, Name = "Switched off", AudioFile = "switched-off.wav", Enabled = false },
        };

        /// <summary>
        /// Out of order on purpose, plus the three kinds that are deliberately left out: switched
        /// off, no play extension, and a greeting with nothing to play.
        /// </summary>
        private static List<Ivr> SampleIvrs() => new()
        {
            new Ivr
            {
                IvrID = 2,
                Name = "After hours",
                AnnouncementID = 3,
                PlayExtension = "501",
                TimeoutSeconds = 5,
                Retries = 0,
                Entries = new List<IvrEntry>
                {
                    new() { Digit = "1", DestinationType = "Voicemail", DestinationValue = "1002" },
                },
            },
            new Ivr { IvrID = 3, Name = "Switched off menu", AnnouncementID = 2, PlayExtension = "502", Enabled = false },
            new Ivr { IvrID = 4, Name = "No number", AnnouncementID = 2 },
            new Ivr { IvrID = 5, Name = "Greeting with no audio", AnnouncementID = 4, PlayExtension = "503" },
            new Ivr
            {
                IvrID = 1,
                Name = "Main menu",
                Description = "Daytime auto attendant",
                AnnouncementID = 2,
                PlayExtension = "500",
                EnableDirectDial = true,
                DestinationType = "Voicemail",
                DestinationValue = "1002",
                Entries = new List<IvrEntry>
                {
                    new() { Digit = "9", DestinationType = "Ivr", DestinationValue = "501" },
                    new() { Digit = "1", DestinationType = "Extension", DestinationValue = "1001" },
                    new() { Digit = "*", DestinationType = "Hangup" },
                    new() { Digit = "2", DestinationType = "Extension", DestinationValue = "1002" },
                    new() { Digit = "0", DestinationType = "Voicemail", DestinationValue = "1002" },
                },
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(params Ivr[] ivrs) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), SampleAnnouncements(), ivrs);

        [Fact]
        public void Ivrs_match_expected_file()
        {
            var actual = Render(SampleIvrs().ToArray());

            Assert.Equal(Expected("extensions-ivrs.conf"), actual);
        }

        /// <summary>
        /// A system with no IVRs has to render exactly the dialplan it rendered before IVRs
        /// existed.
        /// </summary>
        [Fact]
        public void A_system_with_no_ivrs_renders_exactly_what_it_did_before()
        {
            var withEmpty = Render();

            Assert.Equal(
                ExtensionsConfRenderer.Render(
                    SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                    new List<RingGroup>(), SampleAnnouncements()),
                withEmpty);

            Assert.DoesNotContain("[ivr-", withEmpty);
        }

        /// <summary>
        /// The one way in: an entry in the internal context, exactly as an announcement gets one,
        /// so a user dialling the number and a destination arrive by the same door (D59).
        /// </summary>
        [Fact]
        public void The_play_extension_gotos_the_menu_context()
        {
            Assert.Contains("exten => 500,1,Goto(ivr-1,s,1)\n", Render(SampleIvrs()[4]));
        }

        [Fact]
        public void An_ivr_destination_gotos_the_play_extension()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.Ivr, "500"));

            Assert.Equal(new[] { $"Goto({ExtensionsConfRenderer.InternalContext},500,1)" }, steps);
        }

        [Fact]
        public void An_ivr_destination_with_no_number_is_never_written()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Steps(new Destination(DestinationType.Ivr, "")));
        }

        /// <summary>
        /// Background, not Playback: a caller who already knows the menu can press a key over the
        /// greeting. The prompt is the referenced announcement's own stored file (D58).
        /// </summary>
        [Fact]
        public void The_greeting_is_the_referenced_announcements_file_played_with_background()
        {
            var actual = Render(SampleIvrs()[4]);

            Assert.Contains(" same => n(start),Background(tnpbx/announcements/2/menu-greeting)\n", actual);
            Assert.DoesNotContain("Playback(tnpbx/announcements/2/", actual);
        }

        /// <summary>
        /// The menu's keys are extensions of the IVR's own context, which is the whole reason it
        /// has one: an extension called "1" in [internal] would make single digits dialable from
        /// every phone in the building (D59).
        /// </summary>
        [Fact]
        public void The_menu_lives_in_its_own_context_not_the_internal_one()
        {
            var actual = Render(SampleIvrs()[4]);
            var internalContext = actual[..actual.IndexOf("[ivr-1]", StringComparison.Ordinal)];

            Assert.Contains("[ivr-1]\n", actual);
            Assert.DoesNotContain("exten => 1,1,", internalContext);
            Assert.DoesNotContain("exten => *,1,", internalContext);
        }

        /// <summary>
        /// A menu can never be a way out to the phone network: it includes nothing, so nothing in
        /// it can fall through to an outbound route (D50's rule, applied here).
        /// </summary>
        [Fact]
        public void A_menu_context_includes_nothing_and_dials_no_trunk()
        {
            var actual = Render(SampleIvrs().ToArray());
            var menus = actual[actual.IndexOf("[ivr-1]", StringComparison.Ordinal)..];

            Assert.DoesNotContain("include =>", menus);
            Assert.DoesNotContain("Dial(PJSIP/", menus);
        }

        /// <summary>Keys come out in the order a menu reads, whatever order the rows arrived in.</summary>
        [Fact]
        public void Keys_are_written_in_keypad_order()
        {
            var actual = Render(SampleIvrs()[4]);
            var keys = new[] { "exten => 0,1,", "exten => 1,1,", "exten => 2,1,", "exten => 9,1,", "exten => *,1," };
            var positions = keys.Select(k => actual.IndexOf(k, StringComparison.Ordinal)).ToList();

            Assert.DoesNotContain(-1, positions);
            Assert.Equal(positions.OrderBy(p => p), positions);
        }

        /// <summary>
        /// A digit with no key behind it is not a setting, it is simply absent: the caller gets
        /// Asterisk's own 'i' extension, which counts the try and plays "invalid" (D59).
        /// </summary>
        [Fact]
        public void A_free_digit_falls_through_to_the_invalid_handler()
        {
            var actual = Render(SampleIvrs()[4]);

            Assert.DoesNotContain("exten => 3,1,", actual);
            Assert.Contains("exten => i,1,NoOp(IVR 500 invalid entry ${EXTEN})\n", actual);
            Assert.Contains(" same => n,Playback(invalid)\n", actual);
        }

        /// <summary>
        /// Both the timeout and the invalid path count the try and give up at the same point, and
        /// both go back to the greeting rather than to the setup lines above it.
        /// </summary>
        [Fact]
        public void Timeout_and_invalid_both_retry_and_then_give_up()
        {
            var actual = Render(SampleIvrs()[4]);
            var retry =
                " same => n,Set(IVR_RETRIES=$[${IVR_RETRIES} + 1])\n" +
                " same => n,GotoIf($[${IVR_RETRIES} > 3]?final,1)\n";

            Assert.Contains($"exten => t,1,NoOp(IVR 500 timed out)\n{retry} same => n,Goto(s,start)\n", actual);
            Assert.Contains($"exten => i,1,NoOp(IVR 500 invalid entry ${{EXTEN}})\n{retry} same => n,Playback(invalid)\n same => n,Goto(s,start)\n", actual);
        }

        /// <summary>Retries is how many second chances a caller gets, so 0 gives up at the first.</summary>
        [Fact]
        public void No_retries_means_the_first_mistake_ends_the_menu()
        {
            Assert.Contains(" same => n,GotoIf($[${IVR_RETRIES} > 0]?final,1)\n", Render(SampleIvrs()[0]));
        }

        /// <summary>The caller's count starts at zero on every arrival, including from another menu.</summary>
        [Fact]
        public void The_retry_count_is_reset_when_the_menu_is_entered()
        {
            Assert.Contains(" same => n,Set(IVR_RETRIES=0)\n", Render(SampleIvrs()[4]));
        }

        /// <summary>
        /// Where the call goes when nobody chose anything, written once through the shared helper
        /// (D36) and jumped to from both retry paths.
        /// </summary>
        [Fact]
        public void The_final_destination_is_written_once_as_its_own_extension()
        {
            var actual = Render(SampleIvrs()[4]);

            Assert.Contains(
                "exten => final,1,NoOp(IVR 500 giving up to Voicemail:1002)\n same => n,VoiceMail(1002@default,u)\n same => n,Hangup()\n",
                actual);
        }

        /// <summary>
        /// A menu with one key on a dialable announcement, one on an announcement that is only a
        /// greeting (so not a destination the render list has), one on an extension, and a final
        /// destination on the announcement too.
        /// </summary>
        private static Ivr AnnouncementMenu(bool returnAfterAnnouncement) => new()
        {
            IvrID = 7,
            Name = "Hours menu",
            AnnouncementID = 2,
            PlayExtension = "507",
            ReturnAfterAnnouncement = returnAfterAnnouncement,
            DestinationType = "Announcement",
            DestinationValue = "700",
            Entries = new List<IvrEntry>
            {
                new() { Digit = "1", DestinationType = "Announcement", DestinationValue = "700" },
                new() { Digit = "2", DestinationType = "Extension", DestinationValue = "1001" },
            },
        };

        /// <summary>
        /// Off is the default and the dialplan as it always was: the key goes in by the
        /// announcement's own play extension, which plays it and hangs up (D57).
        /// </summary>
        [Fact]
        public void An_announcement_key_without_return_goes_to_the_play_extension()
        {
            var actual = Render(AnnouncementMenu(returnAfterAnnouncement: false));

            Assert.Contains(
                "exten => 1,1,NoOp(IVR 507 key 1 to Announcement:700)\n same => n,Goto(internal,700,1)\n",
                actual);
            Assert.DoesNotContain("and back to the menu", actual);
        }

        /// <summary>
        /// On, the key plays the announcement's file itself and goes back to priority 1 of this
        /// menu's own context: a Goto rather than a Gosub, so however often the caller comes round
        /// nothing stacks, and they arrive as a caller who dialled the menu fresh would (piece 37).
        /// </summary>
        [Fact]
        public void An_announcement_key_with_return_plays_the_file_and_starts_the_menu_again()
        {
            var actual = Render(AnnouncementMenu(returnAfterAnnouncement: true));

            Assert.Contains(
                "exten => 1,1,NoOp(IVR 507 key 1 to Announcement:700 and back to the menu)\n" +
                " same => n,Playback(tnpbx/announcements/1/welcome-message)\n" +
                " same => n,Goto(s,1)\n",
                actual);
            Assert.DoesNotContain("Gosub", actual);
        }

        /// <summary>
        /// Only announcement keys return. The extension key and the final destination are the same
        /// with the option on as off — the final one deliberately, because a caller who presses
        /// nothing would otherwise go round the menu for ever (D59).
        /// </summary>
        [Fact]
        public void Return_changes_nothing_but_the_announcement_keys()
        {
            var off = Render(AnnouncementMenu(returnAfterAnnouncement: false));
            var on = Render(AnnouncementMenu(returnAfterAnnouncement: true));

            const string extensionKey = "exten => 2,1,NoOp(IVR 507 key 2 to Extension:1001)\n same => n,Goto(internal,1001,1)\n";
            const string final = "exten => final,1,NoOp(IVR 507 giving up to Announcement:700)\n same => n,Goto(internal,700,1)\n";

            Assert.Contains(extensionKey, off);
            Assert.Contains(extensionKey, on);
            Assert.Contains(final, off);
            Assert.Contains(final, on);
        }

        /// <summary>
        /// An announcement the render list does not have — switched off since the menu was saved —
        /// has no file to play here, so the key falls back to the Goto it always wrote rather than
        /// a Playback of something that is not there.
        /// </summary>
        [Fact]
        public void A_key_on_an_announcement_that_cannot_play_is_written_as_before()
        {
            var menu = AnnouncementMenu(returnAfterAnnouncement: true);
            var announcements = SampleAnnouncements();
            announcements[0].Enabled = false;

            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), announcements, new[] { menu });

            Assert.Contains(
                "exten => 1,1,NoOp(IVR 507 key 1 to Announcement:700)\n same => n,Goto(internal,700,1)\n",
                actual);
        }

        /// <summary>The golden menus have the option off, and render byte for byte as they did.</summary>
        [Fact]
        public void Return_off_leaves_the_golden_file_unchanged()
        {
            Assert.All(SampleIvrs(), ivr => Assert.False(ivr.ReturnAfterAnnouncement));
            Assert.Equal(Expected("extensions-ivrs.conf"), Render(SampleIvrs().ToArray()));
        }

        [Fact]
        public void An_empty_final_destination_hangs_up()
        {
            var actual = Render(SampleIvrs()[0]);

            Assert.Contains("exten => final,1,NoOp(IVR 501 giving up to Hangup)\n same => n,Hangup()\n", actual);
        }

        /// <summary>
        /// Direct dial is one entry per extension rather than a pattern, as in the internal
        /// context, so only numbers that exist can be reached and a disabled phone cannot (D60).
        /// </summary>
        [Fact]
        public void Direct_dial_writes_one_entry_per_enabled_extension()
        {
            var actual = Render(SampleIvrs()[4]);
            var menu = actual[actual.IndexOf("[ivr-1]", StringComparison.Ordinal)..];

            Assert.Contains("exten => 1001,1,Goto(internal,1001,1)\n", menu);
            Assert.Contains("exten => 1002,1,Goto(internal,1002,1)\n", menu);
            Assert.DoesNotContain("1003", menu);
            Assert.DoesNotContain("_X", menu);
        }

        [Fact]
        public void Direct_dial_off_writes_no_extension_entries()
        {
            var actual = Render(SampleIvrs()[0]);
            var menu = actual[actual.IndexOf("[ivr-2]", StringComparison.Ordinal)..];

            Assert.DoesNotContain("exten => 1001,", menu);
            Assert.DoesNotContain("exten => 1002,", menu);
        }

        [Fact]
        public void A_switched_off_ivr_is_not_in_the_config_at_all()
        {
            var actual = Render(SampleIvrs().ToArray());

            Assert.DoesNotContain("502", actual);
            Assert.DoesNotContain("Switched off menu", actual);
        }

        [Fact]
        public void An_ivr_with_no_play_extension_is_left_out()
        {
            Assert.DoesNotContain("No number", Render(SampleIvrs().ToArray()));
        }

        /// <summary>
        /// A greeting with nothing to play would answer the call and then sit in silence, which is
        /// worse than the number simply not existing (D58).
        /// </summary>
        [Fact]
        public void An_ivr_whose_greeting_has_no_audio_is_left_out()
        {
            var actual = Render(SampleIvrs().ToArray());

            Assert.DoesNotContain("503", actual);
            Assert.DoesNotContain("Greeting with no audio", actual);
        }

        [Fact]
        public void An_ivr_whose_greeting_is_switched_off_is_left_out()
        {
            var ivr = SampleIvrs()[4];
            ivr.AnnouncementID = 5;

            Assert.Empty(ExtensionsConfRenderer.IvrRenderOrder(new[] { ivr }, SampleAnnouncements()));
        }

        [Fact]
        public void The_menus_come_out_in_play_extension_order()
        {
            var order = ExtensionsConfRenderer.IvrRenderOrder(SampleIvrs(), SampleAnnouncements());

            Assert.Equal(new[] { "500", "501" }, order.Select(i => i.PlayExtension));
        }

        [Fact]
        public void An_ivr_that_would_not_validate_is_never_written()
        {
            var ivr = SampleIvrs()[4];
            ivr.Name = "";

            Assert.Throws<InvalidOperationException>(() => Render(ivr));
        }

        /// <summary>
        /// A row that reached the database another way still cannot open a section of its own or
        /// comment out what follows.
        /// </summary>
        [Theory]
        [InlineData("Evil\n[evil]\nexten => _X.,1,Dial(PJSIP/1001)")]
        [InlineData("Evil; comment")]
        [InlineData("Evil]")]
        public void Injection_through_an_ivr_name_is_refused(string name)
        {
            var ivr = SampleIvrs()[4];
            ivr.Name = name;

            Assert.Throws<InvalidOperationException>(() => Render(ivr));
        }

        [Fact]
        public void A_key_that_is_not_on_a_keypad_is_never_written()
        {
            var ivr = SampleIvrs()[4];
            ivr.Entries = new List<IvrEntry>
            {
                new() { Digit = "A", DestinationType = "Extension", DestinationValue = "1001" },
            };

            Assert.Throws<InvalidOperationException>(() => Render(ivr));
        }
    }
}
