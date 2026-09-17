using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What an announcement turns into: an entry in the internal context that answers, plays the
    /// one stored file and hangs up (D56). Dialled by a user, or reached by a Goto from a
    /// destination — the same three lines either way.
    /// </summary>
    public class AnnouncementRendererTests
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
        /// Out of order on purpose, plus the three kinds that are deliberately left out: switched
        /// off, no audio uploaded yet, and no play extension to write an entry for.
        /// </summary>
        private static List<Announcement> SampleAnnouncements() => new()
        {
            new Announcement
            {
                AnnouncementID = 2,
                Name = "Holiday closure",
                PlayExtension = "701",
                AudioFile = "holiday-closure.wav",
            },
            new Announcement
            {
                AnnouncementID = 3,
                Name = "Switched off",
                PlayExtension = "702",
                AudioFile = "switched-off.wav",
                Enabled = false,
            },
            new Announcement
            {
                AnnouncementID = 4,
                Name = "No audio yet",
                PlayExtension = "703",
            },
            new Announcement
            {
                AnnouncementID = 5,
                Name = "No number",
                AudioFile = "no-number.wav",
            },
            new Announcement
            {
                AnnouncementID = 1,
                Name = "Welcome message",
                Description = "Played to callers before the menu",
                PlayExtension = "700",
                AudioFile = "welcome-message.wav",
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(params Announcement[] announcements) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), announcements);

        [Fact]
        public void Announcements_match_expected_file()
        {
            var actual = Render(SampleAnnouncements().ToArray());

            Assert.Equal(Expected("extensions-announcements.conf"), actual);
        }

        /// <summary>
        /// A system with no announcements has to render exactly the dialplan it rendered before
        /// announcements existed.
        /// </summary>
        [Fact]
        public void A_system_with_no_announcements_renders_exactly_what_it_did_before()
        {
            var withEmpty = Render();

            Assert.Equal(ExtensionsConfRenderer.Render(SampleExtensions()), withEmpty);
            Assert.DoesNotContain("announcements", withEmpty);
        }

        /// <summary>Answer, play, hang up. Nothing else, and in that order.</summary>
        [Fact]
        public void An_announcement_answers_plays_and_hangs_up()
        {
            var actual = Render(SampleAnnouncements()[4]);

            Assert.Contains(
                "exten => 700,1,Answer()\n same => n,Playback(tnpbx/announcements/1/welcome-message)\n same => n,Hangup()\n",
                actual);
        }

        /// <summary>
        /// The prompt is the announcement's own directory and file without an extension, so
        /// Asterisk resolves it under its sounds directory and picks the format it has.
        /// </summary>
        [Fact]
        public void The_prompt_is_named_by_id_and_file_without_an_extension()
        {
            var actual = Render(SampleAnnouncements()[0]);

            Assert.Contains(" same => n,Playback(tnpbx/announcements/2/holiday-closure)\n", actual);
            Assert.DoesNotContain(".wav", actual);
        }

        [Fact]
        public void The_description_becomes_a_comment_above_the_entry()
        {
            Assert.Contains("; Welcome message\n; Played to callers before the menu\nexten => 700,", Render(SampleAnnouncements()[4]));
        }

        [Fact]
        public void An_announcement_with_no_description_gets_one_comment_line()
        {
            Assert.Contains("; Holiday closure\nexten => 701,", Render(SampleAnnouncements()[0]));
        }

        [Fact]
        public void A_switched_off_announcement_is_not_in_the_config_at_all()
        {
            var actual = Render(SampleAnnouncements().ToArray());

            Assert.DoesNotContain("702", actual);
            Assert.DoesNotContain("Switched off", actual);
        }

        /// <summary>
        /// Nothing to play means nothing worth writing: the entry would be a Playback of a file
        /// that is not there, which Asterisk only complains about mid-call.
        /// </summary>
        [Fact]
        public void An_announcement_with_no_audio_is_left_out()
        {
            var actual = Render(SampleAnnouncements().ToArray());

            Assert.DoesNotContain("703", actual);
            Assert.DoesNotContain("No audio yet", actual);
        }

        /// <summary>Without a number there is no extension to write, and no destination either.</summary>
        [Fact]
        public void An_announcement_with_no_play_extension_is_left_out()
        {
            var actual = Render(SampleAnnouncements().ToArray());

            Assert.DoesNotContain("No number", actual);
            Assert.DoesNotContain("no-number", actual);
        }

        /// <summary>The entries come out by number, whatever order the rows arrived in.</summary>
        [Fact]
        public void Announcements_are_written_in_play_extension_order()
        {
            var order = ExtensionsConfRenderer.AnnouncementRenderOrder(SampleAnnouncements());

            Assert.Equal(new[] { "700", "701" }, order.Select(a => a.PlayExtension));
        }

        [Fact]
        public void An_announcement_that_would_not_validate_is_never_written()
        {
            var announcement = new Announcement
            {
                AnnouncementID = 1,
                Name = "",
                PlayExtension = "700",
                AudioFile = "broken.wav",
            };

            Assert.Throws<InvalidOperationException>(() => Render(announcement));
        }

        /// <summary>
        /// A row that reached the database another way still cannot open a section of its own or
        /// comment out what follows.
        /// </summary>
        [Theory]
        [InlineData("Evil\n[evil]\nexten => _X.,1,Dial(PJSIP/1001)")]
        [InlineData("Evil; comment")]
        [InlineData("Evil]")]
        public void Injection_through_an_announcement_name_is_refused(string name)
        {
            var announcement = new Announcement
            {
                AnnouncementID = 1,
                Name = name,
                PlayExtension = "700",
                AudioFile = "evil.wav",
            };

            Assert.Throws<InvalidOperationException>(() => Render(announcement));
        }

        /// <summary>
        /// The file name is derived by us, but the renderer checks it anyway: a row edited by hand
        /// cannot smuggle a second Playback argument or a path out of the sounds directory.
        /// </summary>
        [Theory]
        [InlineData("../../../etc/passwd.wav")]
        [InlineData("evil).wav")]
        [InlineData("evil,x.wav")]
        public void A_stored_file_name_we_would_not_have_written_is_refused(string audioFile)
        {
            var announcement = new Announcement
            {
                AnnouncementID = 1,
                Name = "Welcome",
                PlayExtension = "700",
                AudioFile = audioFile,
            };

            Assert.Throws<InvalidOperationException>(() => Render(announcement));
        }

        /// <summary>
        /// An announcement destination goes in by the same door a user dialling the number uses,
        /// so there is one description of what an announcement does rather than two (D56).
        /// </summary>
        [Fact]
        public void An_announcement_destination_gotos_the_play_extension()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.Announcement, "700"));

            Assert.Equal(new[] { $"Goto({ExtensionsConfRenderer.InternalContext},700,1)" }, steps);
        }

        [Fact]
        public void An_announcement_destination_with_no_number_is_never_written()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Steps(new Destination(DestinationType.Announcement, "")));
        }
    }
}
