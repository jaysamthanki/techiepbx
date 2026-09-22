using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Reports
{
    /// <summary>The detail modal: the whole stored record, with its line in the list for the parts worked out from it.</summary>
    public class CallDetail
    {
        public Cdr Cdr { get; set; } = new();

        public CallRow Row { get; set; } = new();

        /// <summary>The zone the times are shown in, named so nobody has to guess.</summary>
        public string Timezone { get; set; } = "";

        public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
    }
}
