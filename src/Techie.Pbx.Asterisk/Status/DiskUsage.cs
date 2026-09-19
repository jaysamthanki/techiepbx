namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// How full the disk holding one path is. Measured by the caller, because asking the operating
    /// system is I/O and everything in this folder is a pure function over what it is handed.
    ///
    /// A percentage and nothing else: an appliance that is filling up needs one number and a page
    /// that says so, not a graph of it over time.
    /// </summary>
    public class DiskUsage
    {
        /// <summary>The path that was asked about, which is what an admin recognises.</summary>
        public string Path { get; set; } = "";

        /// <summary>How much of the filesystem holding it is in use, 0 to 100.</summary>
        public int UsedPercent { get; set; }
    }
}
