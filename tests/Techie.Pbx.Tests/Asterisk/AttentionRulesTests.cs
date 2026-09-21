using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Status;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// One test per rule, against a system that is otherwise healthy, so that a finding appearing
    /// here is the rule under test and not a side effect of the fixture.
    ///
    /// <see cref="Healthy"/> is a small but complete PBX: an extension with a phone on it, a trunk
    /// that is registered and routed, and a certificate with two months left. Adding a rule means
    /// adding a test that breaks exactly one thing about it.
    /// </summary>
    public class AttentionRulesTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void A_healthy_system_has_no_findings()
        {
            Assert.Empty(AttentionRules.Evaluate(Healthy(), Now));
        }

        [Fact]
        public void Asterisk_not_answering_is_the_first_thing_said()
        {
            var snapshot = Healthy();
            snapshot.AmiReachable = false;

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Danger, finding.Severity);
            Assert.Equal(FindingSubject.System, finding.Subject);
            Assert.Contains("not answering on AMI", finding.Text);
        }

        /// <summary>
        /// Every trunk state is Unknown when AMI could not be asked, so repeating it once per
        /// trunk would turn one problem into a list of them.
        /// </summary>
        [Fact]
        public void Trunk_states_are_not_reported_while_ami_is_unreachable()
        {
            var snapshot = Healthy();
            snapshot.AmiReachable = false;
            snapshot.TrunkStates["callcentric"] = RegistrationState.Unknown;

            var findings = AttentionRules.Evaluate(snapshot, Now);

            Assert.Single(findings);
            Assert.DoesNotContain(findings, f => f.Subject == FindingSubject.Trunks);
        }

        [Fact]
        public void A_restart_that_has_not_happened_is_a_warning()
        {
            var snapshot = Healthy();
            snapshot.RestartRequired = true;

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Contains("Restart it from the toolbar", finding.Text);
        }

        [Fact]
        public void Config_that_has_not_been_applied_is_only_a_note()
        {
            var snapshot = Healthy();
            snapshot.ConfigPending = true;

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Info, finding.Severity);
            Assert.Equal(FindingSubject.System, finding.Subject);
        }

        [Fact]
        public void A_rejected_trunk_is_a_danger_because_somebody_has_to_fix_the_credentials()
        {
            var snapshot = Healthy();
            snapshot.TrunkStates["callcentric"] = RegistrationState.Rejected;

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Danger, finding.Severity);
            Assert.Equal(FindingSubject.Trunks, finding.Subject);
            Assert.Equal("Trunk callcentric: the provider rejected our credentials.", finding.Text);
        }

        [Theory]
        [InlineData(RegistrationState.NotRegistered)]
        [InlineData(RegistrationState.Unreachable)]
        public void A_trunk_that_is_not_registered_is_a_warning(RegistrationState state)
        {
            var snapshot = Healthy();
            snapshot.TrunkStates["callcentric"] = state;

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Equal("Trunk callcentric is not registered with its provider.", finding.Text);
        }

        /// <summary>A trunk that does not register cannot be unregistered, so there is nothing to say.</summary>
        [Fact]
        public void A_trunk_that_does_not_register_is_not_expected_to_be_registered()
        {
            var snapshot = Healthy();
            snapshot.Trunks[0].Register = false;
            snapshot.TrunkStates["callcentric"] = RegistrationState.NotRegistered;

            Assert.Empty(AttentionRules.Evaluate(snapshot, Now));
        }

        [Fact]
        public void A_trunk_nothing_routes_over_carries_no_calls()
        {
            var snapshot = Healthy();
            snapshot.InboundRoutes.Clear();

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Info, finding.Severity);
            Assert.Equal("Trunk callcentric has no routes, so it carries no calls.", finding.Text);
        }

        [Fact]
        public void An_outbound_route_is_enough_to_make_a_trunk_used()
        {
            var snapshot = Healthy();
            snapshot.InboundRoutes.Clear();
            snapshot.OutboundRoutes.Add(new OutboundRoute
            {
                DialPattern = "_1NXXNXXXXXX",
                Enabled = true,
                Name = "national",
                OutboundRouteID = 1,
                TrunkID = 1,
            });

            Assert.Empty(AttentionRules.Evaluate(snapshot, Now));
        }

        [Fact]
        public void An_inbound_route_pointing_at_a_deleted_extension_is_a_warning()
        {
            var snapshot = Healthy();
            snapshot.InboundRoutes[0].DestinationValue = "1099";

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Equal(FindingSubject.InboundRoutes, finding.Subject);
            Assert.Equal(
                "Inbound route 19498800842 sends calls to Extension:1099, which no longer exists.",
                finding.Text);
        }

        /// <summary>
        /// A disabled extension is not in the generated dialplan, so a route still pointing at one
        /// is as broken as a route pointing at a deleted one (D54's rule, seen from the route).
        /// </summary>
        [Fact]
        public void A_route_pointing_at_a_disabled_extension_is_a_warning_too()
        {
            var snapshot = Healthy();
            snapshot.Extensions[0].Enabled = false;

            Assert.Contains(
                AttentionRules.Evaluate(snapshot, Now),
                finding => finding.Subject == FindingSubject.InboundRoutes);
        }

        [Fact]
        public void A_route_that_hangs_up_always_resolves()
        {
            var snapshot = Healthy();
            snapshot.InboundRoutes[0].DestinationType = "Hangup";
            snapshot.InboundRoutes[0].DestinationValue = "";

            Assert.Empty(AttentionRules.Evaluate(snapshot, Now));
        }

        /// <summary>A route nobody has switched on sends no calls anywhere, so it is not checked.</summary>
        [Fact]
        public void A_disabled_owner_is_not_checked()
        {
            var snapshot = Healthy();
            snapshot.InboundRoutes[0].DestinationValue = "1099";
            snapshot.InboundRoutes[0].Enabled = false;

            // Only the "no routes" note, because a disabled route does not use its trunk either.
            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSubject.Trunks, finding.Subject);
        }

        [Fact]
        public void A_ring_group_failover_that_no_longer_exists_is_a_warning()
        {
            var snapshot = Healthy();
            snapshot.RingGroups.Add(new RingGroup
            {
                DestinationType = "Extension",
                DestinationValue = "1099",
                Enabled = true,
                Members = "1001",
                Name = "Sales",
                Number = "600",
                RingGroupID = 1,
            });

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSubject.RingGroups, finding.Subject);
            Assert.Equal(
                "Ring group 600 sends unanswered calls to Extension:1099, which no longer exists.",
                finding.Text);
        }

        [Fact]
        public void An_ivr_key_and_its_final_destination_are_both_checked()
        {
            var snapshot = Healthy();
            snapshot.Ivrs.Add(new Ivr
            {
                AnnouncementID = 1,
                DestinationType = "Extension",
                DestinationValue = "1098",
                Enabled = true,
                Entries = new List<IvrEntry>
                {
                    new() { Digit = "1", DestinationType = "Extension", DestinationValue = "1099", IvrID = 1 },
                },
                IvrID = 1,
                Name = "Main menu",
                PlayExtension = "700",
            });

            var findings = AttentionRules.Evaluate(snapshot, Now);

            Assert.Equal(2, findings.Count);
            Assert.All(findings, finding => Assert.Equal(FindingSubject.Ivrs, finding.Subject));
            Assert.Contains(findings, f => f.Text == "IVR Main menu sends key 1 to Extension:1099, which no longer exists.");
            Assert.Contains(findings, f =>
                f.Text == "IVR Main menu sends callers who press nothing to Extension:1098, which no longer exists.");
        }

        [Fact]
        public void A_time_conditions_three_destinations_are_checked()
        {
            var snapshot = Healthy();
            snapshot.TimeConditions.Add(new TimeCondition
            {
                ClosedDestinationType = "Extension",
                ClosedDestinationValue = "1099",
                Enabled = true,
                Name = "Office hours",
                OpenDestinationType = "Extension",
                OpenDestinationValue = "1001",
                PlayExtension = "800",
                TimeConditionID = 1,
            });

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSubject.TimeConditions, finding.Subject);
            Assert.Equal(
                "Time condition Office hours sends closed-hours calls to Extension:1099, which no longer exists.",
                finding.Text);
        }

        /// <summary>
        /// The destination most easily forgotten: one holiday date sending calls somewhere of its
        /// own, inside a condition whose other three destinations are all fine (D64).
        /// </summary>
        [Fact]
        public void A_holiday_override_that_no_longer_exists_is_a_warning()
        {
            var snapshot = Healthy();
            snapshot.TimeConditions.Add(new TimeCondition
            {
                Enabled = true,
                Name = "Office hours",
                PlayExtension = "800",
                Rules = new List<TimeConditionRule>
                {
                    new()
                    {
                        DestinationType = "Extension",
                        DestinationValue = "1099",
                        HolidayDate = "2026-12-25",
                        Kind = TimeConditionRuleKind.Holiday,
                        TimeConditionID = 1,
                    },
                },
                TimeConditionID = 1,
            });

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(
                "Time condition Office hours sends calls on 2026-12-25 to Extension:1099, which no longer exists.",
                finding.Text);
        }

        [Fact]
        public void A_phone_on_a_disabled_extension_cannot_register()
        {
            var snapshot = Healthy();
            snapshot.Extensions[0].Enabled = false;
            snapshot.InboundRoutes[0].DestinationType = "Hangup";
            snapshot.InboundRoutes[0].DestinationValue = "";

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Equal(FindingSubject.Phones, finding.Subject);
            Assert.Equal("Phone Reception is assigned to extension 1001, which is switched off.", finding.Text);
        }

        [Fact]
        public void A_phone_with_no_extension_is_only_a_note()
        {
            var snapshot = Healthy();
            snapshot.PhoneButtons.Clear();
            snapshot.Phones[0].Name = "";

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Info, finding.Severity);
            Assert.Equal("Phone 0004f2aabbcc has no extension assigned.", finding.Text);
        }

        /// <summary>
        /// A line key holds the extension's number, not a foreign key, so deleting the extension
        /// leaves a phone registering as a number that is not there any more (schema 020). Nothing
        /// else would say so: the phones table would just show the number.
        /// </summary>
        [Fact]
        public void A_phone_registering_as_an_extension_that_has_gone_is_a_warning()
        {
            var snapshot = Healthy();
            snapshot.Extensions.Clear();
            snapshot.InboundRoutes[0].DestinationType = "Hangup";
            snapshot.InboundRoutes[0].DestinationValue = "";

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Equal(FindingSubject.Phones, finding.Subject);
            Assert.Equal("Phone Reception registers as extension 1001, which no longer exists.", finding.Text);
        }

        [Fact]
        public void No_usable_certificate_means_the_admin_ui_is_plain_http()
        {
            var snapshot = Healthy();
            snapshot.Certificates.Clear();

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Equal(FindingSubject.Certificates, finding.Subject);
            Assert.Contains("plain HTTP", finding.Text);
        }

        [Fact]
        public void A_certificate_about_to_expire_is_said_out_loud()
        {
            var snapshot = Healthy();
            snapshot.Certificates[0].ExpiresUtc = Now.AddDays(9).ToString("u");

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal("Certificate pbx.example.com expires in 9 days.", finding.Text);
        }

        [Fact]
        public void A_failed_order_is_reported_even_while_a_good_certificate_is_being_served()
        {
            var snapshot = Healthy();
            snapshot.Certificates[0].LastError = "the DNS name did not resolve";

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(
                "Certificate pbx.example.com: the last order failed: the DNS name did not resolve",
                finding.Text);
        }

        [Theory]
        [InlineData(79, 0)]
        [InlineData(80, 1)]
        [InlineData(89, 1)]
        public void A_disk_filling_up_is_a_warning(int percent, int expected)
        {
            var snapshot = Healthy();
            snapshot.Disks[0].UsedPercent = percent;

            var findings = AttentionRules.Evaluate(snapshot, Now);

            Assert.Equal(expected, findings.Count);
            Assert.All(findings, finding => Assert.Equal(FindingSeverity.Warning, finding.Severity));
        }

        [Fact]
        public void A_disk_that_is_nearly_full_is_a_danger()
        {
            var snapshot = Healthy();
            snapshot.Disks[0].UsedPercent = 94;

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Danger, finding.Severity);
            Assert.Equal(FindingSubject.System, finding.Subject);
            Assert.Equal("Disk holding /var/lib/tnpbx is 94% full.", finding.Text);
        }

        /// <summary>
        /// Only worth saying when something actually reads the clock: a site with no time
        /// conditions is not being surprised by anything (D65).
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("Etc/UTC")]
        public void Open_hours_with_no_timezone_set_are_a_note(string zone)
        {
            var snapshot = Healthy();
            snapshot.Timezone = zone;
            snapshot.TimeConditions.Add(new TimeCondition
            {
                Enabled = true,
                Name = "Office hours",
                PlayExtension = "800",
                TimeConditionID = 1,
            });

            var finding = Assert.Single(AttentionRules.Evaluate(snapshot, Now));
            Assert.Equal(FindingSeverity.Info, finding.Severity);
            Assert.Equal(FindingSubject.Settings, finding.Subject);
            Assert.Equal("Open hours are evaluated in UTC because no timezone is set.", finding.Text);
        }

        [Fact]
        public void A_system_with_no_time_conditions_is_not_told_about_the_timezone()
        {
            var snapshot = Healthy();
            snapshot.Timezone = "";

            Assert.Empty(AttentionRules.Evaluate(snapshot, Now));
        }

        /// <summary>
        /// Worst first, so that the thing stopping calls is not below a note about a spare phone.
        /// </summary>
        [Fact]
        public void Findings_are_sorted_by_severity()
        {
            var snapshot = Healthy();
            snapshot.ConfigPending = true;
            snapshot.RestartRequired = true;
            snapshot.TrunkStates["callcentric"] = RegistrationState.Rejected;

            var findings = AttentionRules.Evaluate(snapshot, Now);

            Assert.Equal(
                new[] { FindingSeverity.Danger, FindingSeverity.Warning, FindingSeverity.Info },
                findings.Select(finding => finding.Severity));
        }

        /// <summary>
        /// A small PBX with nothing wrong with it: one extension, the phone on it, a registered
        /// trunk with a route over it, and a certificate with two months left.
        /// </summary>
        private static StatusSnapshot Healthy() => new()
        {
            AmiReachable = true,
            Certificates = new List<Certificate>
            {
                new()
                {
                    CertificateID = 1,
                    CertificatePem = "-----BEGIN CERTIFICATE-----",
                    Enabled = true,
                    ExpiresUtc = Now.AddDays(60).ToString("u"),
                    Hostnames = "pbx.example.com",
                    KeyPem = "-----BEGIN PRIVATE KEY-----",
                    Name = "pbx.example.com",
                },
            },
            Disks = new List<DiskUsage>
            {
                new() { Path = "/var/lib/tnpbx", UsedPercent = 41 },
            },
            Extensions = new List<Extension>
            {
                new() { Enabled = true, ExtensionID = 1, Name = "Alice", Number = "1001" },
            },
            InboundRoutes = new List<InboundRoute>
            {
                new()
                {
                    DID = "19498800842",
                    DestinationType = "Extension",
                    DestinationValue = "1001",
                    Enabled = true,
                    InboundRouteID = 1,
                    TrunkID = 1,
                },
            },
            // A phone registers as whatever its line key says, not as a column on its own row
            // (schema 020), so a healthy phone is one with a line on extension 1001.
            PhoneButtons = new List<PhoneButton>
            {
                new() { PhoneButtonID = 1, PhoneID = 1, Position = 1, TargetType = PhoneButtonTarget.Line, TargetValue = "1001" },
            },
            Phones = new List<Phone>
            {
                new() { Enabled = true, Mac = "0004f2aabbcc", Name = "Reception", PhoneID = 1 },
            },
            Timezone = "Europe/London",
            TrunkStates = new Dictionary<string, RegistrationState>(StringComparer.Ordinal)
            {
                ["callcentric"] = RegistrationState.Registered,
            },
            Trunks = new List<Trunk>
            {
                new()
                {
                    Enabled = true,
                    Name = "callcentric",
                    Register = true,
                    ServerHost = "callcentric.com",
                    TrunkID = 1,
                },
            },
        };
    }
}
