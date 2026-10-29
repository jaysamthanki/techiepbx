using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// An AI voice agent (F11, D199): a number that answers a caller and hands the call's audio to
    /// the media service over Asterisk's AudioSocket, where a model listens and a voice replies.
    /// Dialled like an extension, and a destination like an announcement or a time condition, so
    /// an inbound route, an IVR key or a user at a phone all reach it through the same one entry.
    ///
    /// Only the agent's own behaviour lives here — who it is, what it says first, where it hands a
    /// caller on to. The provider keys and the site-wide defaults are settings (D198).
    /// </summary>
    public partial class AiAgent
    {
        /// <summary>The longest greeting, in characters. A sentence or two, not a speech.</summary>
        public const int MaxGreetingLength = 256;

        /// <summary>The longest system prompt, in characters. Every turn sends it, so it is bounded.</summary>
        public const int MaxPromptLength = 4000;

        public long AiAgentID { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// What the agent says the instant the call answers, spoken without a model round trip so
        /// the caller hears something straight away. Empty for none: the agent waits for the
        /// caller to speak first.
        /// </summary>
        public string Greeting { get; set; } = "";

        /// <summary>The OpenRouter model slug for this agent, or empty for the site default (D198).</summary>
        public string Model { get; set; } = "";

        /// <summary>What the agent is called, for the admin and for the dialplan comment.</summary>
        public string Name { get; set; } = "";

        /// <summary>The number people dial to reach the agent, like an extension number.</summary>
        public string Number { get; set; } = "";

        /// <summary>The system prompt: who the agent is, what the business is, what it may do.</summary>
        public string Prompt { get; set; } = "";

        /// <summary>
        /// Where the call continues when the agent hands off, as a <see cref="Destination.Key"/>
        /// ("RingGroup:600", "Extension:1001"), or empty for none. The media service hands off by
        /// closing the socket, and the dialplan carries on to here (D199, D201).
        /// </summary>
        public string TransferTo { get; set; } = "";

        /// <summary>The ElevenLabs voice for this agent, or empty for the site default (D198).</summary>
        public string VoiceId { get; set; } = "";

        /// <summary>Whether a call can be sent here: switched on, and a number to send it to.</summary>
        public bool IsPlayable => this.Enabled && this.Number.Length > 0;

        /// <summary>
        /// The agent's identity on the media socket (D199). Asterisk's <c>AudioSocket()</c> takes a
        /// UUID in string form as its first argument and sends it as the socket's first message;
        /// that UUID is how the media service tells which agent a call belongs to. It is derived
        /// from the row ID — a hash of "tnpbx-ai-agent:{AiAgentID}" cut to 16 bytes — so it is the
        /// same on every apply, survives a renumber, and needs no column of its own.
        /// </summary>
        public string ServiceUuid
        {
            get
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"tnpbx-ai-agent:{this.AiAgentID}"));
                return new Guid(hash.AsSpan(0, 16)).ToString("D");
            }
        }

        /// <summary>Whether a model name is shaped like an OpenRouter slug, e.g. "vendor/model:variant".</summary>
        public static bool IsValidModel(string value) => ModelPattern().IsMatch(value);

        /// <summary>Whether a voice ID is shaped like one ElevenLabs issues.</summary>
        public static bool IsValidVoiceId(string value) => VoiceIdPattern().IsMatch(value);

        /// <summary>The agent as a destination, for the catalog and the dialplan (D199).</summary>
        public Destination ToDestination() => new(DestinationType.AiAgent, this.Number);

        /// <summary>The handoff as a destination, or null when there is none or it does not parse (D201).</summary>
        public Destination? ToTransferDestination() =>
            Destination.TryParse(this.TransferTo, out var destination) ? destination : null;

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

            // The same rule as an extension number, because it is dialled the same way and shares
            // the same context. Collisions with everything else on that context are checked by the
            // repository, which can see them.
            if (!Extension.IsValidNumber(this.Number))
                errors.Add("Number must be 2 to 6 digits.");

            // The prompt never reaches a conf file — it goes to the model — so line breaks are
            // welcome; it only has to be there and be bounded.
            if (string.IsNullOrWhiteSpace(this.Prompt))
                errors.Add("Prompt is required: it is what tells the agent who it is.");
            else if (this.Prompt.Length > MaxPromptLength)
                errors.Add($"Prompt must be {MaxPromptLength} characters or fewer.");

            if (this.Greeting.Length > MaxGreetingLength)
                errors.Add($"Greeting must be {MaxGreetingLength} characters or fewer.");
            else if (this.Greeting.Any(char.IsControl))
                errors.Add("Greeting must be one line of plain text.");

            if (this.VoiceId.Length > 0 && !IsValidVoiceId(this.VoiceId))
                errors.Add("Voice ID may only contain letters, digits and . _ - (up to 64), or be blank for the site default.");

            if (this.Model.Length > 0 && !IsValidModel(this.Model))
                errors.Add("Model may only contain letters, digits and . _ : / - (up to 127), or be blank for the site default.");

            // Anything a call can be sent to, except another agent: an agent handing to an agent
            // (itself included) is a loop nobody means (D201).
            if (this.TransferTo.Length > 0)
            {
                var target = this.ToTransferDestination();

                if (target == null)
                    errors.Add("Transfer to must be a destination, or blank for none.");
                else if (target.Type == DestinationType.AiAgent)
                    errors.Add("An agent cannot transfer a call to an AI agent.");
            }

            return errors;
        }

        [GeneratedRegex(@"^[A-Za-z0-9._:/\-]{1,127}\z")]
        private static partial Regex ModelPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,64}\z")]
        private static partial Regex VoiceIdPattern();
    }
}
