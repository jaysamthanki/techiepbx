using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Covers the database to files half of "apply config", the decision about which modules need
    /// a reload, and what happens to the "apply is due" marker. The reload itself needs a real
    /// Asterisk, so it is checked on the lab VM.
    /// </summary>
    public class ConfigApplierTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-apply-").FullName;
        private readonly string confDirectory;
        private readonly Database database;
        private readonly AnnouncementRepository announcements;
        private readonly CertificateRepository certificates;
        private readonly ExtensionRepository extensions;
        private readonly InboundRouteRepository inbound;
        private readonly IvrRepository ivrs;
        private readonly MohClassRepository mohClasses;
        private readonly MohFileRepository mohFiles;
        private readonly OutboundRouteRepository routes;
        private readonly RingGroupRepository ringGroups;
        private readonly SettingsRepository settings;
        private readonly TimeConditionRepository timeConditions;
        private readonly TrunkRepository trunks;
        private readonly ConfigPendingMarker pending;
        private readonly ConfigApplier applier;

        public ConfigApplierTests()
        {
            this.confDirectory = Path.Combine(this.directory, "asterisk");
            Directory.CreateDirectory(this.confDirectory);

            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.announcements = new AnnouncementRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
            this.inbound = new InboundRouteRepository(this.database);
            this.ivrs = new IvrRepository(this.database);
            this.mohClasses = new MohClassRepository(this.database);
            this.mohFiles = new MohFileRepository(this.database);
            this.ringGroups = new RingGroupRepository(this.database);
            this.routes = new OutboundRouteRepository(this.database);
            this.settings = new SettingsRepository(this.database);
            this.timeConditions = new TimeConditionRepository(this.database);
            this.trunks = new TrunkRepository(this.database);
            this.pending = new ConfigPendingMarker(this.database);

            // Port 1 has nothing listening: any attempt to reload would fail loudly.
            var ami = new AmiSettings { Port = 1, Username = "tnpbx", Secret = "not-a-real-secret", TimeoutSeconds = 1 };
            this.certificates = new CertificateRepository(this.database);
            this.applier = new ConfigApplier(
                this.confDirectory, new PjsipTransport(), new ParkingSettings(), this.certificates, this.extensions,
                this.trunks, this.routes, this.inbound, this.ringGroups, this.announcements, this.ivrs,
                this.timeConditions, this.mohClasses, this.mohFiles, new CallFlowControlRepository(this.database), ami, this.pending);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private void AddExtension(string number, string name, string secret) =>
            this.extensions.Insert(new Extension { Number = number, Name = name, Secret = secret });

        /// <summary>Every file in /etc/asterisk is generated now, base config included.</summary>
        [Fact]
        public void Renders_every_file_with_the_module_that_owns_it()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var files = this.applier.Render();

            Assert.Equal(
                new[]
                {
                    "asterisk.conf", "modules.conf", "rtp.conf", "pjsip_notify.conf", "logger.conf",
                    "manager.conf", PjsipConfRenderer.TlsCertificateFileName, "pjsip.conf",
                    "extensions.conf", "voicemail.conf", VoicemailOptionsRenderer.FileName,
                    "features.conf", "musiconhold.conf", "res_parking.conf", CdrManagerConfRenderer.FileName,
                    CdrConfRenderer.FileName, IndicationsConfRenderer.FileName,
                },
                files.Select(f => f.FileName));

            // The options file is the one thing here Asterisk never reads: it is the mailcmd
            // script's, and it is listed against app_voicemail rather than against no module at
            // all, because "no module" means "this needs an Asterisk restart" (D128).
            Assert.Equal(
                new string?[]
                {
                    null, null, null, null, ConfigApplier.LoggerModule,
                    ConfigApplier.ManagerModule, null, ConfigApplier.PjsipModule,
                    ConfigApplier.DialplanModule, ConfigApplier.VoicemailModule, ConfigApplier.VoicemailModule,
                    ConfigApplier.FeaturesModule, ConfigApplier.MohModule, ConfigApplier.ParkingModule,
                    ConfigApplier.CdrManagerModule, null, ConfigApplier.IndicationsModule,
                },
                files.Select(f => f.Module));

            Assert.Contains("[1001]", files.Single(f => f.FileName == "pjsip.conf").Content);

            // The Dial is priority 2 on a real database: priority 1 is the hold music backfill,
            // and every database has the class that ships to backfill with (D122 amended). The
            // U() carries the same backfill to the channel the Dial creates.
            Assert.Contains(
                $" same => n,Dial(PJSIP/1001,30,tTkKrU({ExtensionsConfRenderer.SetMohContext}))",
                files.Single(f => f.FileName == "extensions.conf").Content);
        }

        /// <summary>
        /// The class that ships reaches the internal context, so a caller who dialled from a phone
        /// here has something to hear when the other side holds them (D122 amended). It is read
        /// from the database, not baked into the renderer: renaming the class changes this line.
        /// </summary>
        [Fact]
        public void The_class_that_ships_is_what_an_internal_call_falls_back_to()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var dialplan = Content(this.applier.Render(), "extensions.conf");

            Assert.Contains(
                "exten => 1001,1,ExecIf($[\"${CHANNEL(musicclass)}\" = \"\" | \"${CHANNEL(musicclass)}\" = \"default\"]" +
                $"?Set(CHANNEL(musicclass)={MohClass.DefaultName}))\n",
                dialplan);

            var shipped = this.mohClasses.Default()!;
            shipped.Name = "Lobby";
            this.mohClasses.Update(shipped);

            Assert.Contains("?Set(CHANNEL(musicclass)=Lobby))\n", Content(this.applier.Render(), "extensions.conf"));
        }

        /// <summary>
        /// Asterisk reads these once, at startup, so no reload can apply them (D33).
        /// </summary>
        [Fact]
        public void The_files_asterisk_only_reads_at_startup_carry_no_module()
        {
            var restart = this.applier.Render().Where(f => f.NeedsRestart).Select(f => f.FileName);

            // tnpbx-cert.pem joins the restart set (D101): a transport reads its certificate
            // when it is built, so a renewed one reaches SIP only at the next Asterisk start.
            // pjsip_notify.conf joins it too (D123): res_pjsip_notify reads it when the module
            // loads, and nothing re-reads it.
            Assert.Equal(
                new[]
                {
                    "asterisk.conf", "modules.conf", "rtp.conf", "pjsip_notify.conf",
                    PjsipConfRenderer.TlsCertificateFileName, CdrConfRenderer.FileName,
                },
                restart);
        }

        [Fact]
        public void First_write_writes_every_file_and_a_repeat_writes_none()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var written = this.applier.Write().Select(f => f.FileName).ToList();

            // The thirteen of call parking (D119) — the ten of piece 23, of which
            // tnpbx-cert.pem is always written, empty when no certificate exists so a stale key
            // never lingers (D101), plus features.conf, musiconhold.conf and res_parking.conf,
            // written whether parking is switched on or not so that switching it off is itself
            // something an apply carries out — and tnpbx-voicemail-options.json, which is written
            // even with no mailboxes at all, so that removing the last one removes its options
            // too (D128). Fifteen with cdr_manager.conf, which has nothing from the database in it
            // and so is the same on every system (F5), and cdr.conf, which only turns unanswered
            // calls on so a missed inbound call leaves a record. Seventeen with indications.conf,
            // the fixed tone zone inband ringback is played from (D132).
            Assert.Equal(17, written.Count);
            foreach (var fileName in written)
                Assert.True(File.Exists(Path.Combine(this.confDirectory, fileName)), fileName);

            Assert.Empty(this.applier.Write());
        }

        [Fact]
        public void A_secret_change_only_touches_pjsip_conf()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();

            var extension = this.extensions.GetByNumber("1001")!;
            extension.Secret = "EEEEffffGGGGhhhh2222";
            this.extensions.Update(extension);

            var changed = this.applier.Write();

            Assert.Single(changed);
            Assert.Equal("pjsip.conf", changed[0].FileName);
            Assert.Equal(ConfigApplier.PjsipModule, changed[0].Module);
        }

        [Fact]
        public void A_new_extension_touches_both_files()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();

            AddExtension("1002", "Sales", "EEEEffffGGGGhhhh2222");

            Assert.Equal(
                new[] { ConfigApplier.PjsipModule, ConfigApplier.DialplanModule },
                this.applier.Write().Select(f => f.Module));
        }

        /// <summary>
        /// Switching voicemail on rewrites the mailbox list, the dialplan that falls back to it
        /// and the endpoint, which now carries a mailboxes = line for MWI (D108) — and the mailcmd
        /// script's options file, which lists exactly the mailboxes that exist (D128).
        /// </summary>
        [Fact]
        public void Switching_voicemail_on_touches_the_dialplan_the_mailboxes_and_pjsip()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();

            var extension = this.extensions.GetByNumber("1001")!;
            extension.VoicemailEnabled = true;
            extension.VoicemailPin = "4321";
            this.extensions.Update(extension);

            var changed = this.applier.Write();

            Assert.Equal(
                new[] { "pjsip.conf", "extensions.conf", "voicemail.conf", VoicemailOptionsRenderer.FileName },
                changed.Select(f => f.FileName));
            Assert.Equal(
                new[]
                {
                    ConfigApplier.PjsipModule, ConfigApplier.DialplanModule,
                    ConfigApplier.VoicemailModule, ConfigApplier.VoicemailModule,
                },
                changed.Select(f => f.Module));

            // Two files, one reload: the options file rides along with the module that owns
            // voicemail rather than adding a reload of its own (D128).
            Assert.Equal(
                new[] { ConfigApplier.PjsipModule, ConfigApplier.DialplanModule, ConfigApplier.VoicemailModule },
                ConfigApplier.ReloadOrder(changed));
        }

        /// <summary>
        /// The transcription choice reaches a file, because app_voicemail has no option for it and
        /// no way to pass one to the mailcmd script (D128). It touches that file and no other:
        /// nothing in Asterisk's own configuration changes.
        /// </summary>
        [Fact]
        public void Switching_transcription_on_only_touches_the_options_file()
        {
            this.extensions.Insert(new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = "AAAAbbbbCCCCdddd1111",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
                VoicemailEmail = "desk@example.com",
            });
            this.applier.Write();

            var extension = this.extensions.GetByNumber("1001")!;
            extension.VoicemailTranscribe = true;
            this.extensions.Update(extension);

            var changed = this.applier.Write();

            Assert.Equal(VoicemailOptionsRenderer.FileName, Assert.Single(changed).FileName);
            Assert.Contains("\"Transcribe\": true", changed[0].Content);
        }

        [Fact]
        public void Apply_does_not_open_an_ami_connection_when_nothing_changed()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();

            var result = this.applier.Apply();

            Assert.Empty(result.ChangedFiles);
            Assert.Empty(result.ReloadedModules);
            Assert.Empty(result.RestartRequiredFiles);
            Assert.False(result.RestartRequired);
        }

        /// <summary>
        /// Nothing a reload could apply changed, so there is nothing to say to Asterisk. The apply
        /// still succeeds — AMI is not even opened, and port 1 would refuse it — and it says a
        /// restart is owed (D33).
        /// </summary>
        [Fact]
        public void An_apply_that_only_changed_startup_files_needs_no_ami_connection()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();
            File.Delete(Path.Combine(this.confDirectory, "asterisk.conf"));

            var result = this.applier.Apply();

            Assert.Equal(new[] { "asterisk.conf" }, result.ChangedFiles);
            Assert.Empty(result.ReloadedModules);
            Assert.Equal(new[] { "asterisk.conf" }, result.RestartRequiredFiles);
            Assert.True(result.RestartRequired);
        }

        /// <summary>
        /// What the apply response hands the page, so it can name the files in the restart confirm
        /// it then offers (D104): every startup-only file the apply wrote, and only those. A
        /// reload picked up nothing here, so the list is the whole change.
        /// </summary>
        [Fact]
        public void An_apply_reports_every_startup_only_file_it_wrote()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();

            foreach (var fileName in new[] { "asterisk.conf", "modules.conf", "rtp.conf" })
                File.Delete(Path.Combine(this.confDirectory, fileName));

            var result = this.applier.Apply();

            Assert.True(result.RestartRequired);
            Assert.Equal(new[] { "asterisk.conf", "modules.conf", "rtp.conf" }, result.RestartRequiredFiles);
            Assert.Equal(result.ChangedFiles, result.RestartRequiredFiles);
            Assert.Empty(result.ReloadedModules);
        }

        /// <summary>
        /// Reloading manager.conf can close the session the reloads are being sent on, so it goes
        /// last and the files that need a restart are not in the plan at all (D34).
        /// </summary>
        [Fact]
        public void The_reload_plan_puts_manager_last_and_leaves_out_the_startup_files()
        {
            var plan = ConfigApplier.ReloadOrder(this.applier.Render());

            Assert.Equal(ConfigApplier.ManagerModule, plan[^1]);
            Assert.Equal(
                new[]
                {
                    ConfigApplier.LoggerModule, ConfigApplier.PjsipModule,
                    ConfigApplier.DialplanModule, ConfigApplier.VoicemailModule,
                    ConfigApplier.FeaturesModule, ConfigApplier.MohModule, ConfigApplier.ParkingModule, ConfigApplier.CdrManagerModule,
                    ConfigApplier.IndicationsModule, ConfigApplier.ManagerModule,
                },
                plan);
        }

        [Fact]
        public void The_reload_plan_names_each_module_once()
        {
            var twice = new List<GeneratedFile>
            {
                new("pjsip.conf", ConfigApplier.PjsipModule, ""),
                new("pjsip-extra.conf", ConfigApplier.PjsipModule, ""),
                new("asterisk.conf", null, ""),
            };

            Assert.Equal(new[] { ConfigApplier.PjsipModule }, ConfigApplier.ReloadOrder(twice));
        }

        /// <summary>
        /// The files already matching the database is exactly the case the marker is wrong about
        /// if nobody clears it: an apply that had nothing to do still means nothing is due.
        /// </summary>
        [Fact]
        public void Apply_clears_the_pending_marker_even_when_there_was_nothing_to_write()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            this.applier.Write();
            Assert.True(this.pending.IsPending);

            this.applier.Apply();

            Assert.False(this.pending.IsPending);
        }

        [Fact]
        public void Apply_reports_that_asterisk_is_unreachable_rather_than_failing_silently()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var ex = Assert.Throws<AmiException>(() => this.applier.Apply());

            Assert.Contains("Could not connect to AMI", ex.Message);

            // The files were still written: the database stays the source of truth and a later
            // apply only has to reload.
            Assert.True(File.Exists(Path.Combine(this.confDirectory, "pjsip.conf")));

            // But the reload did not happen, so an apply is still due.
            Assert.True(this.pending.IsPending);
        }

        [Fact]
        public void FromDatabase_takes_the_conf_directory_transport_and_ami_details_from_settings()
        {
            this.settings.Set(SettingsKeys.AsteriskConfDirectory, this.confDirectory);
            this.settings.Set(SettingsKeys.AmiUsername, "tnpbx");
            this.settings.Set(SettingsKeys.AmiSecret, "not-a-real-secret");
            this.settings.Set(SettingsKeys.SipExternalAddress, "203.0.113.10");
            this.settings.Set(SettingsKeys.SipLocalNets, "10.8.20.0/24");
            this.settings.Set(SettingsKeys.AmiUsername, "tnpbx");
            this.settings.Set(SettingsKeys.AmiSecret, "not-a-real-secret");
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var applier = ConfigApplier.FromDatabase(
                this.database, this.settings, this.extensions, this.trunks, this.routes, this.inbound,
                this.ringGroups, this.announcements, this.ivrs, this.timeConditions, this.mohFiles);
            var changed = applier.Write();

            Assert.Contains("pjsip.conf", changed.Select(f => f.FileName));
            var pjsip = File.ReadAllText(Path.Combine(this.confDirectory, "pjsip.conf"));
            Assert.Contains("external_media_address = 203.0.113.10", pjsip);
            Assert.Contains("local_net = 10.8.20.0/24", pjsip);
        }

        /// <summary>
        /// Parking is five settings and four files (D119), so switching it on has to reach the
        /// feature code, the lot and the dialplan in one apply — and nothing before that.
        /// </summary>
        [Fact]
        public void Parking_settings_reach_the_feature_code_the_lot_and_the_dialplan()
        {
            this.settings.Set(SettingsKeys.AsteriskConfDirectory, this.confDirectory);
            this.settings.Set(SettingsKeys.AmiUsername, "tnpbx");
            this.settings.Set(SettingsKeys.AmiSecret, "not-a-real-secret");
            this.settings.Set(SettingsKeys.ParkingEnabled, Toggles.On);
            this.settings.Set(SettingsKeys.ParkingDtmfCode, "*72");
            this.settings.Set(SettingsKeys.ParkingSlots, "4");
            this.settings.Set(SettingsKeys.ParkingTimeout, "90");

            var files = this.FromSettings().Render();

            Assert.Contains("parkcall => *72\n", Content(files, "features.conf"));
            Assert.Contains("parkpos => 1-4\n", Content(files, "res_parking.conf"));
            Assert.Contains("parkingtime => 90\n", Content(files, "res_parking.conf"));
            Assert.Contains("exten => 4,1,ParkedCall(default,4)\n", Content(files, "extensions.conf"));
            Assert.DoesNotContain("exten => 5,1,", Content(files, "extensions.conf"));
        }

        /// <summary>
        /// An uploaded track lands in its class's section of musiconhold.conf, and the lot names a
        /// class only once the audio setting says music (D119, D122). The class an admin added is
        /// there next to the one that ships, and the lot follows the Parking.MusicClass setting.
        /// </summary>
        [Fact]
        public void Music_on_hold_reaches_the_class_and_the_lot()
        {
            this.settings.Set(SettingsKeys.AsteriskConfDirectory, this.confDirectory);
            this.settings.Set(SettingsKeys.AmiUsername, "tnpbx");
            this.settings.Set(SettingsKeys.AmiSecret, "not-a-real-secret");
            this.settings.Set(SettingsKeys.ParkingEnabled, Toggles.On);

            var frontDesk = this.mohClasses.Insert(new MohClass { Name = "Front desk", Directory = "front-desk" });
            var trackID = this.mohFiles.Insert(new MohFile { MohClassID = frontDesk, Name = "Piano Loop", CreatedUnix = 1 });

            var silent = this.FromSettings().Render();
            var moh = Content(silent, "musiconhold.conf");

            Assert.Contains($"[{MohClass.DefaultName}]\n", moh);
            Assert.Contains("[Front desk]\n", moh);
            Assert.Contains("directory = /var/lib/asterisk/moh/front-desk\n", moh);
            Assert.Contains($"; {trackID}-piano-loop.g722 - Piano Loop\n", moh);
            Assert.DoesNotContain("parkedmusicclass =", Content(silent, "res_parking.conf"));

            this.settings.Set(SettingsKeys.ParkingAudio, ParkingAudio.MusicOnHold);
            this.settings.Set(SettingsKeys.ParkingMusicClass, "Front desk");

            Assert.Contains("parkedmusicclass = Front desk\n", Content(this.FromSettings().Render(), "res_parking.conf"));
        }

        /// <summary>
        /// A setting stored out of range cannot fail an apply: the applier falls back to the
        /// default and the settings page is what reports the bad value (D67).
        /// </summary>
        [Fact]
        public void A_parking_setting_that_could_not_be_written_out_falls_back_to_the_default()
        {
            this.settings.Set(SettingsKeys.AmiUsername, "tnpbx");
            this.settings.Set(SettingsKeys.AmiSecret, "not-a-real-secret");

            // Past the repository, which would refuse it, so that the reader is what is tested.
            using (var connection = this.database.Open())
            {
                connection.Execute(
                    "INSERT INTO Settings (\"Key\", Value) VALUES (@key, '99'), (@enabled, 'on')",
                    new { key = SettingsKeys.ParkingSlots, enabled = SettingsKeys.ParkingEnabled });
            }

            var parking = Content(this.FromSettings().Render(), "res_parking.conf");

            Assert.Contains($"parkpos => 1-{ParkingSettings.DefaultSlots}\n", parking);
        }

        private static string Content(List<GeneratedFile> files, string fileName) =>
            files.Single(f => f.FileName == fileName).Content;

        private ConfigApplier FromSettings() => ConfigApplier.FromDatabase(
            this.database, this.settings, this.extensions, this.trunks, this.routes, this.inbound,
            this.ringGroups, this.announcements, this.ivrs, this.timeConditions, this.mohFiles);

        [Fact]
        public void An_empty_database_still_renders_a_usable_dialplan()
        {
            var files = this.applier.Render();
            var dialplan = files.Single(f => f.FileName == "extensions.conf").Content;

            Assert.Contains("[internal]", dialplan);
            Assert.Contains("exten => *43,1,Answer()", dialplan);
            Assert.Contains("[transport-udp]", files.Single(f => f.FileName == "pjsip.conf").Content);
        }
    }
}
