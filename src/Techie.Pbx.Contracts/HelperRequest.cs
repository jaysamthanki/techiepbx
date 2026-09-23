namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// The one object a caller sends. A type from <see cref="HelperMessageTypes"/> and, for
    /// firewall.apply, the rules to open — there is no field here that carries a path, a command
    /// or anything else the Helper would have to interpret (D142).
    /// </summary>
    public class HelperRequest
    {
        /// <summary>The ports to open, for firewall.apply. Empty for every other message.</summary>
        public List<FirewallRule> Rules { get; set; } = new();

        public string Type { get; set; } = "";

        public HelperRequest()
        {
        }

        public HelperRequest(string type)
        {
            this.Type = type;
        }

        public static HelperRequest FirewallApply(IEnumerable<FirewallRule> rules) =>
            new(HelperMessageTypes.FirewallApply) { Rules = rules.ToList() };

        public static HelperRequest FirewallStatus() => new(HelperMessageTypes.FirewallStatus);

        public static HelperRequest Ping() => new(HelperMessageTypes.Ping);

        /// <summary>
        /// Everything wrong with this message, in sentences. Rules on a message that is not an
        /// apply are an error rather than something to ignore: a caller sending them means one
        /// end has misunderstood the other, and quietly dropping them is how that goes unnoticed.
        ///
        /// An apply with no rules at all is allowed, and means exactly what it says — the safety
        /// rules and nothing else, so SSH and the console still work and no phone can reach SIP.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!HelperMessageTypes.IsKnown(this.Type))
            {
                // Nothing else can be judged once the type is unknown, and the type is what the
                // rest of the object means.
                errors.Add($"'{this.Type}' is not a message this helper accepts.");
                return errors;
            }

            if (this.Type != HelperMessageTypes.FirewallApply)
            {
                if (this.Rules.Count > 0)
                    errors.Add($"A {this.Type} message does not carry firewall rules.");

                return errors;
            }

            if (this.Rules.Count > HelperSocket.MaxRules)
                errors.Add($"A firewall apply may carry at most {HelperSocket.MaxRules} rules; this one carries {this.Rules.Count}.");

            for (var index = 0; index < this.Rules.Count; index++)
            {
                foreach (var error in this.Rules[index].Validate())
                    errors.Add($"Rule {index + 1}: {error}");
            }

            return errors;
        }
    }
}
