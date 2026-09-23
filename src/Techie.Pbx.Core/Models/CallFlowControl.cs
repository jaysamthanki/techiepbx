using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The human override for business-hours routing (F9): "the secretary is leaving early, flip
    /// us to night mode". A destination with two destinations of its own — normal while the switch
    /// is off, override while it is on — and a feature code any phone can dial to flip it.
    ///
    /// <b>The state is not here.</b> Whether the switch is on lives in Asterisk's database under
    /// <see cref="StateFamily"/>/<see cref="StateKey"/>, written by the dialplan the moment a phone
    /// dials the code, so a flip needs no apply and no app. What this class describes is the
    /// switch; which way it is set is Asterisk's to know and the page's to ask.
    /// </summary>
    public partial class CallFlowControl
    {
        /// <summary>The astdb family every switch's state is kept under.</summary>
        public const string StateFamily = "TNPBX";

        /// <summary>What the state key holds once a phone has switched it back off.</summary>
        public const string StateOff = "0";

        /// <summary>What the state key holds while the switch is on. Anything else, or nothing, is off.</summary>
        public const string StateOn = "1";

        public long CallFlowControlID { get; set; }

        /// <summary>
        /// The dialplan context the normal-or-override check lives in. One per switch, named by ID
        /// for the reason an IVR's and a time condition's are (D59, D63): the ID never changes.
        /// </summary>
        public string Context => "cfc-" + this.Id();

        /// <summary>
        /// The custom device whose state a phone key's lamp follows (D121): lit while the switch
        /// is on. By ID, like the state key, so changing the code does not reset the lamp.
        /// </summary>
        public string DeviceName => "Custom:tnpbx-cfc-" + this.Id();

        /// <summary>What a phone dials to flip the switch: a star and two or three digits.</summary>
        public string FeatureCode { get; set; } = "";

        /// <summary>What the switch is called, e.g. "Night mode". Unique.</summary>
        public string Name { get; set; } = "";

        /// <summary>The kind of destination while the switch is off, by name (D35).</summary>
        public string NormalDestinationType { get; set; } = DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string NormalDestinationValue { get; set; } = "";

        /// <summary>The kind of destination while the switch is on, by name (D35).</summary>
        public string OverrideDestinationType { get; set; } = DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string OverrideDestinationValue { get; set; } = "";

        /// <summary>
        /// The astdb key under <see cref="StateFamily"/>, so the full name is
        /// <c>TNPBX/CFC/&lt;ID&gt;</c>. By ID rather than code, so changing the code keeps the state.
        /// </summary>
        public string StateKey => "CFC/" + this.Id();

        /// <summary>A star and two or three digits: the shape of every call flow control's code.</summary>
        public static bool IsValidFeatureCode(string? code) => code != null && FeatureCodePattern().IsMatch(code);

        /// <summary>The two normal columns as the one string the picker posts (D35).</summary>
        public string NormalDestinationKey() =>
            this.NormalDestinationValue.Length == 0
                ? this.NormalDestinationType
                : $"{this.NormalDestinationType}:{this.NormalDestinationValue}";

        /// <summary>The two override columns as the one string the picker posts (D35).</summary>
        public string OverrideDestinationKey() =>
            this.OverrideDestinationValue.Length == 0
                ? this.OverrideDestinationType
                : $"{this.OverrideDestinationType}:{this.OverrideDestinationValue}";

        /// <summary>The switch as a destination, for the catalog and the dialplan: its feature code.</summary>
        public Destination ToDestination() => new(DestinationType.CallFlowControl, this.FeatureCode);

        /// <summary>
        /// Where a call goes while the switch is off. Never null: a pair that does not read back
        /// becomes Hangup, because a call has to end somewhere and nowhere is worse than here.
        /// </summary>
        public Destination ToNormalDestination() =>
            Destination.TryParse(this.NormalDestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>Where a call goes while the switch is on.</summary>
        public Destination ToOverrideDestination() =>
            Destination.TryParse(this.OverrideDestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(this.Name))
                errors.Add("Name is required.");
            else if (this.Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(this.Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            // Whether the code is already the system's own, or another switch's, needs the
            // settings and the other rows: the repository asks that.
            if (!IsValidFeatureCode(this.FeatureCode))
                errors.Add("Feature code must be a * followed by 2 or 3 digits, e.g. *28.");

            this.ValidateDestination(errors, this.NormalDestinationKey(), "normally");
            this.ValidateDestination(errors, this.OverrideDestinationKey(), "while it is switched on");

            return errors;
        }

        [GeneratedRegex(@"^\*[0-9]{2,3}\z")]
        private static partial Regex FeatureCodePattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();

        private string Id() => this.CallFlowControlID.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// One destination of this switch: it has to read back, and it must not be this very
        /// switch. Another switch is fine — that is how two switches chain — but pointing at
        /// yourself is a call that never leaves the context, whichever way you are set.
        /// </summary>
        private void ValidateDestination(List<string> errors, string key, string when)
        {
            if (!Destination.TryParse(key, out var destination))
            {
                errors.Add($"Choose where a call goes {when}.");
                return;
            }

            if (destination.Type == DestinationType.CallFlowControl &&
                string.Equals(destination.Value, this.FeatureCode, StringComparison.Ordinal))
            {
                errors.Add($"A call flow control cannot send a call back to itself {when}.");
            }
        }
    }
}
