using Techie.Pbx.Asterisk.Status;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// Which page a finding sends an admin to. The rules name a <see cref="FindingSubject"/> and
    /// nothing else, because the project that reads AMI and renders conf files has no business
    /// knowing what this application calls its pages; this is the one place that knows both.
    /// </summary>
    public static class SubjectPage
    {
        /// <summary>
        /// The page for a subject. System findings go to Settings: AMI, the conf directory and
        /// the timezone are all there, and it is the nearest thing this app has to a page about
        /// the box itself.
        /// </summary>
        public static string For(FindingSubject subject) => subject switch
        {
            FindingSubject.Trunks => "/Trunks/Index",
            FindingSubject.Extensions => "/Extensions/Index",
            FindingSubject.Phones => "/Phones/Index",
            FindingSubject.Certificates => "/Certificates/Index",
            FindingSubject.InboundRoutes => "/Inbound/Index",
            FindingSubject.RingGroups => "/RingGroups/Index",
            FindingSubject.Ivrs => "/Ivrs/Index",
            FindingSubject.TimeConditions => "/TimeConditions/Index",
            _ => "/Settings/Index",
        };
    }
}
