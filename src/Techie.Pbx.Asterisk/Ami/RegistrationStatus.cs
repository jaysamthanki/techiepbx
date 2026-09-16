using System.Net.Sockets;
using log4net;
using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Live registration state, both ways round: phones registering with us (the PJSIP contacts
    /// Asterisk holds, <see cref="Map"/> and <see cref="Read"/>) and us registering with providers
    /// (<see cref="MapTrunks"/> and <see cref="ReadTrunks"/>).
    ///
    /// The pages poll these every few seconds, so neither Read throws: a status badge is not
    /// worth failing a page over.
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
        /// Matches outbound registrations to trunk names. Pure, so the mapping is testable without
        /// a connection. A registration object is named after its trunk with "-reg" on the end
        /// (D39), which is how a registration is matched back to the trunk it belongs to.
        /// </summary>
        public static Dictionary<string, RegistrationState> MapTrunks(IEnumerable<PjsipRegistration> registrations, IEnumerable<string> trunkNames)
        {
            var states = new Dictionary<string, RegistrationState>(StringComparer.Ordinal);

            foreach (var name in trunkNames)
                states[name] = RegistrationState.NotRegistered;

            foreach (var registration in registrations)
            {
                var name = TrunkNameOf(registration.ObjectName);

                if (name != null && states.ContainsKey(name))
                    states[name] = TrunkStateFor(registration.Status);
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
                return AllUnknown(wanted);
            }
        }

        /// <summary>
        /// Asks Asterisk over AMI which trunks are registered with their providers right now. Like
        /// <see cref="Read"/>, an unreachable AMI means Unknown rather than an exception.
        /// </summary>
        public static Dictionary<string, RegistrationState> ReadTrunks(AmiSettings ami, IEnumerable<string> trunkNames)
        {
            var wanted = trunkNames.ToList();

            try
            {
                using var client = new AmiClient(ami);
                var session = client.Connect();
                return MapTrunks(session.ShowRegistrations(), wanted);
            }
            catch (Exception ex) when (ex is AmiException or IOException or SocketException)
            {
                Log.Warn($"Could not read trunk registration status over AMI: {ex.Message}");
                return AllUnknown(wanted);
            }
        }

        private static Dictionary<string, RegistrationState> AllUnknown(IEnumerable<string> names)
        {
            var unknown = new Dictionary<string, RegistrationState>(StringComparer.Ordinal);

            foreach (var name in names)
                unknown[name] = RegistrationState.Unknown;

            return unknown;
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

        /// <summary>
        /// The trunk a registration object belongs to, from the "-reg" name the renderer gives it,
        /// or null for a registration this app did not write.
        /// </summary>
        private static string? TrunkNameOf(string objectName) =>
            objectName.EndsWith(PjsipConfRenderer.RegistrationSuffix, StringComparison.Ordinal)
                ? objectName[..^PjsipConfRenderer.RegistrationSuffix.Length]
                : null;

        /// <summary>
        /// "Rejected" is its own state because it is the one an admin has to act on: the provider
        /// has our credentials and does not like them. Anything else that is not "Registered" —
        /// Unregistered, Stopped, a name we have not seen — means no registration right now.
        /// </summary>
        private static RegistrationState TrunkStateFor(string status)
        {
            if (string.Equals(status, "Registered", StringComparison.OrdinalIgnoreCase))
                return RegistrationState.Registered;

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
                return RegistrationState.Rejected;

            return RegistrationState.NotRegistered;
        }
    }
}
