using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Tests
{
    /// <summary>Call report filters the tests share.</summary>
    public static class Filters
    {
        /// <summary>Every record from 2000 to 2100, with nothing else narrowed.</summary>
        public static CdrFilter Everything() => new()
        {
            FromUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }
}
