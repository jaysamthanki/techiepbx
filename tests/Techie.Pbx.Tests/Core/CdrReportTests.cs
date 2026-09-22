using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The pure half of the call reports (F5): reading channel names, deriving direction, the
    /// totals, the status each disposition belongs to, and the CSV export.
    /// </summary>
    public class CdrReportTests
    {
        private static readonly IReadOnlySet<string> Trunks =
            new HashSet<string>(StringComparer.Ordinal) { "voipms", "call-centric" };

        private static Cdr Record(string channel, string? destination, string disposition = CallStatuses.Answered) => new()
        {
            Channel = channel,
            DestinationChannel = destination,
            Disposition = disposition,
            StartUtc = "2026-09-22T10:00:00Z",
            UniqueID = "1.1",
        };

        [Theory]
        [InlineData("PJSIP/101-0000001a", "101")]
        [InlineData("PJSIP/voipms-0000001b", "voipms")]
        [InlineData("PJSIP/call-centric-0000001c", "call-centric")]
        [InlineData("pjsip/101-0000001a", "101")]
        [InlineData("PJSIP/101", "101")]
        [InlineData("Local/5551234@internal-00000001;2", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        [InlineData("PJSIP/", null)]
        public void The_endpoint_is_the_channel_name_without_its_counter(string? channel, string? expected)
        {
            Assert.Equal(expected, CdrChannels.Endpoint(channel));
        }

        [Fact]
        public void A_call_from_a_trunk_is_inbound()
        {
            var cdr = Record("PJSIP/voipms-00000001", "PJSIP/101-00000002");

            Assert.Equal(CallDirection.Inbound, CdrChannels.Direction(cdr, Trunks));
            Assert.Equal("voipms", CdrChannels.Trunk(cdr, Trunks));
        }

        /// <summary>A trunk name with a dash in it is still found: the counter is after the last one.</summary>
        [Fact]
        public void A_call_to_a_trunk_is_outbound()
        {
            var cdr = Record("PJSIP/101-00000001", "PJSIP/call-centric-00000002");

            Assert.Equal(CallDirection.Outbound, CdrChannels.Direction(cdr, Trunks));
            Assert.Equal("call-centric", CdrChannels.Trunk(cdr, Trunks));
        }

        [Fact]
        public void A_call_between_extensions_is_internal()
        {
            var cdr = Record("PJSIP/101-00000001", "PJSIP/102-00000002");

            Assert.Equal(CallDirection.Internal, CdrChannels.Direction(cdr, Trunks));
            Assert.Null(CdrChannels.Trunk(cdr, Trunks));
        }

        /// <summary>Voicemail, an IVR: nothing was dialled, so there is no destination channel.</summary>
        [Fact]
        public void A_call_that_dialled_nothing_is_internal_unless_it_came_from_a_trunk()
        {
            Assert.Equal(CallDirection.Internal, CdrChannels.Direction(Record("PJSIP/101-00000001", null), Trunks));
            Assert.Equal(CallDirection.Inbound, CdrChannels.Direction(Record("PJSIP/voipms-00000001", ""), Trunks));
        }

        /// <summary>
        /// A forwarded call leaves through a Local channel (D130). Its second half, Local;2 to the
        /// trunk, is outbound; the first half, trunk to Local, is inbound.
        /// </summary>
        [Fact]
        public void The_halves_of_a_forwarded_call_read_as_inbound_and_outbound()
        {
            Assert.Equal(CallDirection.Inbound,
                CdrChannels.Direction(Record("PJSIP/voipms-00000001", "Local/5551234@internal-00000001;1"), Trunks));
            Assert.Equal(CallDirection.Outbound,
                CdrChannels.Direction(Record("Local/5551234@internal-00000001;2", "PJSIP/voipms-00000003"), Trunks));
        }

        /// <summary>A trunk name is case sensitive in pjsip.conf, and so it is here.</summary>
        [Fact]
        public void Only_a_trunk_that_exists_makes_a_call_external()
        {
            Assert.Equal(CallDirection.Internal, CdrChannels.Direction(Record("PJSIP/VOIPMS-00000001", "PJSIP/101-00000002"), Trunks));
        }

        [Theory]
        [InlineData("ANSWERED", CallStatus.Answered)]
        [InlineData("NO ANSWER", CallStatus.Missed)]
        [InlineData("CANCEL", CallStatus.Missed)]
        [InlineData("BUSY", CallStatus.Busy)]
        [InlineData("FAILED", CallStatus.Failed)]
        [InlineData("CONGESTION", CallStatus.Failed)]
        [InlineData("UNKNOWN", CallStatus.Failed)]
        [InlineData(null, CallStatus.Failed)]
        public void Every_disposition_has_a_status(string? disposition, CallStatus expected)
        {
            Assert.Equal(expected, CallStatuses.For(disposition));
        }

        /// <summary>
        /// An inbound call that rang 101 and 102 (ring all), answered at 101, and an outbound call
        /// from 102 nobody answered. The trunk had one inbound call answered and one not; 102 missed
        /// one, but the call it made that went unanswered is not a call it missed.
        /// </summary>
        [Fact]
        public void Totals_count_answered_and_missed_per_extension_and_trunk()
        {
            var cdrs = new[]
            {
                Record("PJSIP/voipms-00000001", "PJSIP/101-00000002"),
                Record("PJSIP/voipms-00000001", "PJSIP/102-00000003", CallStatuses.NoAnswer),
                Record("PJSIP/102-00000004", "PJSIP/voipms-00000005", CallStatuses.NoAnswer),
            };

            var (extensions, trunks) = CdrTotals.For(cdrs, Trunks);

            Assert.Collection(extensions,
                t => Assert.Equal(("101", 1, 0, 1), (t.Name, t.Answered, t.Missed, t.Total)),
                t => Assert.Equal(("102", 0, 1, 2), (t.Name, t.Answered, t.Missed, t.Total)));

            Assert.Collection(trunks,
                t => Assert.Equal(("voipms", 1, 1, 3), (t.Name, t.Answered, t.Missed, t.Total)));
        }

        [Fact]
        public void Totals_put_extensions_in_numeric_order()
        {
            var cdrs = new[]
            {
                Record("PJSIP/1000-00000001", "PJSIP/99-00000002"),
                Record("PJSIP/200-00000003", null),
            };

            var (extensions, _) = CdrTotals.For(cdrs, Trunks);

            Assert.Equal(new[] { "99", "200", "1000" }, extensions.Select(t => t.Name));
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("a,b", "\"a,b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("two\nlines", "\"two\nlines\"")]
        [InlineData("+15551234567", "+15551234567")]
        [InlineData("-1", "-1")]
        [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
        [InlineData("@SUM(A1)", "'@SUM(A1)")]
        [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
        [InlineData("-2+3", "'-2+3")]
        public void A_csv_field_is_quoted_when_it_must_be_and_never_runs_as_a_formula(string? value, string expected)
        {
            Assert.Equal(expected, CdrCsv.Field(value));
        }

        [Fact]
        public void The_csv_has_a_header_then_a_crlf_line_per_record()
        {
            var cdr = Record("PJSIP/voipms-00000001", "PJSIP/101-00000002");
            cdr.CallerID = "\"ACME, Corp\" <15551234567>";
            cdr.Src = "15551234567";
            cdr.DurationSeconds = 126;
            cdr.Sequence = 42;

            var csv = CdrCsv.Render(new[] { cdr }, Trunks);
            var lines = csv.Split("\r\n");

            Assert.Equal(3, lines.Length);
            Assert.Equal("", lines[2]);
            Assert.StartsWith("StartUtc,AnswerUtc,EndUtc,Direction,Trunk,Src,Dst,CallerID,Disposition,", lines[0]);
            Assert.StartsWith(
                "2026-09-22T10:00:00Z,,,Inbound,voipms,15551234567,,\"\"\"ACME, Corp\"\" <15551234567>\",ANSWERED,126,,",
                lines[1]);
            Assert.EndsWith(",1.1,,42", lines[1]);
            Assert.DoesNotContain("\n", csv.Replace("\r\n", ""));
        }

        /// <summary>
        /// The quick date buttons (user decision, piece 18): weeks run Sunday to Saturday. The 22nd
        /// of September 2026 is a Tuesday.
        /// </summary>
        [Theory]
        [InlineData("2026-09-22", "2026-09-20", "2026-09-26")]
        [InlineData("2026-09-20", "2026-09-20", "2026-09-26")]
        [InlineData("2026-09-26", "2026-09-20", "2026-09-26")]
        [InlineData("2026-10-01", "2026-09-27", "2026-10-03")]
        public void This_week_is_sunday_to_saturday(string today, string from, string to)
        {
            Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), ReportRange.ThisWeek(DateOnly.Parse(today)));
        }

        [Theory]
        [InlineData("2026-09-22", "2026-09-13", "2026-09-19")]
        [InlineData("2026-09-20", "2026-09-13", "2026-09-19")]
        [InlineData("2026-09-26", "2026-09-13", "2026-09-19")]
        [InlineData("2027-01-02", "2026-12-20", "2026-12-26")]
        public void Last_week_is_the_sunday_to_saturday_before(string today, string from, string to)
        {
            Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), ReportRange.LastWeek(DateOnly.Parse(today)));
        }

        [Fact]
        public void Today_and_yesterday_are_single_days()
        {
            var today = new DateOnly(2026, 3, 1);

            Assert.Equal((today, today), ReportRange.Today(today));
            Assert.Equal((new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 28)), ReportRange.Yesterday(today));
        }

        /// <summary>The page opens on today; a range with only one end is that one day.</summary>
        [Fact]
        public void The_report_defaults_to_today()
        {
            var today = new DateOnly(2026, 9, 22);
            var other = new DateOnly(2026, 9, 1);

            Assert.Equal((today, today), ReportRange.Default(null, null, today));
            Assert.Equal((other, other), ReportRange.Default(other, null, today));
            Assert.Equal((other, other), ReportRange.Default(null, other, today));
            Assert.Equal((other, today), ReportRange.Default(other, today, today));
        }
    }
}
