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
        /// <summary>
        /// Play a recorded message and hang up. The value is the announcement's play extension,
        /// which is why an announcement without one cannot be a destination (D56).
        /// </summary>
        Announcement,

        /// <summary>Ring a phone. The value is the extension number.</summary>
        Extension,

        /// <summary>End the call. Needs no value.</summary>
        Hangup,

        /// <summary>
        /// Send the caller to a menu. The value is the IVR's play extension, which is why an IVR
        /// without one cannot be a destination (D59).
        /// </summary>
        Ivr,

        /// <summary>Ring several phones. The value is the ring group's number (D54).</summary>
        RingGroup,

        /// <summary>
        /// Send the caller on by the time of day: open hours one way, closed another, holidays a
        /// third. The value is the condition's play extension, which is why one without a play
        /// extension cannot be a destination (D63).
        /// </summary>
        TimeCondition,

        /// <summary>Take a message. The value is the number of the extension that owns the box.</summary>
        Voicemail,
    }
}
