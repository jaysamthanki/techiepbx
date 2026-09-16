namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Where AMI is and how to log in. Asterisk binds AMI to 127.0.0.1 only, so the defaults
    /// are the normal case. Constructed by the caller: this class deliberately doesn't know
    /// where the values are stored.
    /// </summary>
    public class AmiSettings
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5038;
        public string Username { get; set; } = "";
        public string Secret { get; set; } = "";

        /// <summary>
        /// Connect and socket read timeout. 0 waits forever, which is what a long-lived event
        /// reader wants and what a request/response caller does not.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// Returns a list of problems; empty means valid. Never mentions the secret's value.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Host))
                errors.Add("AMI host is required.");

            if (Port is < 1 or > 65535)
                errors.Add("AMI port must be between 1 and 65535.");

            if (string.IsNullOrWhiteSpace(Username))
                errors.Add("AMI username is required.");

            if (string.IsNullOrEmpty(Secret))
                errors.Add("AMI secret is required.");

            if (TimeoutSeconds is < 0 or > 600)
                errors.Add("AMI timeout must be between 0 and 600 seconds (0 waits forever).");

            return errors;
        }
    }
}
