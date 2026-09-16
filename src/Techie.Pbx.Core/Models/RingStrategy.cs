namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// How a ring group rings its members. Two, because two is what people ask for; more only if
    /// somebody actually needs one (F3). Stored by name, never by number, so that adding a member
    /// here cannot repoint an existing group.
    /// </summary>
    public enum RingStrategy
    {
        /// <summary>Every member's phone rings at once, and the first to pick up gets the call.</summary>
        All,

        /// <summary>One member at a time, in order, until somebody answers or the list runs out.</summary>
        Hunt,
    }
}
