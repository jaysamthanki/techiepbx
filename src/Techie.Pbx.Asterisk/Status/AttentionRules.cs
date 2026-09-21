using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// The "needs attention" list: everything about this system that an admin would want told,
    /// worked out from a <see cref="StatusSnapshot"/> the caller has already loaded. A pure
    /// function, like the renderers and <see cref="DestinationCatalog"/>, so every rule can be
    /// shown a system of its own in a test.
    ///
    /// The rules earn their place the same way a feature does. Each one is something that stops
    /// calls, is about to stop calls, or is a configuration that cannot do what it looks like it
    /// does — a dangling destination, a phone with no extension, a trunk nothing routes over.
    /// Anything an admin can see for themselves by opening the page it is on does not belong here.
    /// </summary>
    public static class AttentionRules
    {
        /// <summary>
        /// Above this, the disk is full enough to lose a recording or a database write, so it is
        /// the one thing on this page a filesystem can raise to Danger.
        /// </summary>
        public const int DiskDangerPercent = 90;

        /// <summary>Enough left to notice and do something about it.</summary>
        public const int DiskWarningPercent = 80;

        /// <summary>
        /// How close to expiry a certificate has to be before it is said out loud. Renewal starts
        /// at thirty days (D100), so two weeks means a fortnight of attempts has already failed
        /// quietly — which is exactly when somebody needs telling.
        /// </summary>
        public const int ExpiryWarningDays = 14;

        /// <summary>
        /// Everything wrong with this system, worst first and then in text order so that two polls
        /// which found the same things show them in the same order.
        /// </summary>
        public static List<Finding> Evaluate(StatusSnapshot snapshot, DateTimeOffset now)
        {
            var findings = new List<Finding>();

            Availability(snapshot, findings);
            Trunks(snapshot, findings);
            Destinations(snapshot, findings);
            Phones(snapshot, findings);
            Certificates(snapshot, findings, now);
            Disks(snapshot, findings);
            Timezone(snapshot, findings);

            return findings
                .OrderBy(finding => finding.Severity)
                .ThenBy(finding => finding.Text, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Whether Asterisk is answering, and whether it is running what is on disk.</summary>
        private static void Availability(StatusSnapshot snapshot, List<Finding> findings)
        {
            if (!snapshot.AmiReachable)
            {
                findings.Add(Note(
                    FindingSeverity.Danger,
                    FindingSubject.System,
                    "Asterisk is not answering on AMI. Calls cannot be made or received until it is."));
            }

            if (snapshot.RestartRequired)
            {
                findings.Add(Note(
                    FindingSeverity.Warning,
                    FindingSubject.System,
                    "Asterisk is running config that has already been replaced on disk. Restart it from the toolbar."));
            }

            if (snapshot.ConfigPending)
            {
                findings.Add(Note(
                    FindingSeverity.Info,
                    FindingSubject.System,
                    "There are config changes that have not been applied."));
            }
        }

        /// <summary>
        /// Whether this box can still be reached over HTTPS: no usable certificate at all, one
        /// about to run out, or an order that failed and has been failing since.
        /// </summary>
        private static void Certificates(StatusSnapshot snapshot, List<Finding> findings, DateTimeOffset now)
        {
            var usable = snapshot.Certificates.Where(certificate => certificate.IsUsable(now)).ToList();

            if (usable.Count == 0)
            {
                findings.Add(Note(
                    FindingSeverity.Warning,
                    FindingSubject.Certificates,
                    "No usable certificate: the admin UI is served over plain HTTP."));
            }
            else
            {
                // The one the server is actually serving, chosen the way the repository chooses
                // it: the usable certificate with the most life left.
                var current = usable.OrderByDescending(certificate => certificate.Expires).First();
                var days = current.DaysUntilExpiry(now);

                if (days <= ExpiryWarningDays)
                {
                    findings.Add(Note(
                        FindingSeverity.Warning,
                        FindingSubject.Certificates,
                        $"Certificate {current.Name} expires in {days} days."));
                }
            }

            // Said whatever else is true: a site can be serving a perfectly good certificate while
            // the renewal of the next one has been failing for a week.
            foreach (var certificate in snapshot.Certificates
                .Where(certificate => certificate.Enabled && !string.IsNullOrWhiteSpace(certificate.LastError)))
            {
                findings.Add(Note(
                    FindingSeverity.Warning,
                    FindingSubject.Certificates,
                    $"Certificate {certificate.Name}: the last order failed: {certificate.LastError}"));
            }
        }

        /// <summary>
        /// Every stored destination that no longer points at anything. These are the mistakes that
        /// do not announce themselves: deleting an extension leaves every route, key and failover
        /// aimed at it still stored, and still looking fine on its own page (D35).
        ///
        /// Only enabled owners are checked. A switched-off route is not in the dialplan, so where
        /// it would have sent a call is nobody's problem until it is switched on again.
        /// </summary>
        private static void Destinations(StatusSnapshot snapshot, List<Finding> findings)
        {
            foreach (var route in snapshot.InboundRoutes.Where(route => route.Enabled))
            {
                var name = route.CatchAll ? "Inbound catch-all route" : $"Inbound route {route.DID}";

                Missing(snapshot, findings, FindingSubject.InboundRoutes,
                    $"{name} sends calls to", route.ToDestination(), route.DestinationKey());
            }

            foreach (var group in snapshot.RingGroups.Where(group => group.Enabled))
            {
                Missing(snapshot, findings, FindingSubject.RingGroups,
                    $"Ring group {group.Number} sends unanswered calls to",
                    group.ToDestination(), group.DestinationKey());
            }

            foreach (var ivr in snapshot.Ivrs.Where(ivr => ivr.Enabled))
            {
                foreach (var entry in ivr.Entries)
                {
                    Missing(snapshot, findings, FindingSubject.Ivrs,
                        $"IVR {ivr.Name} sends key {entry.Digit} to",
                        entry.ToDestination(), entry.DestinationKey());
                }

                Missing(snapshot, findings, FindingSubject.Ivrs,
                    $"IVR {ivr.Name} sends callers who press nothing to",
                    ivr.ToFinalDestination(), ivr.DestinationKey());
            }

            foreach (var condition in snapshot.TimeConditions.Where(condition => condition.Enabled))
            {
                Missing(snapshot, findings, FindingSubject.TimeConditions,
                    $"Time condition {condition.Name} sends open-hours calls to",
                    condition.ToOpenDestination(), condition.OpenDestinationKey());

                Missing(snapshot, findings, FindingSubject.TimeConditions,
                    $"Time condition {condition.Name} sends closed-hours calls to",
                    condition.ToClosedDestination(), condition.ClosedDestinationKey());

                Missing(snapshot, findings, FindingSubject.TimeConditions,
                    $"Time condition {condition.Name} sends holiday calls to",
                    condition.ToHolidayDestination(), condition.HolidayDestinationKey());

                // A holiday with an override of its own is a fourth destination hiding inside the
                // condition, and the one most easily forgotten (D64).
                foreach (var rule in condition.HolidayRules().Where(rule => rule.HasOverride))
                {
                    Missing(snapshot, findings, FindingSubject.TimeConditions,
                        $"Time condition {condition.Name} sends calls on {rule.HolidayDate} to",
                        rule.ToDestination(), rule.DestinationKey());
                }
            }
        }

        /// <summary>A filesystem filling up, which on an appliance is a slow way to lose a PBX.</summary>
        private static void Disks(StatusSnapshot snapshot, List<Finding> findings)
        {
            foreach (var disk in snapshot.Disks.Where(disk => disk.UsedPercent >= DiskWarningPercent))
            {
                findings.Add(Note(
                    disk.UsedPercent >= DiskDangerPercent ? FindingSeverity.Danger : FindingSeverity.Warning,
                    FindingSubject.System,
                    $"Disk holding {disk.Path} is {disk.UsedPercent}% full."));
            }
        }

        /// <summary>
        /// Adds a finding when a stored destination is not in the catalog any more. The catalog is
        /// the one answer to "where can a call go right now" (D35), so a destination it cannot
        /// find is one the dialplan will not carry either. Hangup is always in it, so a route that
        /// ends the call never appears here.
        /// </summary>
        private static void Missing(
            StatusSnapshot snapshot,
            List<Finding> findings,
            FindingSubject subject,
            string sentence,
            Destination? destination,
            string key)
        {
            var found = DestinationCatalog.Find(
                snapshot.Extensions,
                snapshot.RingGroups,
                snapshot.Announcements,
                snapshot.Ivrs,
                snapshot.TimeConditions,
                destination);

            if (found != null)
                return;

            findings.Add(Note(FindingSeverity.Warning, subject, $"{sentence} {key}, which no longer exists."));
        }

        private static Finding Note(FindingSeverity severity, FindingSubject subject, string text) => new()
        {
            Severity = severity,
            Subject = subject,
            Text = text,
        };

        /// <summary>What an admin calls a phone: its name, or its MAC until somebody names it.</summary>
        private static string PhoneLabel(Phone phone) => phone.Name.Length > 0 ? phone.Name : phone.Mac;

        /// <summary>
        /// Phones that cannot register, which since schema 020 is a question about their line key:
        /// one whose line is an extension that has been switched off, one whose line names an
        /// extension that has since been deleted — the key holds the number, so nothing stops that
        /// — and one nobody has put a line on at all. The last is only a note: a phone waiting on a
        /// desk for somebody to start is an ordinary thing for a site to have.
        /// </summary>
        private static void Phones(StatusSnapshot snapshot, List<Finding> findings)
        {
            foreach (var phone in snapshot.Phones.Where(phone => phone.Enabled))
            {
                var number = PhoneButton.LineNumber(snapshot.PhoneButtons.Where(b => b.PhoneID == phone.PhoneID));

                if (number == null)
                {
                    findings.Add(Note(
                        FindingSeverity.Info,
                        FindingSubject.Phones,
                        $"Phone {PhoneLabel(phone)} has no extension assigned."));

                    continue;
                }

                var extension = snapshot.Extensions.FirstOrDefault(e =>
                    string.Equals(e.Number, number, StringComparison.Ordinal));

                if (extension == null)
                {
                    findings.Add(Note(
                        FindingSeverity.Warning,
                        FindingSubject.Phones,
                        $"Phone {PhoneLabel(phone)} registers as extension {number}, which no longer exists."));
                }
                else if (!extension.Enabled)
                {
                    findings.Add(Note(
                        FindingSeverity.Warning,
                        FindingSubject.Phones,
                        $"Phone {PhoneLabel(phone)} is assigned to extension {extension.Number}, which is switched off."));
                }
            }
        }

        /// <summary>
        /// Open hours are matched against Asterisk's own clock (D65, D74). With no zone recorded
        /// that clock is UTC, which is right for a UK site in winter and an hour out all summer,
        /// so a site that uses time conditions is told which it is getting.
        /// </summary>
        private static void Timezone(StatusSnapshot snapshot, List<Finding> findings)
        {
            if (!snapshot.TimeConditions.Any(condition => condition.Enabled))
                return;

            var zone = snapshot.Timezone.Trim();

            if (zone.Length == 0 || string.Equals(zone, SystemTimezones.Utc, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(Note(
                    FindingSeverity.Info,
                    FindingSubject.Settings,
                    "Open hours are evaluated in UTC because no timezone is set."));
            }
        }

        /// <summary>
        /// Trunks that are not carrying calls, for either of the two reasons: the provider will
        /// not have us, or nothing routes over them.
        /// </summary>
        private static void Trunks(StatusSnapshot snapshot, List<Finding> findings)
        {
            foreach (var trunk in snapshot.Trunks.Where(trunk => trunk.Enabled))
            {
                // Every state is Unknown when AMI could not be asked, and "not registered" would
                // then be a guess repeated once per trunk. The AMI finding has already said it.
                if (snapshot.AmiReachable && trunk.Register &&
                    snapshot.TrunkStates.TryGetValue(trunk.Name, out var state))
                {
                    if (state == RegistrationState.Rejected)
                    {
                        findings.Add(Note(
                            FindingSeverity.Danger,
                            FindingSubject.Trunks,
                            $"Trunk {trunk.Name}: the provider rejected our credentials."));
                    }
                    else if (state is RegistrationState.NotRegistered or RegistrationState.Unreachable)
                    {
                        findings.Add(Note(
                            FindingSeverity.Warning,
                            FindingSubject.Trunks,
                            $"Trunk {trunk.Name} is not registered with its provider."));
                    }
                }

                var routed =
                    snapshot.InboundRoutes.Any(route => route.Enabled && route.TrunkID == trunk.TrunkID) ||
                    snapshot.OutboundRoutes.Any(route => route.Enabled && route.TrunkID == trunk.TrunkID);

                if (!routed)
                {
                    findings.Add(Note(
                        FindingSeverity.Info,
                        FindingSubject.Trunks,
                        $"Trunk {trunk.Name} has no routes, so it carries no calls."));
                }
            }
        }
    }
}
