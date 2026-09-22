using System.Globalization;
using System.Text;
using Dapper;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Call detail records (F5). Written by the collector as cdr_manager reports each one over AMI,
    /// read by the reports page. Nothing here raises the "apply is due" marker: a call record is
    /// something that happened, not configuration.
    ///
    /// Kept forever for now: there is no delete (user decision, piece 18).
    /// </summary>
    public class CdrRepository
    {
        /// <summary>The stored form of every time in the table: UTC, to the second, sortable as text.</summary>
        public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        private const string Columns =
            "CdrID, UniqueID, Sequence, LinkedID, Src, Dst, Dcontext, CallerID, Channel, DestinationChannel, " +
            "LastApplication, LastData, Disposition, AmaFlags, AccountCode, StartUtc, AnswerUtc, EndUtc, " +
            "DurationSeconds, BillSecSeconds";

        /// <summary>How many UniqueIDs one query asks for, well inside SQLite's limit on parameters.</summary>
        private const int OriginBatch = 500;

        private readonly Database database;

        public CdrRepository(Database database)
        {
            this.database = database;
        }

        /// <summary>
        /// The records a filter selects, newest first. The date range, extension and status are
        /// SQL; the direction is not stored, so it is worked out from the trunk names as the rows
        /// come back (<see cref="CdrChannels"/>).
        /// </summary>
        public List<Cdr> Find(CdrFilter filter, IReadOnlySet<string> trunkNames)
        {
            var errors = filter.Validate();
            if (errors.Count > 0)
                throw new ValidationFailedException(errors);

            var sql = new StringBuilder($"SELECT {Columns} FROM Cdrs WHERE StartUtc >= @from AND StartUtc < @to");
            var parameters = new DynamicParameters();
            parameters.Add("from", Format(filter.FromUtc));
            parameters.Add("to", Format(filter.ToUtc));

            if (filter.Extension != null)
            {
                // The channel match is what finds a call that reached this extension through a
                // ring group or an inbound route, where the dialled number was something else.
                // IsValidExtension has already kept % and _ out of the pattern.
                sql.Append(" AND (Src = @extension OR Dst = @extension" +
                           " OR Channel LIKE @channel OR DestinationChannel LIKE @channel)");
                parameters.Add("extension", filter.Extension);
                parameters.Add("channel", $"PJSIP/{filter.Extension}-%");
            }

            if (filter.Status is { } status)
            {
                // Failed is everything that is none of the others, so an unexpected disposition is
                // still found by some filter.
                sql.Append(status == CallStatus.Failed
                    ? " AND Disposition NOT IN @dispositions"
                    : " AND Disposition IN @dispositions");
                parameters.Add("dispositions", CallStatuses.Dispositions(status));
            }

            sql.Append(" ORDER BY StartUtc DESC, CdrID DESC");

            using var connection = this.database.Open();
            var rows = connection.Query<Cdr>(sql.ToString(), parameters);

            return filter.Direction is { } direction
                ? rows.Where(cdr => CdrChannels.Direction(cdr, trunkNames) == direction).ToList()
                : rows.ToList();
        }

        /// <summary>The stored text for an instant: UTC, to the second.</summary>
        public static string Format(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString(TimeFormat, CultureInfo.InvariantCulture);

        public Cdr? GetByID(long cdrID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Cdr>($"SELECT {Columns} FROM Cdrs WHERE CdrID = @cdrID", new { cdrID });
        }

        /// <summary>
        /// Stores one record and returns whether it was new. A record Asterisk has already sent is
        /// ignored rather than stored twice: UniqueID and Sequence together identify one.
        /// </summary>
        public bool Insert(Cdr cdr)
        {
            if (string.IsNullOrWhiteSpace(cdr.UniqueID))
                throw new ValidationFailedException("A call record without a UniqueID cannot be stored.");

            if (string.IsNullOrWhiteSpace(cdr.StartUtc))
                throw new ValidationFailedException("A call record without a start time cannot be stored.");

            using var connection = this.database.Open();
            var rows = connection.Execute(
                "INSERT OR IGNORE INTO Cdrs (UniqueID, Sequence, LinkedID, Src, Dst, Dcontext, CallerID, Channel, " +
                "DestinationChannel, LastApplication, LastData, Disposition, AmaFlags, AccountCode, StartUtc, " +
                "AnswerUtc, EndUtc, DurationSeconds, BillSecSeconds) " +
                "VALUES (@UniqueID, @Sequence, @LinkedID, @Src, @Dst, @Dcontext, @CallerID, @Channel, " +
                "@DestinationChannel, @LastApplication, @LastData, @Disposition, @AmaFlags, @AccountCode, @StartUtc, " +
                "@AnswerUtc, @EndUtc, @DurationSeconds, @BillSecSeconds)",
                cdr);

            return rows > 0;
        }

        /// <summary>
        /// The first leg of every call among <paramref name="cdrs"/> whose caller was a Local
        /// channel, by UniqueID, for <see cref="CdrParties.For"/>. Read from the whole table rather
        /// than from the records given: a filter can leave the first leg out — an outbound filter
        /// drops the internal leg that says which extension forwarded the call — and who made the
        /// call is still the same.
        /// </summary>
        public Dictionary<string, Cdr> Origins(IEnumerable<Cdr> cdrs)
        {
            var linkedIDs = cdrs.Where(CdrParties.NeedsOrigin).Select(cdr => cdr.LinkedID!).Distinct(StringComparer.Ordinal).ToList();
            var origins = new Dictionary<string, Cdr>(StringComparer.Ordinal);

            if (linkedIDs.Count == 0)
                return origins;

            using var connection = this.database.Open();

            foreach (var batch in linkedIDs.Chunk(OriginBatch))
            {
                // A ring-all leaves one record per phone under the same UniqueID; the caller is the
                // same on all of them, so the first will do.
                var rows = connection.Query<Cdr>(
                    $"SELECT {Columns} FROM Cdrs WHERE UniqueID IN @ids ORDER BY CdrID",
                    new { ids = batch });

                foreach (var row in rows)
                    origins.TryAdd(row.UniqueID, row);
            }

            return origins;
        }

        /// <summary>
        /// Totals per extension and per trunk over exactly the records <see cref="Find"/> returns
        /// for the same filter, so the totals and the list always agree.
        /// </summary>
        public (List<CdrTotal> Extensions, List<CdrTotal> Trunks) Totals(CdrFilter filter, PbxEndpoints endpoints)
        {
            var found = this.Find(filter, endpoints.TrunkNames);
            return CdrTotals.For(found, endpoints, this.Origins(found));
        }
    }
}
