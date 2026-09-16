namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// What an apply did, as one sentence for the page's toast. The file and module lists are
    /// not sent: nothing in the UI renders them, and the applier logs them.
    /// </summary>
    public class ApplyConfigResponse
    {
        public string Summary { get; set; } = "";
    }
}
