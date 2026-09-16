using System.Net.Sockets;
using log4net;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Live registration state per extension number, from the PJSIP contacts Asterisk holds.
    /// The extensions page polls this every few seconds, so <see cref="Read"/> never throws:
    /// a status badge is not worth failing a page over.
    /// </summary>
    public static class RegistrationStatus
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(RegistrationStatus));

        /// <summary>
        /// Matches contacts to the extension numbers we asked about. Pure, so the mapping is
        /// testable without a connection. A contact's endpoint name is the extension number in
        /// our generated config (D19); contacts for anything else are ignored.
        /// </summary>
        public static Dictionary<string, RegistrationState> Map(IEnumerable<PjsipContact> contacts, IEnumerable<string> numbers)
        {
            var states = new Dictionary<string, RegistrationState>(StringComparer.Ordinal);

            foreach (var number in numbers)
                states[number] = RegistrationState.NotRegistered;

            foreach (var contact in contacts)
            {
                if (!states.TryGetValue(contact.Aor, out var known))
                    continue;

                // One extension can have several contacts (a desk phone and a softphone, say).
                // One reachable contact is enough to call the extension registered.
                if (known != RegistrationState.Registered)
                    states[contact.Aor] = StateFor(contact.Status);
            }

            return states;
        }

        /// <summary>
        /// Asks Asterisk over AMI which of these extensions have a contact right now. If AMI is
        /// unreachable or refuses, every extension comes back <see cref="RegistrationState.Unknown"/>
        /// and the reason is logged.
        /// </summary>
        public static Dictionary<string, RegistrationState> Read(AmiSettings ami, IEnumerable<string> numbers)
        {
            var wanted = numbers.ToList();

            try
            {
                using var client = new AmiClient(ami);
                var session = client.Connect();
                return Map(session.ShowContacts(), wanted);
            }
            catch (Exception ex) when (ex is AmiException or IOException or SocketException)
            {
                Log.Warn($"Could not read registration status over AMI: {ex.Message}");

                var unknown = new Dictionary<string, RegistrationState>(StringComparer.Ordinal);
                foreach (var number in wanted)
                    unknown[number] = RegistrationState.Unknown;

                return unknown;
            }
        }

        /// <summary>
        /// A contact exists, so the phone has registered. Only an explicit "Unreachable" means
        /// qualify stopped getting answers; "Reachable", "NonQualified" (qualify switched off)
        /// and "Unknown" (not probed yet) all mean the extension is usable.
        /// </summary>
        private static RegistrationState StateFor(string contactStatus) =>
            string.Equals(contactStatus, "Unreachable", StringComparison.OrdinalIgnoreCase)
                ? RegistrationState.Unreachable
                : RegistrationState.Registered;
    }
}
