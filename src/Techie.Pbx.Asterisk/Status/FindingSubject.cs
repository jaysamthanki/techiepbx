namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// What a finding is about, so the page can send an admin to the right place.
    ///
    /// A subject and not a URL: this project renders conf files and reads AMI, and it has no
    /// business knowing what the web application calls its pages. The mapping from one to the
    /// other lives in the Web project, which is where route names belong.
    /// </summary>
    public enum FindingSubject
    {
        System,
        Trunks,
        Extensions,
        Phones,
        Certificates,
        InboundRoutes,
        RingGroups,
        Ivrs,
        TimeConditions,
        Settings,
    }
}
