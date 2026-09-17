using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// The one place that knows how to send a call somewhere in dialplan (D36). Inbound routes,
    /// IVR keys, ring group failover and the extension fallback all end in "and then the call goes
    /// here", and they all ask this class what to write.
    ///
    /// Pure function, like every other renderer, and every value goes through
    /// <see cref="ConfText.Safe"/> on the way out even though the destination was validated first.
    /// </summary>
    public static class DestinationDialplan
    {
        /// <summary>
        /// The ready-to-write dialplan lines, each already prefixed with " same => n,".
        /// </summary>
        /// <param name="label">
        /// A priority label for the first line, so a Goto can jump to it: "busy" gives
        /// " same => n(busy),...". Null for the usual case of simply carrying on.
        /// </param>
        public static string Lines(Destination destination, string? label = null, VoicemailGreeting greeting = VoicemailGreeting.Unavailable)
        {
            var sb = new StringBuilder();
            var first = label == null ? " same => n," : $" same => n({ConfText.Safe(label, "label")}),";

            foreach (var step in Steps(destination, greeting))
            {
                sb.Append(first).Append(step).Append('\n');
                first = " same => n,";
            }

            return sb.ToString();
        }

        /// <summary>
        /// The applications a call has to run to end up at this destination, in order and without
        /// the dialplan syntax around them. A destination that does not validate throws, the same
        /// way a bad extension row does: the renderers never trust their input (D35).
        /// </summary>
        public static List<string> Steps(Destination destination, VoicemailGreeting greeting = VoicemailGreeting.Unavailable)
        {
            var errors = destination.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException($"Refusing to write destination '{destination.Key}': {string.Join(" ", errors)}");

            var value = ConfText.Safe(destination.Value, "destination");

            return destination.Type switch
            {
                // Into the same context the phones call from, so an inbound call reaches the
                // extension by exactly the route an internal call takes: one dialplan entry per
                // extension, voicemail fallback and all (D12).
                DestinationType.Extension =>
                    new List<string> { $"Goto({ExtensionsConfRenderer.InternalContext},{value},1)" },

                // In by the same door as an extension, for the same reason: the group's own entry
                // already knows how to ring it and where to send the call if nobody does (D54).
                DestinationType.RingGroup =>
                    new List<string> { $"Goto({ExtensionsConfRenderer.InternalContext},{value},1)" },

                // And again for an announcement: the play extension's own entry already answers,
                // plays the file and hangs up, so there is one description of what an announcement
                // does rather than two that drift (D56). An announcement with no play extension
                // never becomes a destination, which is why there is always somewhere to Goto.
                DestinationType.Announcement =>
                    new List<string> { $"Goto({ExtensionsConfRenderer.InternalContext},{value},1)" },

                // And once more for an IVR: its play extension's entry in the internal context is
                // the one door into the menu, so a destination, an inbound route and a user
                // dialling the number all arrive the same way (D59).
                DestinationType.Ivr =>
                    new List<string> { $"Goto({ExtensionsConfRenderer.InternalContext},{value},1)" },

                DestinationType.Voicemail =>
                    new List<string>
                    {
                        $"VoiceMail({value}@{VoicemailConfRenderer.MailboxContext},{Option(greeting)})",
                        "Hangup()",
                    },

                DestinationType.Hangup =>
                    new List<string> { "Hangup()" },

                _ => throw new InvalidOperationException($"No dialplan is known for destination type '{destination.Type}'."),
            };
        }

        private static string Option(VoicemailGreeting greeting) =>
            greeting == VoicemailGreeting.Busy ? "b" : "u";
    }
}
