namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// The four outcomes the report filters and labels by (F5), each one or more of Asterisk's own
    /// dispositions. Stored rows keep Asterisk's word; this is only how it is read.
    /// </summary>
    public enum CallStatus
    {
        Answered,
        Missed,
        Busy,
        Failed,
    }

    /// <summary>Asterisk's dispositions, and which <see cref="CallStatus"/> each one is.</summary>
    public static class CallStatuses
    {
        public const string Answered = "ANSWERED";
        public const string Busy = "BUSY";

        /// <summary>Only written when cdr.conf turns canceldispositionenabled on, which ours does not.</summary>
        public const string Cancel = "CANCEL";

        /// <summary>Only written when cdr.conf turns congestion on, which ours does not.</summary>
        public const string Congestion = "CONGESTION";

        public const string Failed = "FAILED";
        public const string NoAnswer = "NO ANSWER";

        /// <summary>
        /// The dispositions an answered, missed or busy record can have. Failed is everything
        /// else, so it is filtered as "none of these" rather than by a list of its own.
        /// </summary>
        public static IReadOnlyList<string> Dispositions(CallStatus status) => status switch
        {
            CallStatus.Answered => new[] { Answered },
            CallStatus.Missed => new[] { NoAnswer, Cancel },
            CallStatus.Busy => new[] { Busy },
            _ => new[] { Answered, NoAnswer, Cancel, Busy },
        };

        /// <summary>
        /// The status a disposition belongs to. Anything Asterisk might invent later that is not
        /// an answer counts as failed rather than disappearing from every filter.
        /// </summary>
        public static CallStatus For(string? disposition) => disposition?.Trim().ToUpperInvariant() switch
        {
            Answered => CallStatus.Answered,
            NoAnswer or Cancel => CallStatus.Missed,
            Busy => CallStatus.Busy,
            _ => CallStatus.Failed,
        };
    }
}
