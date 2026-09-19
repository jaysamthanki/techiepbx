using System.Globalization;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// What Asterisk says about itself: when it started, when it last reloaded, and how many calls
    /// it has up. The answer to a plain Action: CoreStatus, which is a response rather than an
    /// event list, so there is nothing to collect.
    ///
    /// The dates and times arrive as two headers each, in the server's own words, and are kept
    /// that way as well as parsed: if a build of Asterisk ever writes them differently, the page
    /// can still show what it was told.
    /// </summary>
    public class CoreStatus
    {
        /// <summary>How many calls Asterisk counts right now. Its own count, not ours.</summary>
        public int CoreCurrentCalls { get; set; }

        /// <summary>"2026-09-18", or empty when Asterisk has not reloaded since it started.</summary>
        public string CoreReloadDate { get; set; } = "";

        public string CoreReloadTime { get; set; } = "";

        /// <summary>"2026-09-18".</summary>
        public string CoreStartupDate { get; set; } = "";

        /// <summary>"07:14:02".</summary>
        public string CoreStartupTime { get; set; } = "";

        /// <summary>
        /// When Asterisk started, or null when the pair could not be read. Taken as UTC because
        /// the server's clock is UTC by design and every time condition is written against that
        /// (D74); a box whose clock is not UTC would only make the uptime wrong, never a call.
        /// </summary>
        public DateTimeOffset? StartedUtc => Parse(this.CoreStartupDate, this.CoreStartupTime);

        public static CoreStatus FromResponse(AmiMessage message) => new()
        {
            CoreCurrentCalls = int.TryParse(message.Get("CoreCurrentCalls"), out var calls) ? calls : 0,
            CoreReloadDate = message.Get("CoreReloadDate") ?? "",
            CoreReloadTime = message.Get("CoreReloadTime") ?? "",
            CoreStartupDate = message.Get("CoreStartupDate") ?? "",
            CoreStartupTime = message.Get("CoreStartupTime") ?? "",
        };

        /// <summary>
        /// The two headers as one instant. Exact formats rather than a general parse: anything
        /// that is not what Asterisk sends is a header we have misunderstood, and guessing at it
        /// would show an uptime of years.
        /// </summary>
        private static DateTimeOffset? Parse(string date, string time)
        {
            if (date.Length == 0 || time.Length == 0)
                return null;

            return DateTimeOffset.TryParseExact(
                $"{date} {time}",
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value)
                ? value
                : null;
        }
    }
}
