namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Which greeting VoiceMail() plays: Asterisk's "b" and "u" options. A caller who got a busy
    /// tone and a caller nobody picked up for should not hear the same thing (D29).
    /// </summary>
    public enum VoicemailGreeting
    {
        /// <summary>"I am on the phone."</summary>
        Busy,

        /// <summary>"I am not here." Also the right one for a call sent straight to a mailbox.</summary>
        Unavailable,
    }
}
