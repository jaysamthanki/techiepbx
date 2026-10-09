using Techie.Pbx.Core.Migration;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Migration
{
    /// <summary>
    /// The import plan (D170): what lands, what is skipped, and that every skip and every changed
    /// value is a warning the operator can act on.
    /// </summary>
    public class ImportPlannerTests : IDisposable
    {
        private const string Secret = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";

        private readonly string root = Directory.CreateTempSubdirectory("tnpbx-plan-").FullName;

        public void Dispose()
        {
            Directory.Delete(this.root, recursive: true);
        }

        private static ManifestExtension Ext(string number, string tech = "pjsip") => new()
        {
            Name = $"User {number}",
            Number = number,
            Secret = Secret,
            Tech = tech,
            VoicemailContext = "default",
        };

        private static MigrationManifest Manifest() => MigrationManifest.Parse("{\"manifestVersion\":1}");

        private static ManifestTrunk Trunk(string name) => new()
        {
            Name = name,
            Password = "provider-secret",
            Register = true,
            ServerHost = "callcentric.com",
            ServerPort = 5060,
            Tech = "pjsip",
            Username = "17771234567",
        };

        private ImportPlan Plan(MigrationManifest manifest, ExistingConfig? existing = null, ImportOptions? options = null) =>
            new ImportPlanner(manifest, existing ?? new ExistingConfig(), this.root, options ?? new ImportOptions()).Plan();

        private void WriteFile(string relative, string content = "x")
        {
            var path = Path.Combine(this.root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        [Fact]
        public void Extensions_land_with_their_secret_and_voicemail()
        {
            var manifest = Manifest();
            var source = Ext("101");
            source.OutboundCallerID = "17147307943";
            source.VoicemailEnabled = true;
            source.VoicemailPin = "4321";
            source.VoicemailEmail = "amy@example.com";
            source.VoicemailAttach = false;
            manifest.Extensions!.Add(source);

            var plan = this.Plan(manifest);

            var extension = Assert.Single(plan.Extensions);
            Assert.Equal("101", extension.Number);
            Assert.Equal("User 101", extension.Name);
            Assert.Equal(Secret, extension.Secret);
            Assert.Equal("17147307943", extension.OutboundCallerID);
            Assert.True(extension.VoicemailEnabled);
            Assert.Equal("4321", extension.VoicemailPin);
            Assert.Equal("amy@example.com", extension.VoicemailEmail);
            Assert.False(extension.VoicemailAttachRecording);
            Assert.Equal("", extension.UserEmail);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void The_sign_in_address_is_copied_only_when_asked()
        {
            var manifest = Manifest();
            var source = Ext("101");
            source.VoicemailEmail = "amy@example.com";
            manifest.Extensions!.Add(source);

            var plan = this.Plan(manifest, options: new ImportOptions { UserEmailFromVoicemail = true });

            Assert.Equal("amy@example.com", Assert.Single(plan.Extensions).UserEmail);
        }

        [Fact]
        public void An_existing_extension_is_cleared_and_the_freePBX_one_lands()
        {
            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("101"));
            manifest.Extensions.Add(Ext("102"));

            var existing = new ExistingConfig();
            existing.ExtensionNumbers.Add("101");

            var plan = this.Plan(manifest, existing);

            // The import clears its tables first (D174): both land, and the preview says what went.
            Assert.Equal(new[] { "101", "102" }, plan.Extensions.Select(e => e.Number));
            Assert.Contains("101", plan.Cleared.ExtensionNumbers);
            Assert.DoesNotContain(plan.Warnings, w => w.What.Contains("was not imported"));
        }

        [Fact]
        public void Chan_sip_extensions_land_as_the_same_number_and_secret()
        {
            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("120", tech: "sip"));

            var plan = this.Plan(manifest);

            var extension = Assert.Single(plan.Extensions);
            Assert.Equal("120", extension.Number);
            Assert.Equal(Secret, extension.Secret);
            Assert.Equal(1, plan.ChanSipExtensions);
            Assert.Contains(plan.Warnings, w => w.What.Contains("chan_sip extension(s) become PJSIP"));
        }

        [Fact]
        public void A_secret_or_pin_TNPBX_would_refuse_is_replaced_and_said_so()
        {
            var manifest = Manifest();
            var source = Ext("390");
            source.Secret = "";
            source.VoicemailEnabled = true;
            source.VoicemailPin = "";
            manifest.Extensions!.Add(source);

            var plan = this.Plan(manifest);

            var extension = Assert.Single(plan.Extensions);
            Assert.Empty(extension.Validate());
            Assert.Matches("^[A-Za-z0-9]{24}$", extension.Secret);
            Assert.Matches("^[0-9]{6}$", extension.VoicemailPin);
            Assert.Contains(plan.Warnings, w => w.What == "Extension 390 was given a new SIP secret.");
            Assert.Contains(plan.Warnings, w => w.What == "Extension 390's mailbox was given a new voicemail PIN.");
        }

        [Fact]
        public void Every_trunk_is_planned_disabled_and_the_preview_says_so()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("callcentric"));
            var second = Trunk("backup");
            second.DisabledInFreePBX = false;
            manifest.Trunks.Add(second);

            var plan = this.Plan(manifest);

            Assert.Equal(2, plan.Trunks.Count);
            Assert.All(plan.Trunks, t => Assert.False(t.Enabled));
            Assert.Contains(plan.Warnings, w => w.What.StartsWith("Every imported trunk is DISABLED: callcentric, backup"));
        }

        [Fact]
        public void A_colliding_trunk_name_is_cleared_so_nothing_needs_a_suffix()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("callcentric"));

            var existing = new ExistingConfig();
            existing.TrunkNames.Add("callcentric");

            var plan = this.Plan(manifest, existing);

            Assert.Equal("callcentric", Assert.Single(plan.Trunks).Name);
            Assert.Contains("callcentric", plan.Cleared.TrunkNames);
        }

        [Theory]
        [InlineData("Callcentric SIP", "Callcentric-SIP")]
        [InlineData("2nd provider", "trunk-2nd-provider")]
        [InlineData("!!!", "trunk")]
        [InlineData("a-very-long-trunk-name-from-freepbx-that-goes-on", "a-very-long-trunk-name-from-free")]
        public void Section_names_follow_the_trunk_name_rule(string raw, string expected)
        {
            Assert.Equal(expected, ImportPlanner.SectionName(raw, "trunk"));
        }

        [Fact]
        public void Unique_names_fit_in_32()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abcdefghijabcdefghijabcdefghij12", "abcdefghijabcdefghijabc-imported" };

            Assert.Equal("abcdefghijabcdefghijab-imported2", ImportPlanner.UniqueSectionName("abcdefghijabcdefghijabcdefghij12", taken));
        }

        [Fact]
        public void A_trunk_the_model_refuses_is_skipped_and_its_routes_with_it()
        {
            var manifest = Manifest();
            var trunk = Trunk("bad host");
            trunk.ServerHost = "not a host!";
            manifest.Trunks!.Add(trunk);
            manifest.OutboundRoutes!.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "bad host", DialPattern = "NXXNXXXXXX" });
            manifest.OutboundRoutes.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "bad host", DialPattern = "NXXXXXX" });

            var plan = this.Plan(manifest);

            Assert.Empty(plan.Trunks);
            Assert.Empty(plan.OutboundRoutes);
            Assert.Contains(plan.Warnings, w => w.What == "Trunk 'bad host' was not imported." && w.Why.Contains("Server host"));
            var route = Assert.Single(plan.Warnings, w => w.Section == MigrationSection.OutboundRoutes);
            Assert.Equal("Outbound route 'Default' was not imported (2 pattern(s): NXXNXXXXXX, NXXXXXX).", route.What);
        }

        [Fact]
        public void A_trunk_with_a_password_but_no_username_lands_without_the_password()
        {
            var manifest = Manifest();
            var trunk = Trunk("CallcentricSIP");
            trunk.Tech = "sip";
            trunk.Username = "";
            trunk.Register = false;
            manifest.Trunks!.Add(trunk);
            manifest.OutboundRoutes!.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "CallcentricSIP", DialPattern = "NXXNXXXXXX" });

            var plan = this.Plan(manifest);

            var planned = Assert.Single(plan.Trunks);
            Assert.Equal("", planned.Password);
            Assert.False(planned.Register);
            Assert.False(planned.Enabled);
            Assert.Single(plan.OutboundRoutes);
            Assert.Contains(plan.Warnings, w => w.What == "Trunk 'CallcentricSIP' was imported without its password, and not registering.");
            Assert.Contains(plan.Warnings, w => w.What == "1 chan_sip trunk(s) become PJSIP trunks.");
        }

        [Fact]
        public void A_route_with_several_patterns_becomes_numbered_routes_in_export_order()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("callcentric"));

            foreach (var (pattern, prepend) in new[] { ("011.", ""), ("1NXXNXXXXXX", ""), ("NXXNXXXXXX", "1"), ("NXXXXXX", "1714"), ("911", "") })
                manifest.OutboundRoutes!.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "callcentric", DialPattern = pattern, PrependDigits = prepend });

            var plan = this.Plan(manifest);

            Assert.Equal(new[] { "Default-02", "Default-03", "Default-04", "Default-05" }, plan.OutboundRoutes.Select(r => r.Route.Name));
            Assert.Equal(new[] { "_1NXXNXXXXXX", "_NXXNXXXXXX", "_NXXXXXX", "_911" }, plan.OutboundRoutes.Select(r => r.Route.DialPattern));
            Assert.Equal("1714", plan.OutboundRoutes[2].Route.PrependDigits);
            Assert.All(plan.OutboundRoutes, r => Assert.Equal("callcentric", r.TrunkName));
            Assert.Contains(plan.Warnings, w => w.What.Contains("pattern '011.' was not imported") && w.Why.Contains("international"));
        }

        [Fact]
        public void Two_routes_on_one_pattern_are_both_kept_and_the_overlap_named()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("callcentric"));
            manifest.OutboundRoutes!.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "callcentric", DialPattern = "911" });
            manifest.OutboundRoutes.Add(new ManifestOutboundRoute { Name = "Default", Priority = 1, TrunkName = "callcentric", DialPattern = "911", PrependDigits = "9" });

            var plan = this.Plan(manifest);

            Assert.Equal(2, plan.OutboundRoutes.Count);
            Assert.Contains(plan.Warnings, w => w.What == "Routes Default-01 and Default-02 both match _911.");
        }

        [Fact]
        public void An_explicit_strip_is_added_to_a_prefix_strip()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("callcentric"));
            manifest.OutboundRoutes!.Add(new ManifestOutboundRoute { Name = "out", Priority = 5, TrunkName = "callcentric", DialPattern = "9|NXXXXXX", StripDigits = 0 });

            var route = Assert.Single(this.Plan(manifest).OutboundRoutes).Route;

            Assert.Equal("out", route.Name);
            Assert.Equal("_9NXXXXXX", route.DialPattern);
            Assert.Equal(1, route.StripDigits);
            Assert.Equal(5, route.Priority);
        }

        [Fact]
        public void Inbound_extension_destinations_land_once_per_trunk()
        {
            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("151"));
            manifest.Trunks!.Add(Trunk("one"));
            manifest.Trunks.Add(Trunk("two"));
            manifest.InboundRoutes!.Add(new ManifestInboundRoute { DID = "17142029302", Description = "DID Javier", Destination = "from-did-direct,151,1" });

            var plan = this.Plan(manifest);

            Assert.Equal(1, plan.InboundRouteCount);
            Assert.Equal(new[] { "one", "two" }, plan.InboundRoutes.Select(r => r.TrunkName));
            Assert.All(plan.InboundRoutes, r =>
            {
                Assert.Equal("17142029302", r.Route.DID);
                Assert.Equal("Extension", r.Route.DestinationType);
                Assert.Equal("151", r.Route.DestinationValue);
                Assert.Equal("DID Javier", r.Route.Description);
            });
            Assert.Contains(plan.Warnings, w => w.What.Contains("once for every imported trunk"));
        }

        [Fact]
        public void An_unsupported_destination_skips_the_route_and_names_the_raw_string()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("one"));
            manifest.InboundRoutes!.Add(new ManifestInboundRoute { DID = "17147307943", Description = "QualityBidders", Destination = "ivr-1,s,1" });
            manifest.InboundRoutes.Add(new ManifestInboundRoute { DID = "", CatchAll = true, Description = "Default", Destination = "ext-group,600,1" });

            var plan = this.Plan(manifest);

            Assert.Empty(plan.InboundRoutes);
            Assert.Contains(plan.Warnings, w => w.Section == MigrationSection.InboundRoutes && w.Why.Contains("'ivr-1,s,1'"));
            Assert.Contains(plan.Warnings, w => w.What.StartsWith("The catch-all inbound route") && w.Why.Contains("'ext-group,600,1'"));
        }

        [Fact]
        public void A_blank_did_is_the_catch_all_and_a_caller_id_match_is_not_carried()
        {
            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("101"));
            manifest.Trunks!.Add(Trunk("one"));
            manifest.InboundRoutes!.Add(new ManifestInboundRoute { DID = "", CatchAll = true, Destination = "ext-local,101,1" });
            manifest.InboundRoutes.Add(new ManifestInboundRoute { DID = "", CallerIDMatch = "18585733600", Destination = "ext-local,101,1" });

            var plan = this.Plan(manifest);

            var route = Assert.Single(plan.InboundRoutes).Route;
            Assert.True(route.CatchAll);
            Assert.Equal("", route.DID);
            Assert.Contains(plan.Warnings, w => w.Why.Contains("caller ID 18585733600"));
        }

        [Fact]
        public void A_destination_extension_that_will_not_exist_skips_the_route()
        {
            var manifest = Manifest();
            manifest.Trunks!.Add(Trunk("one"));
            manifest.InboundRoutes!.Add(new ManifestInboundRoute { DID = "5551234", Destination = "ext-local,999,1" });

            var plan = this.Plan(manifest);

            Assert.Empty(plan.InboundRoutes);
            Assert.Contains(plan.Warnings, w => w.Why.Contains("extension 999"));
        }

        [Fact]
        public void Phones_get_their_line_as_key_1_and_keys_after_it()
        {
            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("110"));
            manifest.Extensions.Add(Ext("111"));
            manifest.Phones!.Add(new ManifestPhone
            {
                Mac = "64:16:7F:96:73:24",
                Model = "VVX-VVX_410-UA",
                Line = "110",
                Keys = new List<ManifestPhoneKey>
                {
                    new() { Position = 1, Type = "parking", Value = "71", Label = "Park 1" },
                    new() { Position = 2, Type = "blf", Value = "111", Label = "Bob" },
                    new() { Position = 3, Type = "speeddial", Value = "18005551212", Label = "Pizza" },
                },
            });

            var plan = this.Plan(manifest);

            var phone = Assert.Single(plan.Phones);
            Assert.Equal("64167f967324", phone.Phone.Mac);
            Assert.Equal("VVX_410", phone.Phone.Model);
            Assert.Equal(PhoneBrand.Polycom, phone.Phone.Brand);
            Assert.Equal(new[] { "1 Line:110", "2 ParkingSlot:1", "3 Blf:111" }, phone.Buttons.Select(b => $"{b.Position} {b.Key}"));
            Assert.Contains(plan.Warnings, w => w.What.Contains("key 4 ('Pizza'"));
            Assert.Contains(plan.Warnings, w => w.What.StartsWith("Parking keys were mapped"));
        }

        [Fact]
        public void An_existing_mac_is_cleared_and_a_second_phone_on_one_line_gets_no_keys()
        {
            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("104"));
            manifest.Phones!.Add(new ManifestPhone { Mac = "0004f2000001", Line = "104" });
            manifest.Phones.Add(new ManifestPhone { Mac = "0004f2000002", Line = "104" });
            manifest.Phones.Add(new ManifestPhone { Mac = "0004f2000003", Line = "104" });

            var existing = new ExistingConfig();
            existing.PhoneMacs.Add("0004f2000001");

            var plan = this.Plan(manifest, existing);

            // All three land (the existing one is cleared first, D174); the first to claim line 104 keeps it.
            Assert.Equal(new[] { "0004f2000001", "0004f2000002", "0004f2000003" }, plan.Phones.Select(p => p.Phone.Mac));
            Assert.Contains("0004f2000001", plan.Cleared.PhoneMacs);
            Assert.Single(plan.Phones[0].Buttons);
            Assert.Empty(plan.Phones[1].Buttons);
            Assert.Empty(plan.Phones[2].Buttons);
            Assert.Contains(plan.Warnings, w => w.What == "Phone 0004f2000002 was imported without keys.");
            Assert.Contains(plan.Warnings, w => w.What == "Phone 0004f2000003 was imported without keys.");
        }

        [Fact]
        public void Voicemail_goes_only_to_mailboxes_the_import_creates()
        {
            this.WriteFile("files/voicemail/101/INBOX/msg0000.txt");
            this.WriteFile("files/voicemail/101/INBOX/msg0000.wav");
            this.WriteFile("files/voicemail/101/Old/msg0000.txt");
            this.WriteFile("files/voicemail/101/Old/msg0000.WAV");
            this.WriteFile("files/voicemail/101/INBOX/greet.wav");
            this.WriteFile("files/voicemail/102/INBOX/msg0000.txt");
            this.WriteFile("files/voicemail/103/INBOX/msg0000.txt");

            var manifest = Manifest();
            manifest.Extensions!.Add(Ext("101"));
            manifest.Extensions.Add(Ext("102"));

            var existing = new ExistingConfig();
            existing.ExtensionNumbers.Add("102");

            var plan = this.Plan(manifest, existing);

            // Extension 102 is cleared first (D174), so its mailbox is one the import creates too.
            Assert.Equal(new[] { "101", "102" }, plan.Mailboxes.Select(m => m.Mailbox));
            Assert.Equal(2, plan.Mailboxes[0].Messages);
            Assert.Equal(new[] { "INBOX/msg0000.txt", "INBOX/msg0000.wav", "Old/msg0000.WAV", "Old/msg0000.txt" }, plan.Mailboxes[0].Files);
            Assert.Equal(3, plan.VoicemailMessages);
            Assert.DoesNotContain(plan.Warnings, w => w.Why.Contains("already existed"));
            Assert.Contains(plan.Warnings, w => w.What.Contains("mailbox 103") && w.Why.Contains("was not imported"));
        }

        [Fact]
        public void Sounds_are_one_announcement_per_recording_preferring_the_wav()
        {
            this.WriteFile("files/sounds/en/custom/QB_Main_Line_Greeting.g722");
            this.WriteFile("files/sounds/en/custom/QB_Main_Line_Greeting.wav");
            this.WriteFile("files/sounds/en/custom/Old.gsm");

            var manifest = Manifest();
            manifest.Sounds!.Add(new ManifestSound { Filename = "en/custom/QB_Main_Line_Greeting.g722" });
            manifest.Sounds.Add(new ManifestSound { Filename = "en/custom/QB_Main_Line_Greeting.wav", AnnouncementName = "Main greeting" });
            manifest.Sounds.Add(new ManifestSound { Filename = "en/custom/Old.gsm" });
            manifest.Sounds.Add(new ManifestSound { Filename = "../../../etc/passwd" });

            var existing = new ExistingConfig();
            existing.AnnouncementNames.Add("Main greeting");

            var plan = this.Plan(manifest, existing);

            // The announcement that was here is cleared first (D174), so the name lands as it was.
            Assert.Equal(2, plan.Sounds.Count);
            Assert.Equal("Main greeting", plan.Sounds[0].Announcement.Name);
            Assert.Contains("Main greeting", plan.Cleared.AnnouncementNames);
            Assert.Equal("en/custom/QB_Main_Line_Greeting.wav", plan.Sounds[0].SourceFile);
            Assert.Equal(new[] { "en/custom/QB_Main_Line_Greeting.g722" }, plan.Sounds[0].IgnoredFiles);
            Assert.True(plan.Sounds[0].Convertible);
            Assert.Equal("Old", plan.Sounds[1].Announcement.Name);
            Assert.False(plan.Sounds[1].Convertible);
            Assert.Contains(plan.Warnings, w => w.What == "Sound '../../../etc/passwd' was not imported.");
        }

        [Fact]
        public void The_exporters_own_warnings_are_passed_on()
        {
            var manifest = MigrationManifest.Parse("{\"manifestVersion\":1,\"warnings\":[\"Extension 390 has no SIP secret in sip/pjsip.\"]}");

            var warning = Assert.Single(this.Plan(manifest).Warnings);
            Assert.Equal(MigrationSection.Export, warning.Section);
            Assert.Equal("Extension 390 has no SIP secret in sip/pjsip.", warning.What);
        }

        [Fact]
        public void The_manifest_reads_the_exporters_field_names()
        {
            var manifest = MigrationManifest.Parse("""
                {
                  "manifestVersion": 1,
                  "source": { "distribution": "FreePBX", "version": "16.0.50", "asterisk": "16.30.0" },
                  "extensions": [ { "number": "101", "outboundCallerId": "17147307943", "voicemailEnabled": true } ],
                  "trunks": [ { "name": "cc", "serverPort": "5080", "callerIdNumber": "17145059544", "disabledInFreePBX": true } ],
                  "inboundRoutes": [ { "did": "", "callerIdMatch": "1858", "trunkName": null, "mohclass": "default" } ],
                  "phones": [ { "mac": "64167f967324", "lastIp": "10.0.0.1", "line": null, "keys": [] } ]
                }
                """);

            Assert.Equal("16.0.50", manifest.Source!.Version);
            Assert.Equal("17147307943", manifest.Extensions![0].OutboundCallerID);
            Assert.Equal(5080, manifest.Trunks![0].ServerPort);
            Assert.True(manifest.Trunks[0].DisabledInFreePBX);
            Assert.Equal("1858", manifest.InboundRoutes![0].CallerIDMatch);
            Assert.Null(manifest.Phones![0].Line);
            Assert.NotNull(manifest.Sounds);
        }

        [Theory]
        [InlineData("{\"manifestVersion\":2}")]
        [InlineData("{}")]
        [InlineData("not json")]
        public void Only_manifest_version_1_is_read(string json)
        {
            Assert.Throws<MigrationArchiveException>(() => MigrationManifest.Parse(json));
        }
    }
}
