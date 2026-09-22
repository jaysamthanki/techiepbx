namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// The quick date ranges the call reports offer (F5): today, yesterday, this week and last
    /// week, as first and last day included. Weeks run Sunday to Saturday (user decision, piece 18).
    ///
    /// Worked out on the server from the site's own today rather than in the browser, so a button
    /// means the site's week even for an admin whose laptop is in another zone. The page only copies
    /// the two dates into the inputs.
    /// </summary>
    public static class ReportRange
    {
        /// <summary>
        /// The range to report on, given the days the form sent. With neither, today, which is what
        /// the page opens on; with only one, that day alone.
        /// </summary>
        public static (DateOnly From, DateOnly To) Default(DateOnly? from, DateOnly? to, DateOnly today)
        {
            if (from == null && to == null)
                return Today(today);

            return (from ?? to!.Value, to ?? from!.Value);
        }

        /// <summary>Sunday to Saturday, the week before the one <paramref name="today"/> falls in.</summary>
        public static (DateOnly From, DateOnly To) LastWeek(DateOnly today)
        {
            var (sunday, _) = ThisWeek(today);
            return (sunday.AddDays(-7), sunday.AddDays(-1));
        }

        /// <summary>Sunday to Saturday, the week <paramref name="today"/> falls in.</summary>
        public static (DateOnly From, DateOnly To) ThisWeek(DateOnly today)
        {
            var sunday = today.AddDays(-(int)today.DayOfWeek);
            return (sunday, sunday.AddDays(6));
        }

        /// <summary>The whole of the day, which is what the page opens on.</summary>
        public static (DateOnly From, DateOnly To) Today(DateOnly today) => (today, today);

        /// <summary>The whole of the day before.</summary>
        public static (DateOnly From, DateOnly To) Yesterday(DateOnly today) => (today.AddDays(-1), today.AddDays(-1));
    }
}
