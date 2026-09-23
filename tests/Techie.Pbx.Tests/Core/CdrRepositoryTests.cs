using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Call records in the database (F5): the schema script, the dedupe, and the filters the
    /// reports page applies. The trunk in every test is "voipms" and the extensions are 101-103.
    /// </summary>
    public class CdrRepositoryTests : IDisposable
    {
        private static readonly IReadOnlySet<string> Trunks = new HashSet<string>(StringComparer.Ordinal) { "voipms" };

        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-cdr-").FullName;
        private readonly Database database;
        private readonly CdrRepository cdrs;

        public CdrRepositoryTests()
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

        private static Cdr Record(
            string uniqueID, long? sequence, string start, string channel, string? destination,
            string disposition = CallStatuses.Answered, string src = "", string dst = "", string? linkedID = null) => new()
        {
            Channel = channel,
            DestinationChannel = destination,
            Disposition = disposition,
            Dst = dst,
            LinkedID = linkedID,
            Sequence = sequence,
            Src = src,
            StartUtc = start,
            UniqueID = uniqueID,
        };

        /// <summary>One of each kind: inbound answered, outbound busy, internal missed.</summary>
        private void AddThree()
        {
            this.cdrs.Insert(Record("1.1", 1, "2026-09-20T10:00:00Z", "PJSIP/voipms-00000001", "PJSIP/101-00000002",
                src: "15551234567", dst: "15557654321"));
            this.cdrs.Insert(Record("2.1", 2, "2026-09-21T10:00:00Z", "PJSIP/102-00000003", "PJSIP/voipms-00000004",
                CallStatuses.Busy, src: "102", dst: "15559990000"));
            this.cdrs.Insert(Record("3.1", 3, "2026-09-22T10:00:00Z", "PJSIP/101-00000005", "PJSIP/103-00000006",
                CallStatuses.NoAnswer, src: "101", dst: "103"));
        }

        private List<string> Found(CdrFilter filter) =>
            this.cdrs.Find(filter, Trunks).Select(c => c.UniqueID).ToList();

        [Fact]
        public void The_schema_creates_the_table_and_its_indexes()
        {
            using var connection = this.database.Open();

            Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Cdrs"));

            var indexes = connection.Query<string>("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Cdrs'").ToList();
            Assert.Contains("IX_Cdrs_StartUtc", indexes);
            Assert.Contains("IX_Cdrs_Src", indexes);
            Assert.Contains("IX_Cdrs_Dst", indexes);
        }

        [Fact]
        public void A_record_reads_back_as_it_was_stored()
        {
            var record = Record("1758549785.18", 42, "2026-09-22T14:03:05Z", "PJSIP/voipms-00000012", "PJSIP/101-00000013");
            record.AnswerUtc = "2026-09-22T14:03:11Z";
            record.BillSecSeconds = 120;
            record.LinkedID = "1758549785.18";
            record.Did = "17771234567";

            Assert.True(this.cdrs.Insert(record));

            var stored = this.cdrs.Find(Filters.Everything(), Trunks).Single();
            Assert.Equal(record.AnswerUtc, stored.AnswerUtc);
            Assert.Equal("17771234567", stored.Did);
            Assert.Equal("17771234567", this.cdrs.GetByID(stored.CdrID)!.Did);
            Assert.Equal(120, stored.BillSecSeconds);
            Assert.Equal(42, stored.Sequence);
            Assert.Equal("1758549785.18", stored.LinkedID);
            Assert.Equal(stored.UniqueID, this.cdrs.GetByID(stored.CdrID)!.UniqueID);
        }

        [Fact]
        public void A_record_without_a_did_stores_null()
        {
            this.cdrs.Insert(Record("1.1", 1, "2026-09-22T10:00:00Z", "PJSIP/101-00000001", "PJSIP/102-00000002"));

            Assert.Null(this.cdrs.Find(Filters.Everything(), Trunks).Single().Did);
        }

        /// <summary>
        /// 026 adds the column to a database that already has records: they keep everything they
        /// had and have no DID, and the reports fall back to Dst for them.
        /// </summary>
        [Fact]
        public void The_did_column_is_added_to_existing_records_as_null()
        {
            using (var connection = this.database.Open())
            {
                // Back to how 025 left the table, with one record in it.
                connection.Execute("ALTER TABLE Cdrs DROP COLUMN Did");
                connection.Execute(
                    "INSERT INTO Cdrs (UniqueID, Sequence, Src, Dst, Channel, Disposition, StartUtc) " +
                    "VALUES ('1.1', 1, '15551234567', '17771234567', 'PJSIP/voipms-00000001', 'ANSWERED', '2026-09-20T10:00:00Z')");
                connection.Execute("PRAGMA user_version = 25");
            }

            this.database.Migrate();

            var stored = this.cdrs.Find(Filters.Everything(), Trunks).Single();
            Assert.Equal("17771234567", stored.Dst);
            Assert.Null(stored.Did);

            using var check = this.database.Open();
            Assert.Equal(27, check.ExecuteScalar<long>("PRAGMA user_version"));
        }

        [Fact]
        public void The_same_record_twice_is_stored_once()
        {
            var record = Record("1.1", 7, "2026-09-22T10:00:00Z", "PJSIP/101-00000001", "PJSIP/102-00000002");

            Assert.True(this.cdrs.Insert(record));
            Assert.False(this.cdrs.Insert(record));
            Assert.Single(this.cdrs.Find(Filters.Everything(), Trunks));
        }

        /// <summary>
        /// A ring-all Dial writes a record per phone, all with the caller's UniqueID. Each is its
        /// own record, told apart by Sequence, and all of them are kept.
        /// </summary>
        [Fact]
        public void The_legs_of_one_call_share_a_uniqueid_and_are_all_kept()
        {
            Assert.True(this.cdrs.Insert(Record("1.1", 7, "2026-09-22T10:00:00Z", "PJSIP/voipms-00000001", "PJSIP/101-00000002")));
            Assert.True(this.cdrs.Insert(Record("1.1", 8, "2026-09-22T10:00:00Z", "PJSIP/voipms-00000001", "PJSIP/102-00000003", CallStatuses.NoAnswer)));

            Assert.Equal(2, this.cdrs.Find(Filters.Everything(), Trunks).Count);
        }

        /// <summary>
        /// Without a Sequence — func_cdr missing, say — nothing tells two records apart, and they
        /// are both kept rather than one silently dropped.
        /// </summary>
        [Fact]
        public void Records_without_a_sequence_are_never_dropped_as_duplicates()
        {
            this.cdrs.Insert(Record("1.1", null, "2026-09-22T10:00:00Z", "PJSIP/voipms-00000001", "PJSIP/101-00000002"));
            this.cdrs.Insert(Record("1.1", null, "2026-09-22T10:00:00Z", "PJSIP/voipms-00000001", "PJSIP/102-00000003"));

            Assert.Equal(2, this.cdrs.Find(Filters.Everything(), Trunks).Count);
        }

        [Fact]
        public void A_record_without_a_uniqueid_is_refused()
        {
            Assert.Throws<ValidationFailedException>(() =>
                this.cdrs.Insert(Record("", 1, "2026-09-22T10:00:00Z", "PJSIP/101-00000001", null)));
        }

        [Fact]
        public void Newest_first_within_the_date_range_start_included_end_excluded()
        {
            this.AddThree();

            var filter = Filters.Everything();
            filter.FromUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            filter.ToUtc = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

            Assert.Equal(new[] { "2.1", "1.1" }, this.Found(filter));
            Assert.Equal(new[] { "3.1", "2.1", "1.1" }, this.Found(Filters.Everything()));
        }

        /// <summary>
        /// An extension is found as the caller, as the number dialled, and by its channel — which is
        /// how an inbound call that rang it is found, when the number dialled was a DID.
        /// </summary>
        [Fact]
        public void An_extension_is_found_by_number_and_by_channel()
        {
            this.AddThree();

            var filter = Filters.Everything();

            filter.Extension = "101";
            Assert.Equal(new[] { "3.1", "1.1" }, this.Found(filter));

            filter.Extension = "103";
            Assert.Equal(new[] { "3.1" }, this.Found(filter));

            filter.Extension = "voipms";
            Assert.Equal(new[] { "2.1", "1.1" }, this.Found(filter));
        }

        /// <summary>"10" is not the start of 101's channel name: the match stops at the dash.</summary>
        [Fact]
        public void An_extension_does_not_match_a_longer_one()
        {
            this.AddThree();

            var filter = Filters.Everything();
            filter.Extension = "10";

            Assert.Empty(this.Found(filter));
        }

        [Theory]
        [InlineData("10%")]
        [InlineData("1_1")]
        [InlineData("101' OR 1=1 --")]
        [InlineData("")]
        public void An_extension_filter_that_is_not_an_extension_or_trunk_is_refused(string extension)
        {
            var filter = Filters.Everything();
            filter.Extension = extension;

            Assert.Throws<ValidationFailedException>(() => this.cdrs.Find(filter, Trunks));
        }

        [Fact]
        public void A_backwards_date_range_is_refused()
        {
            var filter = Filters.Everything();
            filter.ToUtc = filter.FromUtc;

            Assert.Throws<ValidationFailedException>(() => this.cdrs.Find(filter, Trunks));
        }

        [Theory]
        [InlineData(CallDirection.Inbound, "1.1")]
        [InlineData(CallDirection.Outbound, "2.1")]
        [InlineData(CallDirection.Internal, "3.1")]
        public void Direction_is_derived_from_the_trunks(CallDirection direction, string expected)
        {
            this.AddThree();

            var filter = Filters.Everything();
            filter.Direction = direction;

            Assert.Equal(new[] { expected }, this.Found(filter));
        }

        /// <summary>
        /// Direction is not stored: a trunk that is renamed or deleted changes how its old records
        /// read, which is the price of deriving it, and what this test pins down.
        /// </summary>
        [Fact]
        public void Direction_follows_the_trunks_as_they_are_now()
        {
            this.AddThree();

            var filter = Filters.Everything();
            filter.Direction = CallDirection.Internal;

            Assert.Equal(3, this.cdrs.Find(filter, new HashSet<string>()).Count);
        }

        [Theory]
        [InlineData(CallStatus.Answered, "1.1")]
        [InlineData(CallStatus.Busy, "2.1")]
        [InlineData(CallStatus.Missed, "3.1")]
        public void Status_filters_by_disposition(CallStatus status, string expected)
        {
            this.AddThree();

            var filter = Filters.Everything();
            filter.Status = status;

            Assert.Equal(new[] { expected }, this.Found(filter));
        }

        /// <summary>Failed covers FAILED, CONGESTION and anything Asterisk might invent later.</summary>
        [Fact]
        public void Failed_is_everything_that_is_not_answered_missed_or_busy()
        {
            this.AddThree();
            this.cdrs.Insert(Record("4.1", 4, "2026-09-22T11:00:00Z", "PJSIP/101-00000007", null, CallStatuses.Failed));
            this.cdrs.Insert(Record("5.1", 5, "2026-09-22T12:00:00Z", "PJSIP/101-00000008", null, CallStatuses.Congestion));
            this.cdrs.Insert(Record("6.1", 6, "2026-09-22T13:00:00Z", "PJSIP/101-00000009", null, "UNKNOWN"));

            var filter = Filters.Everything();
            filter.Status = CallStatus.Failed;

            Assert.Equal(new[] { "6.1", "5.1", "4.1" }, this.Found(filter));
        }

        [Fact]
        public void Totals_count_the_same_records_the_list_shows()
        {
            this.AddThree();

            var filter = Filters.Everything();
            filter.Extension = "101";

            var endpoints = new PbxEndpoints(new Dictionary<string, string>(), Trunks);
            var (extensions, trunks) = this.cdrs.Totals(filter, endpoints);

            Assert.Equal(new[] { "101", "103" }, extensions.Select(t => t.Name));
            Assert.Equal(new[] { "voipms" }, trunks.Select(t => t.Name));
        }

        /// <summary>
        /// 102 called 1003, which forwards to a mobile. The outbound filter keeps only the leg out
        /// over the trunk, and its first leg is still found, so the list can say 102 made the call.
        /// </summary>
        [Fact]
        public void The_first_leg_of_a_forwarded_call_is_found_even_when_the_filter_left_it_out()
        {
            this.cdrs.Insert(Record("7.1", 1, "2026-09-22T10:00:00Z", "PJSIP/102-00000001", "Local/7146085242@internal-00000001;1",
                src: "102", dst: "1003", linkedID: "7.1"));
            this.cdrs.Insert(Record("7.3", 2, "2026-09-22T10:00:01Z", "Local/7146085242@internal-00000001;2", "PJSIP/voipms-00000003",
                src: "17771234567", dst: "7146085242", linkedID: "7.1"));

            var filter = Filters.Everything();
            filter.Direction = CallDirection.Outbound;

            var found = this.cdrs.Find(filter, Trunks);
            var origins = this.cdrs.Origins(found);

            Assert.Equal("7.3", Assert.Single(found).UniqueID);
            Assert.Equal("PJSIP/102-00000001", origins["7.1"].Channel);
            Assert.Empty(this.cdrs.Origins(new[] { origins["7.1"] }));
        }
    }
}
