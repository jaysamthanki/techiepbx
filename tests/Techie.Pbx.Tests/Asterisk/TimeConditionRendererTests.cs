using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What a time condition turns into: one way in from the internal context, and a context of its
    /// own holding the checks — holidays first, then the open hours, then the fall-through that is
    /// "closed" by definition (D63, D64, D65).
    /// </summary>
    public class TimeConditionRendererTests
    {
        private const int MondayToFriday = 0b000_11111;
        private const int Saturday = 0b010_0000;

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

        private static List<Announcement> SampleAnnouncements() => new()
        {
            new Announcement
            {
                AnnouncementID = 1,
                Name = "Closed message",
                PlayExtension = "700",
                AudioFile = "closed-message.wav",
            },
        };

        /// <summary>
        /// Out of order on purpose, plus the two kinds that are deliberately left out: switched off,
        /// and no play extension. The first condition hands "closed" to the second, which is the
        /// whole reason one condition may point at another (D63).
        /// </summary>
        private static List<TimeCondition> SampleTimeConditions() => new()
        {
            new TimeCondition
            {
                TimeConditionID = 2,
                Name = "After hours",
                PlayExtension = "601",
                OpenDestinationType = "Extension",
                OpenDestinationValue = "1002",
                ClosedDestinationType = "Voicemail",
                ClosedDestinationValue = "1002",
                Rules = new List<TimeConditionRule>
                {
                    new()
                    {
                        Kind = TimeConditionRuleKind.Weekly,
                        DaysMask = MondayToFriday,
                        StartTime = "17:00",
                        EndTime = "21:00",
                    },
                },
            },
            new TimeCondition { TimeConditionID = 4, Name = "Switched off hours", PlayExtension = "603", Enabled = false },
            new TimeCondition { TimeConditionID = 5, Name = "No number" },
            new TimeCondition
            {
                TimeConditionID = 3,
                Name = "Always closed",
                Description = "Nothing is ever open on this number",
                PlayExtension = "602",
                ClosedDestinationType = "Announcement",
                ClosedDestinationValue = "700",
            },
            new TimeCondition
            {
                TimeConditionID = 1,
                Name = "Office hours",
                Description = "Nine to five, and Saturday mornings",
                PlayExtension = "600",
                OpenDestinationType = "Extension",
                OpenDestinationValue = "1001",
                ClosedDestinationType = "TimeCondition",
                ClosedDestinationValue = "601",
                HolidayDestinationType = "Announcement",
                HolidayDestinationValue = "700",
                Rules = new List<TimeConditionRule>
                {
                    new()
                    {
                        Kind = TimeConditionRuleKind.Weekly,
                        DaysMask = MondayToFriday,
                        StartTime = "09:00",
                        EndTime = "17:00",
                    },
                    new()
                    {
                        Kind = TimeConditionRuleKind.Weekly,
                        DaysMask = Saturday,
                        StartTime = "09:00",
                        EndTime = "12:30",
                        SortOrder = 1,
                    },

                    // Out of date order, and the one with a destination of its own is not the first.
                    new() { Kind = TimeConditionRuleKind.Holiday, HolidayDate = "2026-12-25" },
                    new() { Kind = TimeConditionRuleKind.Holiday, HolidayDate = "2026-01-01", DestinationType = "Hangup" },
                    new() { Kind = TimeConditionRuleKind.Holiday, HolidayDate = "2026-07-04" },
                },
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(params TimeCondition[] timeConditions) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), SampleAnnouncements(), new List<Ivr>(), timeConditions, "Europe/London");

        [Fact]
        public void Time_conditions_match_expected_file()
        {
            var actual = Render(SampleTimeConditions().ToArray());

            Assert.Equal(Expected("extensions-time-conditions.conf"), actual);
        }

        /// <summary>
        /// A system with no time conditions has to render exactly the dialplan it rendered before
        /// they existed.
        /// </summary>
        [Fact]
        public void A_system_with_no_time_conditions_renders_exactly_what_it_did_before()
        {
            var withEmpty = Render();

            Assert.Equal(
                ExtensionsConfRenderer.Render(
                    SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                    new List<RingGroup>(), SampleAnnouncements()),
                withEmpty);

            Assert.DoesNotContain("[tc-", withEmpty);
        }

        /// <summary>
        /// The one way in: an entry in the internal context, exactly as an announcement and an IVR
        /// get one, so dialling the number to test it and a destination arrive by the same door.
        /// </summary>
        [Fact]
        public void The_play_extension_gotos_the_condition_context()
        {
            Assert.Contains("exten => 600,1,Goto(tc-1,s,1)\n", Render(SampleTimeConditions()[4]));
        }

        [Fact]
        public void A_time_condition_destination_gotos_the_play_extension()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.TimeCondition, "600"));

            Assert.Equal(new[] { $"Goto({ExtensionsConfRenderer.InternalContext},600,1)" }, steps);
        }

        [Fact]
        public void A_time_condition_destination_with_no_number_is_never_written()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Steps(new Destination(DestinationType.TimeCondition, "")));
        }

        /// <summary>
        /// A condition's context can never be a way out to the phone network: it includes nothing,
        /// so nothing in it can fall through to an outbound route (D50's rule, applied again).
        /// </summary>
        [Fact]
        public void A_condition_context_includes_nothing_and_dials_no_trunk()
        {
            var actual = Render(SampleTimeConditions().ToArray());
            var contexts = actual[actual.IndexOf("[tc-1]", StringComparison.Ordinal)..];

            Assert.DoesNotContain("include =>", contexts);
            Assert.DoesNotContain("Dial(PJSIP/", contexts);
        }

        /// <summary>A holiday beats the weekly hours, so it has to be asked about first.</summary>
        [Fact]
        public void Holidays_are_checked_before_the_open_hours()
        {
            var actual = Render(SampleTimeConditions()[4]);
            var holiday = actual.IndexOf("GotoIfTime(*,*,1,jan", StringComparison.Ordinal);
            var open = actual.IndexOf("GotoIfTime(09:00-17:00", StringComparison.Ordinal);

            Assert.NotEqual(-1, holiday);
            Assert.NotEqual(-1, open);
            Assert.True(holiday < open);
        }

        /// <summary>
        /// The holidays come out in date order whatever order the rules arrived in, and the year is
        /// nowhere in the dialplan: GotoIfTime has no field for one, so a holiday recurs (D64).
        /// </summary>
        [Fact]
        public void Holiday_dates_are_written_in_date_order_without_their_year()
        {
            var actual = Render(SampleTimeConditions()[4]);
            var dates = new[] { "GotoIfTime(*,*,1,jan", "GotoIfTime(*,*,4,jul", "GotoIfTime(*,*,25,dec" };
            var positions = dates.Select(d => actual.IndexOf(d, StringComparison.Ordinal)).ToList();

            Assert.DoesNotContain(-1, positions);
            Assert.Equal(positions.OrderBy(p => p), positions);
            Assert.DoesNotContain("2026", actual);
        }

        /// <summary>
        /// A holiday with a destination of its own gets a label of its own; the rest share the
        /// condition's holiday destination, written once.
        /// </summary>
        [Fact]
        public void A_holiday_override_gets_its_own_label_and_the_rest_share_one()
        {
            var actual = Render(SampleTimeConditions()[4]);

            Assert.Contains(" same => n,GotoIfTime(*,*,1,jan,Europe/London?h1)\n", actual);
            Assert.Contains(" same => n,GotoIfTime(*,*,4,jul,Europe/London?holiday)\n", actual);
            Assert.Contains(" same => n,GotoIfTime(*,*,25,dec,Europe/London?holiday)\n", actual);
            Assert.Contains(" same => n(h1),NoOp(Time condition 600 holiday 1 jan to Hangup)\n same => n,Hangup()\n", actual);
            Assert.Contains(" same => n(holiday),NoOp(Time condition 600 holiday to Announcement:700)\n", actual);
        }

        /// <summary>Consecutive days become a range, which is how an admin wrote them down.</summary>
        [Fact]
        public void Weekdays_come_out_as_ranges_and_single_days()
        {
            var actual = Render(SampleTimeConditions()[4]);

            Assert.Contains(" same => n,GotoIfTime(09:00-17:00,mon-fri,*,*,Europe/London?open)\n", actual);
            Assert.Contains(" same => n,GotoIfTime(09:00-12:30,sat,*,*,Europe/London?open)\n", actual);
        }

        [Fact]
        public void Scattered_days_are_written_with_ampersands_and_every_day_is_a_star()
        {
            var scattered = new TimeConditionRule { DaysMask = 0b000_10101 };
            var everyDay = new TimeConditionRule { DaysMask = TimeConditionRule.AllDays };

            Assert.Equal("mon&wed&fri", scattered.DaysField());
            Assert.Equal("*", everyDay.DaysField());
        }

        /// <summary>
        /// Nothing matched means closed, so "closed" is what the chain falls into rather than
        /// something with a check of its own.
        /// </summary>
        [Fact]
        public void Closed_is_the_fall_through_and_needs_no_check()
        {
            var actual = Render(SampleTimeConditions()[4]);

            Assert.Contains(
                " same => n(closed),NoOp(Time condition 600 closed to TimeCondition:601)\n same => n,Goto(internal,601,1)\n",
                actual);
            Assert.DoesNotContain("?closed)", actual);
        }

        /// <summary>A condition with no open hours is not broken: it is always closed (D63).</summary>
        [Fact]
        public void No_open_hours_means_always_closed()
        {
            var actual = Render(SampleTimeConditions()[3]);
            var context = actual[actual.IndexOf("[tc-3]", StringComparison.Ordinal)..];

            Assert.Contains("; No open hours are set, so this condition is always closed.\n", context);
            Assert.DoesNotContain(" same => n,GotoIfTime(", context);
            Assert.DoesNotContain("(open)", context);
            Assert.Contains(" same => n(closed),NoOp(Time condition 602 closed to Announcement:700)\n", context);
        }

        [Fact]
        public void An_empty_destination_hangs_up()
        {
            var condition = new TimeCondition { TimeConditionID = 9, Name = "Nothing set", PlayExtension = "609" };

            Assert.Contains(
                " same => n(closed),NoOp(Time condition 609 closed to Hangup)\n same => n,Hangup()\n",
                Render(condition));
        }

        /// <summary>
        /// The zone is the last argument of every GotoIfTime, not a comment: Asterisk evaluates
        /// the checks in that zone against a UTC clock, so local hours just work (D74).
        /// </summary>
        [Fact]
        public void The_zone_is_the_last_argument_of_every_GotoIfTime()
        {
            var actual = Render(SampleTimeConditions()[4]);

            Assert.Contains("; The hours below are LOCAL time in Europe/London: every GotoIfTime names that zone as\n", actual);
            Assert.DoesNotContain("?open)\n", actual.Replace(",Europe/London?open)\n", ""));
        }

        [Fact]
        public void A_switched_off_condition_is_not_in_the_config_at_all()
        {
            var actual = Render(SampleTimeConditions().ToArray());

            Assert.DoesNotContain("603", actual);
            Assert.DoesNotContain("Switched off hours", actual);
        }

        [Fact]
        public void A_condition_with_no_play_extension_is_left_out()
        {
            Assert.DoesNotContain("No number", Render(SampleTimeConditions().ToArray()));
        }

        [Fact]
        public void The_conditions_come_out_in_play_extension_order()
        {
            var order = ExtensionsConfRenderer.TimeConditionRenderOrder(SampleTimeConditions());

            Assert.Equal(new[] { "600", "601", "602" }, order.Select(t => t.PlayExtension));
        }

        [Fact]
        public void A_condition_that_would_not_validate_is_never_written()
        {
            var condition = SampleTimeConditions()[4];
            condition.Name = "";

            Assert.Throws<InvalidOperationException>(() => Render(condition));
        }

        /// <summary>
        /// Two rules on the same day of the year would write the same GotoIfTime twice and the
        /// second could never be reached. The year is not part of the comparison (D64).
        /// </summary>
        [Fact]
        public void Two_holidays_on_the_same_day_of_the_year_are_never_written()
        {
            var condition = SampleTimeConditions()[4];
            condition.Rules.Add(new TimeConditionRule { Kind = TimeConditionRuleKind.Holiday, HolidayDate = "2027-12-25" });

            Assert.Throws<InvalidOperationException>(() => Render(condition));
        }

        [Fact]
        public void An_open_window_that_ends_before_it_starts_is_never_written()
        {
            var condition = SampleTimeConditions()[4];
            condition.Rules[0].EndTime = "08:00";

            Assert.Throws<InvalidOperationException>(() => Render(condition));
        }

        /// <summary>
        /// A row that reached the database another way still cannot open a section of its own or
        /// comment out what follows.
        /// </summary>
        [Theory]
        [InlineData("Evil\n[evil]\nexten => _X.,1,Dial(PJSIP/1001)")]
        [InlineData("Evil; comment")]
        [InlineData("Evil]")]
        public void Injection_through_a_condition_name_is_refused(string name)
        {
            var condition = SampleTimeConditions()[4];
            condition.Name = name;

            Assert.Throws<InvalidOperationException>(() => Render(condition));
        }
    }
}
