using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// What the call report is narrowed to (F5). The date range is already in UTC: which local day
    /// the admin meant is the page's business, not the repository's.
    /// </summary>
    public partial class CdrFilter
    {
        /// <summary>Only records going this way; null for any.</summary>
        public CallDirection? Direction { get; set; }

        /// <summary>
        /// An extension number, matched against the caller number, the dialled number and either
        /// side's channel, so a call that reached this extension through a ring group or an inbound
        /// route is found too. A trunk name works as well, through the channels. Null for any.
        /// </summary>
        public string? Extension { get; set; }

        /// <summary>The first instant included.</summary>
        public DateTime FromUtc { get; set; }

        /// <summary>Only records with this outcome; null for any.</summary>
        public CallStatus? Status { get; set; }

        /// <summary>The first instant not included.</summary>
        public DateTime ToUtc { get; set; }

        /// <summary>
        /// Whether a string can be an extension or trunk filter: the characters an extension number
        /// or a trunk name is made of and nothing else, which also keeps it clear of the LIKE
        /// wildcards the channel match is written with.
        /// </summary>
        public static bool IsValidExtension(string value) => ExtensionPattern().IsMatch(value);

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (this.ToUtc <= this.FromUtc)
                errors.Add("The end of the date range has to be after its start.");

            if (this.Extension != null && !IsValidExtension(this.Extension))
                errors.Add("An extension filter is an extension number or a trunk name.");

            return errors;
        }

        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-]{0,31}\z")]
        private static partial Regex ExtensionPattern();
    }
}
