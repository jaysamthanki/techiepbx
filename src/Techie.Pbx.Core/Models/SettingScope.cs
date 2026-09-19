namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// Who actually reads a setting, which is what decides whether changing it has anything to
    /// apply (D103). Read it as "who has to be told": Asterisk has to be handed rewritten conf
    /// files, a phone fetches its own config on its next poll and is told nothing, and the app
    /// itself needs no telling at all because it reads the value fresh every time.
    ///
    /// The scope is a property of the key, not of the page it is edited on, so a key edited from
    /// the Phones page and the same key edited from the Settings page behave the same way.
    /// </summary>
    public enum SettingScope
    {
        /// <summary>
        /// Only this web application reads it. Nothing is generated from it, so there is nothing
        /// to apply and nothing to poll for: the next read picks the new value up.
        /// </summary>
        App,

        /// <summary>
        /// A generated conf file carries it, so the change reaches Asterisk at the next apply and
        /// the apply button has to light up.
        /// </summary>
        Asterisk,

        /// <summary>
        /// A phone's generated config carries it. Those files are generated per request rather
        /// than written to disk, so the change is already live and each phone picks it up at its
        /// next poll (D79) — nothing to apply.
        /// </summary>
        Phones,
    }
}
