namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The kinds of place a call can be sent. Adding a kind is adding a member here and a case in
    /// the dialplan helper: nothing that stores a destination needs a schema change for it (D35).
    ///
    /// Stored by <b>name</b>, never by number, so that reordering this list cannot silently
    /// repoint every inbound route on a live system.
    /// </summary>
    public enum DestinationType
    {
        /// <summary>Ring a phone. The value is the extension number.</summary>
        Extension,

        /// <summary>End the call. Needs no value.</summary>
        Hangup,

        /// <summary>Take a message. The value is the number of the extension that owns the box.</summary>
        Voicemail,
    }
}
