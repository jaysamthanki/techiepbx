using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// "A call arriving on this trunk for this number goes there." One row per DID, plus at most
    /// one catch-all per trunk for everything else (D49).
    ///
    /// The destination is stored as the two fields D35 settled on, rather than as a copy of
    /// whatever it points at.
    /// </summary>
    public partial class InboundRoute
    {
        /// <summary>
        /// Takes every call on its trunk that no DID route matched. At most one per trunk, and it
        /// has no DID of its own.
        /// </summary>
        public bool CatchAll { get; set; }

        /// <summary>
        /// The digits the provider puts in the request, matched exactly as stored.
        ///
        /// What a provider actually sends is not something we can know from here: Callcentric may
        /// send 10 digits, 11 with a country code, or the account number. It is matched as a
        /// literal string, so if a provider's idea of the number differs from what an admin typed,
        /// the call falls through to the catch-all rather than being routed somewhere wrong. A
        /// normalisation step may be needed once that is known (D51).
        /// </summary>
        public string DID { get; set; } = "";

        /// <summary>The kind of destination, by name: "Extension", "Voicemail", "Hangup" (D35).</summary>
        public string DestinationType { get; set; } = "";

        /// <summary>What the destination points at, e.g. an extension number. Empty for Hangup.</summary>
        public string DestinationValue { get; set; } = "";

        /// <summary>What this route is for, in an admin's words. Ends up as a dialplan comment.</summary>
        public string Description { get; set; } = "";

        public bool Enabled { get; set; } = true;
        public long InboundRouteID { get; set; }

        /// <summary>
        /// The music on hold class a caller on this route hears whenever they are held, or null for
        /// none — which leaves the channel as Asterisk found it, exactly as every route did before
        /// this existed (D122 amended). A reference, never a copy of the name: renaming a class
        /// renames it here at the next apply, and deleting one puts this back to null.
        /// </summary>
        public long? MohClassID { get; set; }

        public long TrunkID { get; set; }

        /// <summary>The dialplan extension this route answers to: the DID, or any number.</summary>
        public string DialplanExtension => this.CatchAll ? "_X." : this.DID;

        /// <summary>
        /// The stored choice as a destination. Never null: an unreadable pair reads back as
        /// Hangup, because a call has to end somewhere and nowhere is worse than here.
        /// </summary>
        public Destination ToDestination() =>
            Destination.TryParse(this.DestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>The two columns as the one string the picker posts and reads back (D35).</summary>
        public string DestinationKey() =>
            this.DestinationValue.Length == 0 ? this.DestinationType : $"{this.DestinationType}:{this.DestinationValue}";

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (CatchAll)
            {
                if (DID.Length > 0)
                    errors.Add("A catch-all route takes every number, so it has no DID of its own.");
            }
            else if (!DIDPattern().IsMatch(DID))
            {
                errors.Add("DID must be 1 to 15 digits, exactly as the provider sends them.");
            }

            if (Description.Length > 64)
                errors.Add("Description must be 64 characters or fewer.");
            else if (Description.Length > 0 && !DescriptionPattern().IsMatch(Description))
                errors.Add("Description may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (TrunkID <= 0)
                errors.Add("A route needs the trunk the call arrives on.");

            if (!Destination.TryParse(DestinationKey(), out _))
                errors.Add("Choose where calls on this number should go.");

            return errors;
        }

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex DescriptionPattern();

        [GeneratedRegex(@"^[0-9]{1,15}\z")]
        private static partial Regex DIDPattern();
    }
}
