namespace Techie.Pbx.Web.Pages.Routes
{
    /// <summary>One line of the outbound routes table, in the order the routes are tried.</summary>
    public class RouteRow
    {
        public string DialPattern { get; set; } = "";
        public bool Enabled { get; set; }
        public string Name { get; set; } = "";
        public long OutboundRouteID { get; set; }
        public int Priority { get; set; }

        /// <summary>The trunk's name, or a note when it is not there any more.</summary>
        public string Trunk { get; set; } = "";

        /// <summary>Whether this route would be written to the dialplan as things stand.</summary>
        public bool TrunkUsable { get; set; }
    }
}
