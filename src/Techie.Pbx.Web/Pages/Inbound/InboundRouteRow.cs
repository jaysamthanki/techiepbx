namespace Techie.Pbx.Web.Pages.Inbound
{
    /// <summary>One line of the inbound routes table.</summary>
    public class InboundRouteRow
    {
        /// <summary>What the destination is called on screen, or a note when it is gone.</summary>
        public string Destination { get; set; } = "";

        public bool DestinationUsable { get; set; }

        /// <summary>The DID, or what a catch-all does instead of having one.</summary>
        public string DID { get; set; } = "";

        public string Description { get; set; } = "";
        public bool Enabled { get; set; }
        public long InboundRouteID { get; set; }

        /// <summary>The trunk's name, or a note when it is not there any more.</summary>
        public string Trunk { get; set; } = "";

        public bool TrunkUsable { get; set; }
    }
}
