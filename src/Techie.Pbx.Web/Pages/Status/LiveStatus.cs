using Techie.Pbx.Asterisk.Status;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// What one poll of Asterisk found, and the row of tiles it turns into. Everything here came
    /// from a single AMI conversation plus two file checks, because this is fetched every five
    /// seconds for as long as somebody has the page open.
    ///
    /// Nothing is remembered between polls. A tile shows what is true now; what was true a minute
    /// ago is a graph, and a graph is not what this page is for.
    /// </summary>
    public class LiveStatus
    {
        public List<ActiveCall> ActiveCalls { get; set; } = new();

        /// <summary>Days left on the certificate being served, or null when there is none.</summary>
        public int? CertificateDays { get; set; }

        public bool ConfigPending { get; set; }

        /// <summary>How many enabled extensions have a phone registered against them.</summary>
        public int ExtensionsRegistered { get; set; }

        /// <summary>How many extensions are switched on. A disabled one is not in the config.</summary>
        public int ExtensionsTotal { get; set; }

        /// <summary>Whether Asterisk answered at all. When it did not, the live tiles say so.</summary>
        public bool Reachable { get; set; }

        public bool RestartRequired { get; set; }

        /// <summary>When Asterisk started, from CoreStatus, or null when it did not answer.</summary>
        public DateTimeOffset? StartedUtc { get; set; }

        public int TrunksRegistered { get; set; }

        /// <summary>Whether any trunk's provider is refusing our credentials, which is red.</summary>
        public bool TrunksRejected { get; set; }

        /// <summary>How many trunks are switched on and set to register with a provider.</summary>
        public int TrunksTotal { get; set; }

        /// <summary>The six tiles, in the order they are read: is it up, then what it is carrying.</summary>
        public List<StatusTile> Tiles() => new()
        {
            this.AsteriskTile(),
            this.TrunksTile(),
            this.ExtensionsTile(),
            this.CallsTile(),
            this.CertificateTile(),
            this.ConfigTile(),
        };

        private StatusTile AsteriskTile()
        {
            if (!this.Reachable)
                return new StatusTile { Level = TileLevel.Bad, Text = "Not answering", Title = "Asterisk" };

            var uptime = this.StartedUtc is { } started
                ? $"Running, up for {Uptime(DateTimeOffset.UtcNow - started)}"
                : "Running";

            return new StatusTile { Level = TileLevel.Good, Text = uptime, Title = "Asterisk" };
        }

        private StatusTile CallsTile()
        {
            if (!this.Reachable)
                return new StatusTile { Level = TileLevel.Warn, Text = "Unknown", Title = "Active calls" };

            var count = this.ActiveCalls.Count;

            return new StatusTile
            {
                Level = TileLevel.Good,
                Text = count == 0 ? "None" : count.ToString(),
                Title = "Active calls",
            };
        }

        /// <summary>
        /// The certificate Kestrel is serving. Amber rather than red at either end: an appliance
        /// with no certificate still works over plain HTTP (D99), it is just not how it should be
        /// left.
        /// </summary>
        private StatusTile CertificateTile()
        {
            if (this.CertificateDays is not { } days)
                return new StatusTile { Level = TileLevel.Warn, Text = "None", Title = "Certificate" };

            return days <= AttentionRules.ExpiryWarningDays
                ? new StatusTile { Level = TileLevel.Warn, Text = $"Expires in {days} days", Title = "Certificate" }
                : new StatusTile { Level = TileLevel.Good, Text = $"Valid, {days} days", Title = "Certificate" };
        }

        /// <summary>The same two markers the navbar polls for (D43, D104), said as one line.</summary>
        private StatusTile ConfigTile()
        {
            if (this.RestartRequired)
                return new StatusTile { Level = TileLevel.Warn, Text = "Restart required", Title = "Config" };

            return this.ConfigPending
                ? new StatusTile { Level = TileLevel.Warn, Text = "Apply pending", Title = "Config" }
                : new StatusTile { Level = TileLevel.Good, Text = "Up to date", Title = "Config" };
        }

        /// <summary>
        /// Never red: a site with a phone unplugged for the afternoon is not a broken PBX, and a
        /// tile that is red every day is a tile nobody looks at.
        /// </summary>
        private StatusTile ExtensionsTile()
        {
            if (!this.Reachable)
                return new StatusTile { Level = TileLevel.Warn, Text = "Unknown", Title = "Extensions" };

            return new StatusTile
            {
                Level = this.ExtensionsRegistered < this.ExtensionsTotal ? TileLevel.Warn : TileLevel.Good,
                Text = $"{this.ExtensionsRegistered} of {this.ExtensionsTotal} registered",
                Title = "Extensions",
            };
        }

        private StatusTile TrunksTile()
        {
            if (!this.Reachable)
                return new StatusTile { Level = TileLevel.Warn, Text = "Unknown", Title = "Trunks" };

            if (this.TrunksTotal == 0)
                return new StatusTile { Level = TileLevel.Good, Text = "None registering", Title = "Trunks" };

            // Rejected is the one an admin has to act on, so it is the one that goes red: the
            // provider has our credentials and will not accept them (D40).
            var level = this.TrunksRejected
                ? TileLevel.Bad
                : this.TrunksRegistered < this.TrunksTotal ? TileLevel.Warn : TileLevel.Good;

            return new StatusTile
            {
                Level = level,
                Text = $"{this.TrunksRegistered} of {this.TrunksTotal} registered",
                Title = "Trunks",
            };
        }

        /// <summary>
        /// An uptime a person reads, not a duration: two units is as much as anybody wants, and
        /// which two depends on how long it has been up.
        /// </summary>
        private static string Uptime(TimeSpan span)
        {
            if (span.Days > 0)
                return $"{span.Days}d {span.Hours}h";

            return span.Hours > 0 ? $"{span.Hours}h {span.Minutes}m" : $"{span.Minutes}m";
        }
    }
}
