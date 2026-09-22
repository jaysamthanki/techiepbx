using Microsoft.Data.Sqlite;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Cdr events as cdr_manager writes them in Asterisk 22 (F5), read into records and through
    /// the collector into the database. The header names here are the ones in cdr_manager.c, plus
    /// the LinkedID, Sequence and Did our cdr_manager.conf maps on; the end-to-end check against a real
    /// Asterisk happens on the lab VM.
    /// </summary>
    public class CdrEventTests : IDisposable
    {
        private const string Greeting = "Asterisk Call Manager/9.0.0\r\n";
        private const string LoginAccepted = "Response: Success\r\nActionID: 1\r\nMessage: Authentication accepted\r\n\r\n";

        private static readonly DateTime Received = new(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc);

        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-cdr-event-").FullName;
        private readonly Database database;
        private readonly CdrRepository cdrs;

        public CdrEventTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.cdrs = new CdrRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        /// <summary>
        /// An inbound call answered at 101, as cdr_manager.c formats it, headers in its order. The
        /// trunk context sent the call into internal, so Destination is the extension; the DID is
        /// in the userfield, which cdr_manager.conf also maps on as Did.
        /// </summary>
        private static string AnsweredInbound(string sequence = "42") =>
            "Event: Cdr\r\n" +
            "Privilege: cdr,all\r\n" +
            "AccountCode: \r\n" +
            "Source: 15551234567\r\n" +
            "Destination: 101\r\n" +
            "DestinationContext: internal\r\n" +
            "CallerID: \"ACME Corp\" <15551234567>\r\n" +
            "Channel: PJSIP/voipms-00000012\r\n" +
            "DestinationChannel: PJSIP/101-00000013\r\n" +
            "LastApplication: Dial\r\n" +
            "LastData: PJSIP/101,30,tTkK\r\n" +
            "StartTime: 2026-09-22 14:03:05\r\n" +
            "AnswerTime: 2026-09-22 14:03:11\r\n" +
            "EndTime: 2026-09-22 14:05:11\r\n" +
            "Duration: 126\r\n" +
            "BillableSeconds: 120\r\n" +
            "Disposition: ANSWERED\r\n" +
            "AMAFlags: DOCUMENTATION\r\n" +
            "UniqueID: 1758549785.18\r\n" +
            "UserField: 15557654321\r\n" +
            "LinkedID: 1758549785.18\r\n" +
            $"Sequence: {sequence}\r\n" +
            "Did: 15557654321\r\n" +
            "\r\n";

        private static AmiMessage Message(string packet) =>
            new AmiReader(new StringReader(packet)).ReadMessage()!;

        [Fact]
        public void Every_field_lands_in_its_column()
        {
            var cdr = CdrEvent.ToCdr(Message(AnsweredInbound()), TimeZoneInfo.Utc, Received);

            Assert.Null(cdr.AccountCode);
            Assert.Equal("DOCUMENTATION", cdr.AmaFlags);
            Assert.Equal("2026-09-22T14:03:11Z", cdr.AnswerUtc);
            Assert.Equal(120, cdr.BillSecSeconds);
            Assert.Equal("\"ACME Corp\" <15551234567>", cdr.CallerID);
            Assert.Equal("PJSIP/voipms-00000012", cdr.Channel);
            Assert.Equal("internal", cdr.Dcontext);
            Assert.Equal("PJSIP/101-00000013", cdr.DestinationChannel);
            Assert.Equal("ANSWERED", cdr.Disposition);
            Assert.Equal("15557654321", cdr.Did);
            Assert.Equal("101", cdr.Dst);
            Assert.Equal(126, cdr.DurationSeconds);
            Assert.Equal("2026-09-22T14:05:11Z", cdr.EndUtc);
            Assert.Equal("Dial", cdr.LastApplication);
            Assert.Equal("PJSIP/101,30,tTkK", cdr.LastData);
            Assert.Equal("1758549785.18", cdr.LinkedID);
            Assert.Equal(42, cdr.Sequence);
            Assert.Equal("15551234567", cdr.Src);
            Assert.Equal("2026-09-22T14:03:05Z", cdr.StartUtc);
            Assert.Equal("1758549785.18", cdr.UniqueID);
        }

        /// <summary>
        /// The CDR engine's own names for the fields, in case a version or a mapping spells them
        /// that way: the record still lands in the right columns.
        /// </summary>
        [Fact]
        public void The_cdr_engine_names_for_the_fields_are_read_too()
        {
            var packet =
                "Event: Cdr\r\nSrc: 101\r\nDst: 102\r\nDContext: internal\r\nClid: <101>\r\n" +
                "Channel: PJSIP/101-00000001\r\nDstChannel: PJSIP/102-00000002\r\nLastApp: Dial\r\n" +
                "Start: 2026-09-22 09:00:00\r\nAnswer: \r\nEnd: 2026-09-22 09:00:20\r\n" +
                "Duration: 20\r\nBillSec: 0\r\nDisposition: NO ANSWER\r\nUniqueID: 1758531600.1\r\n\r\n";

            var cdr = CdrEvent.ToCdr(Message(packet), TimeZoneInfo.Utc, Received);

            Assert.Equal("101", cdr.Src);
            Assert.Equal("102", cdr.Dst);
            Assert.Equal("internal", cdr.Dcontext);
            Assert.Equal("PJSIP/102-00000002", cdr.DestinationChannel);
            Assert.Equal("Dial", cdr.LastApplication);
            Assert.Equal(0, cdr.BillSecSeconds);
            Assert.Equal("2026-09-22T09:00:00Z", cdr.StartUtc);
            Assert.Null(cdr.AnswerUtc);
            Assert.Null(cdr.LinkedID);
            Assert.Null(cdr.Sequence);
        }

        /// <summary>
        /// Only an inbound call has a DID: an internal or outbound record carries an empty
        /// userfield, and so does any record from before the mapping, which is null rather than "".
        /// </summary>
        [Fact]
        public void A_record_without_a_did_has_none()
        {
            var packet = AnsweredInbound()
                .Replace("UserField: 15557654321\r\n", "UserField: \r\n")
                .Replace("Did: 15557654321\r\n", "Did: \r\n");
            Assert.Null(CdrEvent.ToCdr(Message(packet), TimeZoneInfo.Utc, Received).Did);

            var missing = AnsweredInbound()
                .Replace("UserField: 15557654321\r\n", "")
                .Replace("Did: 15557654321\r\n", "");
            Assert.Null(CdrEvent.ToCdr(Message(missing), TimeZoneInfo.Utc, Received).Did);
        }

        /// <summary>
        /// The standard event already carries the userfield as UserField, so a record still has its
        /// DID if the Did mapping is ever missing.
        /// </summary>
        [Fact]
        public void The_did_is_read_from_userfield_when_there_is_no_mapping()
        {
            var packet = AnsweredInbound().Replace("Did: 15557654321\r\n", "");

            Assert.Equal("15557654321", CdrEvent.ToCdr(Message(packet), TimeZoneInfo.Utc, Received).Did);
        }

        /// <summary>
        /// cdr_manager writes local time with no zone in it. The server is meant to be on UTC
        /// (D74), but if it is not, the stored time is still UTC.
        /// </summary>
        [Fact]
        public void Times_are_read_in_the_server_zone_and_stored_as_utc()
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("UTC-7", TimeSpan.FromHours(-7), "UTC-7", "UTC-7");

            var cdr = CdrEvent.ToCdr(Message(AnsweredInbound()), zone, Received);

            Assert.Equal("2026-09-22T21:03:05Z", cdr.StartUtc);
            Assert.Equal("2026-09-22T21:03:11Z", cdr.AnswerUtc);
        }

        [Fact]
        public void An_unreadable_start_falls_back_to_the_end_and_then_to_when_it_arrived()
        {
            var noStart = AnsweredInbound().Replace("StartTime: 2026-09-22 14:03:05", "StartTime: garbage");
            Assert.Equal("2026-09-22T14:05:11Z", CdrEvent.ToCdr(Message(noStart), TimeZoneInfo.Utc, Received).StartUtc);

            var noTimes = noStart.Replace("EndTime: 2026-09-22 14:05:11", "EndTime: ");
            Assert.Equal("2026-09-22T18:00:00Z", CdrEvent.ToCdr(Message(noTimes), TimeZoneInfo.Utc, Received).StartUtc);
        }

        [Fact]
        public void A_record_without_a_uniqueid_is_refused()
        {
            var packet = AnsweredInbound().Replace("UniqueID: 1758549785.18\r\n", "");

            Assert.Throws<FormatException>(() => CdrEvent.ToCdr(Message(packet), TimeZoneInfo.Utc, Received));
        }

        /// <summary>
        /// The collector stores Cdr events, ignores every other event on the connection, and keeps
        /// reading past a record it cannot store rather than letting one bad event end it.
        /// </summary>
        [Fact]
        public void The_collector_stores_records_and_survives_a_bad_one()
        {
            var bad = AnsweredInbound().Replace("UniqueID: 1758549785.18\r\n", "");
            var other = "Event: FullyBooted\r\nPrivilege: system,all\r\nStatus: Fully Booted\r\n\r\n";
            var canned = Greeting + LoginAccepted + other + bad + AnsweredInbound("42") + AnsweredInbound("43");

            var session = new AmiSession(new StringReader(canned), new StringWriter());
            session.ReadGreeting();
            session.Login("tnpbx", "not-a-real-secret");

            var collector = new CdrCollector(this.cdrs, () => new AmiSettings(), TimeZoneInfo.Utc);
            collector.Collect(session, CancellationToken.None);

            var stored = this.cdrs.Find(Filters.Everything(), new HashSet<string>());
            Assert.Equal(new long?[] { 43, 42 }, stored.Select(c => c.Sequence).OrderByDescending(s => s));
        }

        /// <summary>The same record twice — same UniqueID, same Sequence — is stored once.</summary>
        [Fact]
        public void The_collector_does_not_store_a_record_twice()
        {
            var collector = new CdrCollector(this.cdrs, () => new AmiSettings(), TimeZoneInfo.Utc);

            Assert.True(collector.Store(Message(AnsweredInbound())));
            Assert.False(collector.Store(Message(AnsweredInbound())));
        }

        [Theory]
        [InlineData(1, 5)]
        [InlineData(2, 10)]
        [InlineData(3, 20)]
        [InlineData(4, 40)]
        [InlineData(5, 60)]
        [InlineData(50, 60)]
        public void Reconnecting_backs_off_to_a_minute(int failures, int seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), CdrCollector.RetryDelay(failures));
        }
    }
}
