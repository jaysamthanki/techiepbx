namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One call detail record, as cdr_manager reported it over AMI (F5). A record is one leg of a
    /// call, not the whole of it: a ring-all Dial writes one per phone it rang, all sharing the
    /// caller's <see cref="UniqueID"/>. Times are ISO-8601 UTC text, the way they are stored.
    /// </summary>
    public class Cdr
    {
        public string? AccountCode { get; set; }

        public string? AmaFlags { get; set; }

        public string? AnswerUtc { get; set; }

        /// <summary>Seconds from answer to hangup: the talking. Zero when nobody answered.</summary>
        public int? BillSecSeconds { get; set; }

        /// <summary>The whole caller ID, name and number, e.g. "\"Front Desk\" &lt;101&gt;".</summary>
        public string? CallerID { get; set; }

        /// <summary>The caller's channel, e.g. "PJSIP/101-0000001a" or "PJSIP/voipms-0000001b".</summary>
        public string Channel { get; set; } = "";

        public long CdrID { get; set; }

        public string Dcontext { get; set; } = "";

        /// <summary>The called channel, empty when nothing was dialled (voicemail, an IVR).</summary>
        public string? DestinationChannel { get; set; }

        /// <summary>ANSWERED, NO ANSWER, BUSY, FAILED, CONGESTION or CANCEL: Asterisk's own words.</summary>
        public string Disposition { get; set; } = "";

        /// <summary>The dialled number, or the DID for a call from a trunk.</summary>
        public string Dst { get; set; } = "";

        /// <summary>Seconds from start to hangup, ringing included.</summary>
        public int? DurationSeconds { get; set; }

        public string? EndUtc { get; set; }

        public string? LastApplication { get; set; }

        public string? LastData { get; set; }

        /// <summary>The ID every leg of one call shares, when cdr_manager.conf maps it (it does).</summary>
        public string? LinkedID { get; set; }

        /// <summary>The CDR engine's own counter for this record; with UniqueID, what makes it unique.</summary>
        public long? Sequence { get; set; }

        /// <summary>The caller ID number.</summary>
        public string Src { get; set; } = "";

        public string StartUtc { get; set; } = "";

        /// <summary>The Asterisk uniqueid of the caller's channel.</summary>
        public string UniqueID { get; set; } = "";
    }
}
