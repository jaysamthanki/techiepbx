namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// How much a finding matters. Declared worst first, so sorting by the enum puts the things
    /// that stop calls above the things that are merely untidy.
    /// </summary>
    public enum FindingSeverity
    {
        /// <summary>Calls are not working, or are about to stop.</summary>
        Danger,

        /// <summary>Something is wrong and somebody should look at it today.</summary>
        Warning,

        /// <summary>Worth knowing, and nothing is broken.</summary>
        Info,
    }
}
