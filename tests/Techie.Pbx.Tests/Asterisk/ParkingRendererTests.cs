using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Call parking (D119): features.conf, res_parking.conf, musiconhold.conf and the slot entries
    /// the dialplan grows when parking is switched on.
    ///
    /// The option names checked here were read off the Asterisk 22 sources rather than guessed:
    /// <c>configs/samples/res_parking.conf.sample</c> for the lot, <c>configs/samples/
    /// features.conf.sample</c> and <c>main/features_config.c</c> for <c>parkcall</c>,
    /// <c>configs/samples/musiconhold.conf.sample</c> for the class, and <c>apps/app_dial.c</c>
    /// for the k/K Dial options that make the feature code reachable at all.
    /// </summary>
    public class ParkingRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            new Extension
            {
                Number = "1002",
                Name = "O'Brien (Sales)",
                Secret = "EEEEffffGGGGhhhh2222",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
                VoicemailEmail = "sales@example.com",
            },
            new Extension
            {
                Number = "1003",
                Name = "Disabled Phone",
                Secret = "IIIIjjjjKKKKllll3333",
                Enabled = false,
            },
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        private static List<MohFile> SampleTracks() => new()
        {
            // Out of order, and one with no audio: only the stored ones are worth naming.
            new MohFile { MohFileID = 2, Name = "O'Brien's Theme", File = "2-obrien-s-theme.wav", CreatedUnix = 1 },
            new MohFile { MohFileID = 3, Name = "Never Uploaded", File = "", CreatedUnix = 2 },
            new MohFile { MohFileID = 1, Name = "Piano Loop", File = "1-piano-loop.wav", CreatedUnix = 3 },
        };

        private static ParkingSettings Enabled() => new()
        {
            Enabled = true,
            Slots = 3,
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void Features_with_parking_off_matches_expected_file()
        {
            Assert.Equal(Expected("features.conf"), FeaturesConfRenderer.Render());
        }

        [Fact]
        public void Features_with_parking_on_matches_expected_file()
        {
            Assert.Equal(Expected("features-parking.conf"), FeaturesConfRenderer.Render(Enabled()));
        }

        [Fact]
        public void Parking_with_parking_off_matches_expected_file()
        {
            Assert.Equal(Expected("res_parking.conf"), ParkingConfRenderer.Render());
        }

        /// <summary>
        /// The full default lot: nine slots, a minute, and no parkedmusicclass — which is what
        /// makes a parked caller hear nothing at all.
        /// </summary>
        [Fact]
        public void Parking_on_with_silence_matches_expected_file()
        {
            var parking = new ParkingSettings { Enabled = true };

            Assert.Equal(Expected("res_parking-silence.conf"), ParkingConfRenderer.Render(parking));
        }

        [Fact]
        public void Parking_on_with_music_matches_expected_file()
        {
            var parking = new ParkingSettings
            {
                Audio = ParkingAudio.MusicOnHold,
                Enabled = true,
                Slots = 4,
                TimeoutSeconds = 120,
            };

            Assert.Equal(Expected("res_parking-moh.conf"), ParkingConfRenderer.Render(parking));
        }

        [Fact]
        public void Moh_with_no_tracks_matches_expected_file()
        {
            Assert.Equal(Expected("musiconhold.conf"), MohConfRenderer.Render());
        }

        [Fact]
        public void Moh_with_tracks_matches_expected_file()
        {
            Assert.Equal(Expected("musiconhold-files.conf"), MohConfRenderer.Render(SampleTracks()));
        }

        [Fact]
        public void The_dialplan_with_parking_on_matches_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, Enabled());

            Assert.Equal(Expected("extensions-parking.conf"), actual);
        }

        /// <summary>
        /// Parking off is the file that existed before parking did: not one slot, and nothing a
        /// phone could dial by accident.
        /// </summary>
        [Fact]
        public void The_dialplan_says_nothing_about_parking_when_it_is_off()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.DoesNotContain("ParkedCall", actual);
            Assert.DoesNotContain("Call parking", actual);
        }

        /// <summary>
        /// The whole reason the slot count stops at 9: a slot is dialled, and anything longer than
        /// one digit would be competing with the extensions.
        /// </summary>
        [Fact]
        public void Every_slot_is_one_digit_and_there_is_one_entry_each()
        {
            var parking = new ParkingSettings { Enabled = true, Slots = SettingsValidation.MaxParkingSlots };
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, parking);

            for (var slot = 1; slot <= SettingsValidation.MaxParkingSlots; slot++)
                Assert.Contains($"exten => {slot},1,ParkedCall(default,{slot})\n", actual);

            Assert.Equal(SettingsValidation.MaxParkingSlots, actual.Split("ParkedCall(").Length - 1);
            Assert.DoesNotContain("exten => 10,", actual);
        }

        /// <summary>
        /// Every generated Dial has to ask for the feature, on both sides, or the feature code is
        /// a line in a file nothing consults (verified in apps/app_dial.c: k for the called party,
        /// K for the calling one).
        /// </summary>
        [Theory]
        [InlineData('t')]
        [InlineData('T')]
        [InlineData('k')]
        [InlineData('K')]
        public void The_dial_options_carry_transfer_and_park_for_both_sides(char option)
        {
            Assert.Contains(option.ToString(), ExtensionsConfRenderer.DialOptions);
        }

        [Fact]
        public void Every_generated_dial_carries_the_options()
        {
            var trunks = new List<Trunk>
            {
                new()
                {
                    TrunkID = 1,
                    Name = "callcentric",
                    ServerHost = "callcentric.com",
                    Username = "17771234567",
                    Password = "not-a-real-password",
                    Register = true,
                },
            };
            var routes = new List<OutboundRoute>
            {
                new() { OutboundRouteID = 1, Name = "local", TrunkID = 1, DialPattern = "_NXXXXXXX", Priority = 10 },
            };
            var groups = new List<RingGroup>
            {
                new() { RingGroupID = 1, Number = "600", Name = "Support", Members = "1001,1002", RingSeconds = 20 },
            };

            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), trunks, routes, new List<InboundRoute>(), groups,
                new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, Enabled());

            var dials = actual.Split('\n').Where(line => line.Contains("Dial(", StringComparison.Ordinal)).ToList();

            Assert.NotEmpty(dials);
            Assert.All(dials, line => Assert.EndsWith($",{ExtensionsConfRenderer.DialOptions})", line, StringComparison.Ordinal));
        }

        /// <summary>
        /// The class must not be called "default": Asterisk falls back to a class by that name
        /// whenever a caller asks for music and none was named, so one would be played to a parked
        /// caller whose setting says silence (res/res_musiconhold.c, local_ast_moh_start).
        /// </summary>
        [Fact]
        public void The_music_class_is_not_called_default()
        {
            Assert.NotEqual("default", MohConfRenderer.ClassName);
            Assert.DoesNotContain("[default]", MohConfRenderer.Render(SampleTracks()));
        }

        /// <summary>
        /// And the lot's class has to beat whatever the channel suggests, or a PJSIP endpoint's
        /// own moh_suggest would decide what a parked caller hears.
        /// </summary>
        [Fact]
        public void The_lots_class_wins_over_the_channels()
        {
            Assert.Contains("preferchannelclass = no\n", MohConfRenderer.Render());
        }

        /// <summary>A track with no file on the row is not worth naming in a conf file.</summary>
        [Fact]
        public void A_track_with_no_audio_is_left_out()
        {
            var actual = MohConfRenderer.Render(SampleTracks());

            Assert.DoesNotContain("Never Uploaded", actual);
            Assert.Equal(2, MohConfRenderer.RenderOrder(SampleTracks()).Count);
        }

        /// <summary>
        /// A row that reached the database some other way must not reach a conf file, which is the
        /// rule every other renderer here follows.
        /// </summary>
        [Fact]
        public void A_track_whose_file_name_we_would_not_have_written_is_refused()
        {
            var tracks = new List<MohFile>
            {
                new() { MohFileID = 1, Name = "Evil", File = "../../etc/asterisk/pjsip.conf" },
            };

            Assert.Throws<InvalidOperationException>(() => MohConfRenderer.Render(tracks));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public void A_slot_count_outside_the_range_is_refused(int slots)
        {
            var parking = new ParkingSettings { Enabled = true, Slots = slots };

            Assert.Throws<InvalidOperationException>(() => ParkingConfRenderer.Render(parking));
        }

        [Theory]
        [InlineData("3")]
        [InlineData("*333")]
        [InlineData("*3]\n[evil")]
        [InlineData("")]
        public void A_feature_code_that_is_not_a_star_and_digits_is_refused(string code)
        {
            var parking = new ParkingSettings { Enabled = true, DtmfCode = code };

            Assert.Throws<InvalidOperationException>(() => FeaturesConfRenderer.Render(parking));
        }

        /// <summary>
        /// res_parking.conf is read by a module that can reload it, so a change to the parking
        /// settings reaches Asterisk at the next apply rather than at the next restart.
        /// </summary>
        [Fact]
        public void The_parking_files_are_reloads_not_restarts()
        {
            var files = new List<GeneratedFile>
            {
                new("features.conf", ConfigApplier.FeaturesModule, ""),
                new("musiconhold.conf", ConfigApplier.MohModule, ""),
                new("res_parking.conf", ConfigApplier.ParkingModule, ""),
            };

            Assert.All(files, file => Assert.False(file.NeedsRestart));
            Assert.Equal(
                new[] { ConfigApplier.FeaturesModule, ConfigApplier.MohModule, ConfigApplier.ParkingModule },
                ConfigApplier.ReloadOrder(files));
        }

        /// <summary>
        /// The modules parking needs, all three of them. bridge_holding is the one that is easy to
        /// forget and impossible to work without: a parked call lives in a holding bridge.
        /// </summary>
        [Theory]
        [InlineData("res_parking.so")]
        [InlineData("res_musiconhold.so")]
        [InlineData("bridge_holding.so")]
        public void The_allowlist_carries_what_parking_needs(string module)
        {
            Assert.Contains(module, ModulesConfRenderer.Modules);
        }

        /// <summary>The music on hold directory the renderer names is the one the store writes to.</summary>
        [Fact]
        public void The_class_names_the_directory_the_store_writes_to()
        {
            Assert.Contains($"directory = {MohStore.DefaultMohPath}\n", MohConfRenderer.Render(SampleTracks()));
        }
    }
}
