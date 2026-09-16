namespace Techie.Pbx.Web.Pages.RingGroups
{
    /// <summary>One line of the ring groups table.</summary>
    public class RingGroupRow
    {
        /// <summary>Where a call nobody answered goes, as the picker labels it.</summary>
        public string Destination { get; set; } = "";

        public bool DestinationUsable { get; set; }
        public bool Enabled { get; set; }

        /// <summary>The members, as numbers, in the order they ring.</summary>
        public string Members { get; set; } = "";

        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
        public long RingGroupID { get; set; }
        public int RingSeconds { get; set; }
        public string Strategy { get; set; } = "";
    }
}
