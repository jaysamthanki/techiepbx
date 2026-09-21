namespace Techie.Pbx.Web.Pages.Moh
{
    /// <summary>
    /// One line of the music on hold class table: what Asterisk calls it, what it plays, how much
    /// is in it, and whether anything points at it (D122).
    /// </summary>
    public class MohClassRow
    {
        /// <summary>The full path this class plays, as the generated musiconhold.conf names it.</summary>
        public string Directory { get; set; } = "";

        /// <summary>Whether this is the class that ships with the product, which cannot be deleted.</summary>
        public bool IsDefault { get; set; }

        /// <summary>Whether the parking settings name this class, so the table says what uses it.</summary>
        public bool IsParkingClass { get; set; }

        public long MohClassID { get; set; }

        public string Name { get; set; } = "";

        /// <summary>How many tracks the database has in this class.</summary>
        public int Tracks { get; set; }
    }
}
