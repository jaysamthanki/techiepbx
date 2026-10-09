using Microsoft.Data.Sqlite;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Migration;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Migration;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Migration
{
    /// <summary>
    /// The import written into a real database (D170): trunks disabled whatever happens, routes
    /// landing with their disabled trunk, nothing overwritten, and voicemail copied only for the
    /// mailboxes that landed.
    /// </summary>
    public class MigrationImporterTests : IDisposable
    {
        private const string Secret = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";

        private readonly Database database;
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-import-").FullName;

        private string Export => Path.Combine(this.directory, "export");
        private string Spool => Path.Combine(this.directory, "spool");

        public MigrationImporterTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            Directory.CreateDirectory(this.Export);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private static ManifestExtension Ext(string number) => new() { Name = $"User {number}", Number = number, Secret = Secret, VoicemailEnabled = true, VoicemailPin = "1234" };

        private MigrationManifest Manifest()
        {
            var manifest = MigrationManifest.Parse("{\"manifestVersion\":1}");

            manifest.Extensions!.Add(Ext("101"));
            manifest.Extensions.Add(Ext("102"));
            manifest.Trunks!.Add(new ManifestTrunk
            {
                Name = "callcentric",
                Password = "provider-secret",
                Register = true,
                ServerHost = "callcentric.com",
                Username = "17771234567",
                MatchAddresses = "204.11.192.0/22",
            });
            manifest.OutboundRoutes!.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "callcentric", DialPattern = "NXXNXXXXXX", PrependDigits = "1" });
            manifest.InboundRoutes!.Add(new ManifestInboundRoute { DID = "17142029302", Destination = "from-did-direct,101,1" });
            manifest.Phones!.Add(new ManifestPhone { Mac = "0004f2728ceb", Model = "VVX-VVX_410-UA", Line = "101", Keys = new() { new() { Position = 1, Type = "blf", Value = "102" } } });

            return manifest;
        }

        private ImportPlan Plan(MigrationManifest manifest) =>
            new ImportPlanner(manifest, ExistingConfig.FromDatabase(this.database), this.Export, new ImportOptions()).Plan();

        private ImportReport Run(ImportPlan plan, string ffmpeg = "tnpbx-test-no-such-ffmpeg")
        {
            var sounds = new AnnouncementStore(Path.Combine(this.directory, "sounds"), new AudioConverter(ffmpeg, 5));
            return new MigrationImporter(this.database, sounds, this.Spool).Run(plan);
        }

        private void WriteFile(string relative, byte[] content)
        {
            var path = Path.Combine(this.Export, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        /// <summary>A real 16-bit 8 kHz mono WAV of a tenth of a second of silence.</summary>
        private static byte[] Wav()
        {
            const int samples = 800;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + (samples * 2));
            writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(8000);
            writer.Write(16000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(samples * 2);
            writer.Write(new byte[samples * 2]);
            writer.Flush();

            return stream.ToArray();
        }

        [Fact]
        public void Everything_lands_and_the_trunk_is_disabled_in_the_database()
        {
            var report = this.Run(this.Plan(this.Manifest()));

            Assert.Equal(new[] { "101", "102" }, report.Extensions);
            Assert.Equal(new[] { "callcentric" }, report.Trunks);

            var trunk = Assert.Single(new TrunkRepository(this.database).GetAll());
            Assert.False(trunk.Enabled);
            Assert.Equal("204.11.192.0/22", trunk.MatchAddresses);

            var route = Assert.Single(new OutboundRouteRepository(this.database).GetAll());
            Assert.Equal(trunk.TrunkID, route.TrunkID);
            Assert.Equal("_NXXNXXXXXX", route.DialPattern);
            Assert.Equal("1", route.PrependDigits);

            var inbound = Assert.Single(new InboundRouteRepository(this.database).GetAll());
            Assert.Equal(trunk.TrunkID, inbound.TrunkID);
            Assert.Equal("Extension:101", inbound.DestinationKey());

            var phone = Assert.Single(new PhoneRepository(this.database).GetAll());
            Assert.Equal("VVX_410", phone.Model);
            Assert.Equal(new[] { "Line:101", "Blf:102" }, new PhoneButtonRepository(this.database).GetForPhone(phone.PhoneID).Select(b => b.Key));
        }

        [Fact]
        public void A_plan_claiming_a_trunk_is_enabled_still_lands_it_disabled()
        {
            var plan = this.Plan(this.Manifest());
            plan.Trunks[0].Enabled = true;

            this.Run(plan);

            Assert.False(Assert.Single(new TrunkRepository(this.database).GetAll()).Enabled);
        }

        [Fact]
        public void An_extension_that_appeared_after_the_preview_is_cleared_and_replaced()
        {
            var plan = this.Plan(this.Manifest());

            new ExtensionRepository(this.database).Insert(new Extension { Number = "101", Name = "Already here", Secret = "ExistingSecret12345" });
            this.WriteFile("files/voicemail/101/INBOX/msg0000.txt", "[message]"u8.ToArray());

            var report = this.Run(plan);

            // The import clears its tables first (D174), even what appeared after the preview.
            Assert.Equal(new[] { "101", "102" }, report.Extensions);
            Assert.Contains("101", report.Cleared.ExtensionNumbers);
            var extension = new ExtensionRepository(this.database).GetByNumber("101")!;
            Assert.Equal("User 101", extension.Name);
            Assert.NotEqual("ExistingSecret12345", extension.Secret);
        }

        [Fact]
        public void Running_a_plan_twice_clears_and_lands_the_same_rows_again()
        {
            var manifest = this.Manifest();
            this.Run(this.Plan(manifest));

            var second = this.Plan(manifest);

            // Re-running is clean (D174): everything is planned again, no -imported suffixes.
            Assert.Equal(new[] { "101", "102" }, second.Extensions.Select(e => e.Number));
            Assert.Equal("callcentric", Assert.Single(second.Trunks).Name);
            Assert.Equal("Default", Assert.Single(second.OutboundRoutes).Route.Name);
            Assert.Contains("101", second.Cleared.ExtensionNumbers);

            this.Run(second);

            Assert.Equal(2, new ExtensionRepository(this.database).GetAll().Count);
            Assert.Single(new TrunkRepository(this.database).GetAll());
            Assert.Single(new PhoneRepository(this.database).GetAll());
        }

        [Fact]
        public void Voicemail_is_copied_into_the_mailbox_with_its_folders()
        {
            this.WriteFile("files/voicemail/101/INBOX/msg0000.txt", "[message]"u8.ToArray());
            this.WriteFile("files/voicemail/101/INBOX/msg0000.wav", Wav());
            this.WriteFile("files/voicemail/101/Old/msg0000.txt", "[message]"u8.ToArray());
            this.WriteFile("files/voicemail/101/Old/msg0000.wav", Wav());

            var report = this.Run(this.Plan(this.Manifest()));

            Assert.Equal(2, report.VoicemailMessages);
            Assert.False(report.StagingKept);
            Assert.True(File.Exists(Path.Combine(this.Spool, "default", "101", "INBOX", "msg0000.wav")));
            Assert.True(File.Exists(Path.Combine(this.Spool, "default", "101", "Old", "msg0000.txt")));
        }

        [Fact]
        public void A_mailbox_that_already_holds_messages_is_left_alone_and_the_export_kept()
        {
            this.WriteFile("files/voicemail/101/INBOX/msg0000.txt", "[imported]"u8.ToArray());

            var existing = Path.Combine(this.Spool, "default", "101", "INBOX");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "msg0000.txt"), "[already here]");

            var report = this.Run(this.Plan(this.Manifest()));

            Assert.Equal("[already here]", File.ReadAllText(Path.Combine(existing, "msg0000.txt")));
            Assert.Equal(0, report.VoicemailMessages);
            Assert.True(report.StagingKept);
            Assert.Contains(report.Warnings, w => w.Section == MigrationSection.Voicemail && w.Action.StartsWith("As root:"));
        }

        [Fact]
        public void Without_ffmpeg_a_wav_is_kept_as_it_is_and_said_so()
        {
            this.WriteFile("files/sounds/en/custom/Main.wav", Wav());

            var manifest = this.Manifest();
            manifest.Sounds!.Add(new ManifestSound { Filename = "en/custom/Main.wav" });

            var report = this.Run(this.Plan(manifest));

            var announcement = Assert.Single(new AnnouncementRepository(this.database).GetAll());
            Assert.Equal("Main", announcement.Name);
            Assert.Equal("main.wav", announcement.AudioFile);
            Assert.True(File.Exists(Path.Combine(this.directory, "sounds", "announcements", announcement.AnnouncementID.ToString(), "main.wav")));
            Assert.Contains(report.Warnings, w => w.What == "Announcement 'Main' kept FreePBX's WAV unconverted.");
        }

        [Fact]
        public void A_raw_sound_lands_as_an_announcement_with_no_audio()
        {
            this.WriteFile("files/sounds/en/custom/Old.gsm", new byte[] { 1, 2, 3 });

            var manifest = this.Manifest();
            manifest.Sounds!.Add(new ManifestSound { Filename = "en/custom/Old.gsm" });

            this.Run(this.Plan(manifest));

            var announcement = Assert.Single(new AnnouncementRepository(this.database).GetAll());
            Assert.False(announcement.HasAudio);
        }
    }
}
