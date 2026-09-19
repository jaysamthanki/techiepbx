namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// The colour of a health tile. Three, deliberately: green means nothing to do, amber means
    /// look at this today, red means calls are not working. A tile with more shades than that is
    /// a tile nobody reads at a glance, which is the only thing this row of cards is for.
    /// </summary>
    public enum TileLevel
    {
        Good,
        Warn,
        Bad,
    }
}
