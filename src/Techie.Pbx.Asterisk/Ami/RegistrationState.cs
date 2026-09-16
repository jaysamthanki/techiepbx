namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Whether an extension's phone is registered with Asterisk right now, as the UI shows it.
    /// </summary>
    public enum RegistrationState
    {
        /// <summary>Asterisk could not be asked, so we genuinely do not know.</summary>
        Unknown,

        /// <summary>Asterisk has no contact for this extension: no phone has registered.</summary>
        NotRegistered,

        /// <summary>A phone is registered, and answering qualify probes if they are switched on.</summary>
        Registered,

        /// <summary>A phone registered but has stopped answering qualify probes.</summary>
        Unreachable,
    }
}
