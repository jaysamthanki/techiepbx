using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// Turns a FreePBX export into an <see cref="ImportPlan"/>: every row the import will write,
    /// already translated and validated against the model it lands in, and a warning for
    /// everything that could not be carried as it was (D170, docs/freepbx-import.md).
    ///
    /// Nothing is written here. The preview is this plan, and the import writes this plan, so what
    /// the operator confirmed is what lands — unless the database changed in between, which the
    /// repositories still catch row by row.
    ///
    /// The rules, in the order things depend on each other:
    /// <list type="bullet">
    /// <item><b>The import clears first</b> (D174): extensions, trunks, phones with their keys,
    /// routes and announcements are the import's tables, and everything already in them goes
    /// before anything lands — <see cref="ImportPlan.Cleared"/> is what was there, so the preview
    /// can say it. There is no keep-or-overwrite decision to make; settings, users and everything
    /// else the import does not own are untouched.</item>
    /// <item><b>Extensions</b> all land: chan_sip and pjsip both become PJSIP, same number and
    /// secret. A secret or PIN TNPBX would refuse is replaced with a generated one and said so,
    /// rather than costing the extension.</item>
    /// <item><b>Trunks</b> always land <c>Enabled = false</c> (D170).</item>
    /// <item><b>Outbound routes</b> are one per FreePBX pattern, through <see cref="FreePbxPattern"/>.</item>
    /// <item><b>Inbound routes</b> carry an extension destination or nothing
    /// (<see cref="FreePbxDestination"/>). FreePBX's are not bound to a trunk and ours are, so each
    /// becomes one route per imported trunk.</item>
    /// <item><b>Sounds</b> are one announcement per recording, the WAV copy preferred.</item>
    /// <item><b>Phones</b> all land: the line is key 1 and FreePBX's keys follow it.</item>
    /// <item><b>Voicemail</b> goes only into mailboxes this import creates.</item>
    /// </list>
    /// </summary>
    public partial class ImportPlanner
    {
        /// <summary>What a colliding trunk or route name gets, D170's suggestion.</summary>
        private const string ImportedSuffix = "-imported";

        /// <summary>The trunk and route name rule: a letter, then letters, digits and dashes (D37).</summary>
        private const int MaxSectionNameLength = 32;

        /// <summary>A validation stand-in for a trunk ID the import does not have yet.</summary>
        private const long PlaceholderTrunkID = 1;

        private readonly MigrationManifest manifest;
        private readonly ImportOptions options;
        private readonly ImportPlan plan = new();
        private readonly string root;

        /// <summary>
        /// What this TNPBX already has. Only read to fill <see cref="ImportPlan.Cleared"/>: the
        /// import deletes all of it first (D174), so from here on the tables it owns are empty.
        /// </summary>
        private ExistingConfig existing;

        /// <summary>FreePBX trunk name to the name it was planned under here.</summary>
        private readonly Dictionary<string, string> trunkNames = new(StringComparer.Ordinal);

        public ImportPlanner(MigrationManifest manifest, ExistingConfig existing, string extractedRoot, ImportOptions options)
        {
            this.existing = existing;
            this.manifest = manifest;
            this.options = options;
            this.root = Path.GetFullPath(extractedRoot);
        }

        /// <summary>Works the whole plan out. Call once.</summary>
        public ImportPlan Plan()
        {
            var source = this.manifest.Source ?? new ManifestSource();
            this.plan.Source = $"{Text(source.Distribution)} {Text(source.Version)} (Asterisk {Text(source.Asterisk)}), " +
                               $"exported {Text(this.manifest.Exported)}";

            // The import deletes everything in the six tables it owns before anything lands (D174),
            // so the plan works out as if none of it was there. The snapshot is the preview's to say.
            this.plan.Cleared = this.existing;
            this.existing = new ExistingConfig();

            this.PlanExportWarnings();
            this.PlanExtensions();
            this.PlanTrunks();
            this.PlanOutboundRoutes();
            this.PlanInboundRoutes();
            this.PlanSounds();
            this.PlanPhones();
            this.PlanVoicemail();

            return this.plan;
        }

        /// <summary>
        /// The section-name rule (D37) applied to a FreePBX name: anything that is not a letter or
        /// digit becomes a dash, runs collapse, it starts with a letter and it fits in 32.
        /// </summary>
        public static string SectionName(string? raw, string fallback)
        {
            var sb = new StringBuilder();

            foreach (var c in Text(raw))
            {
                if (char.IsAsciiLetterOrDigit(c))
                    sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
            }

            var name = sb.ToString().Trim('-');

            if (name.Length == 0)
                name = fallback;
            else if (!char.IsAsciiLetter(name[0]))
                name = fallback + "-" + name;

            return Fit(name, "");
        }

        /// <summary>
        /// <paramref name="name"/>, or the first of <c>name-imported</c>, <c>name-imported2</c>…
        /// that is not taken. Each fits in 32, cutting the name rather than the suffix.
        /// </summary>
        public static string UniqueSectionName(string name, ISet<string> taken)
        {
            if (!taken.Contains(name))
                return name;

            for (var n = 1; ; n++)
            {
                var suffix = n == 1 ? ImportedSuffix : ImportedSuffix + n.ToString(CultureInfo.InvariantCulture);
                var candidate = Fit(name, suffix);

                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        /// <summary>
        /// Whether a value passes the extension model's own rule for one field, asked of an
        /// otherwise valid extension so that only that field can fail. The rules stay in
        /// <see cref="Extension.Validate"/>, in one place.
        /// </summary>
        private static bool Accepted(Action<Extension> set)
        {
            var probe = new Extension
            {
                Name = "Probe",
                Number = "10",
                Secret = "ProbeProbeProbe1",
                VoicemailEnabled = true,
                VoicemailPin = "1234",
            };

            set(probe);
            return probe.Validate().Count == 0;
        }

        /// <summary>0 for WAV, 1 for MP3 — both of which the converter reads — and 2 for the raw telephony formats it does not.</summary>
        private static int AudioPreference(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => 0,
            ".mp3" => 1,
            _ => 2,
        };

        /// <summary>
        /// A display name or description cut down to what the name rule allows: letters, digits,
        /// spaces and <c>. , ' - _ ( ) &amp;</c>, single-spaced, at most <paramref name="max"/> long.
        /// </summary>
        private static string CleanText(string? raw, int max)
        {
            var sb = new StringBuilder();

            foreach (var c in Text(raw))
            {
                var allowed = char.IsLetterOrDigit(c) || c is '.' or ',' or '\'' or '-' or '_' or '(' or ')' or '&';

                if (allowed)
                    sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
            }

            var text = sb.ToString().Trim();
            return text.Length <= max ? text : text[..max].TrimEnd();
        }

        /// <summary>The extension's display name, made valid; the number when FreePBX had none.</summary>
        private string ExtensionName(string number, string? raw)
        {
            var name = CleanText(raw, 64);
            if (name.Length == 0 || !Accepted(e => e.Name = name))
                name = $"Extension {number}";

            if (Text(raw).Length > 0 && !string.Equals(name, Text(raw), StringComparison.Ordinal))
            {
                this.Warn(MigrationSection.Extensions,
                    $"Extension {number}'s name '{Printable(raw)}' became '{name}'.",
                    "A TNPBX name is letters, digits, spaces and . , ' - _ ( ) & only, up to 64 characters.",
                    "Nothing, unless the new name reads badly — then edit it on the Extensions page.");
            }

            return name;
        }

        /// <summary>A name cut to fit 32 with a suffix on the end.</summary>
        private static string Fit(string name, string suffix)
        {
            var room = MaxSectionNameLength - suffix.Length;
            var head = name.Length > room ? name[..room].TrimEnd('-') : name;

            return head + suffix;
        }

        /// <summary>
        /// One FreePBX attendant key as a TNPBX key, or null with a warning. Positions move one
        /// along, because FreePBX counts its attendant keys after the line and ours count the line.
        /// </summary>
        private PhoneButton? PhoneKey(string phoneLabel, ManifestPhoneKey key, ref bool parkingMapped)
        {
            var position = PhoneButton.FirstPosition + key.Position;
            var type = Text(key.Type).ToLowerInvariant();
            var value = Text(key.Value);
            var label = $"{phoneLabel} key {position} ('{Printable(key.Label)}', {Printable(type)} {Printable(value)})";

            if (key.Position < 1 || position > PhoneButton.Count)
            {
                this.Warn(MigrationSection.Phones, $"{label} was dropped.",
                    $"TNPBX offers keys {PhoneButton.FirstPosition} to {PhoneButton.Count}, the line being key 1.",
                    "Rearrange the keys by hand on the Phones page if it matters.");
                return null;
            }

            if (type == "blf")
            {
                if (this.Reachable(value))
                    return new PhoneButton { Position = position, TargetType = PhoneButtonTarget.Blf, TargetValue = value };

                this.Warn(MigrationSection.Phones, $"{label} was dropped.",
                    $"It watches extension {value}, which neither this TNPBX nor the import has.",
                    "Assign it by hand on the Phones page once the extension exists.");
                return null;
            }

            if (type == "parking")
            {
                var slot = ParkingSlotPattern().Match(value);
                if (slot.Success)
                {
                    parkingMapped = true;
                    return new PhoneButton { Position = position, TargetType = PhoneButtonTarget.ParkingSlot, TargetValue = slot.Groups["slot"].Value };
                }

                this.Warn(MigrationSection.Phones, $"{label} was dropped.",
                    "Only FreePBX's default parking slots 71-79 have a TNPBX slot to map to.",
                    "Assign a parking slot key by hand on the Phones page.");
                return null;
            }

            this.Warn(MigrationSection.Phones, $"{label} was dropped.",
                "TNPBX keys are lines, BLF lamps, parking slots and call flow controls; this one is none of those.",
                "Assign it by hand on the Phones page if there is an equivalent.");
            return null;
        }

        /// <summary>The exporter's own warnings, passed on as they are.</summary>
        private void PlanExportWarnings()
        {
            foreach (var warning in this.manifest.Warnings ?? new List<string?>())
            {
                if (Text(warning).Length == 0)
                    continue;

                this.Warn(MigrationSection.Export, Printable(warning),
                    "The exporter reported this on the FreePBX box.",
                    "Check it on the FreePBX box; the sections below say what the import did about it.");
            }
        }

        private void PlanExtensions()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var source in this.manifest.Extensions ?? new List<ManifestExtension>())
            {
                var number = Text(source.Number);

                if (!Extension.IsValidNumber(number))
                {
                    this.Warn(MigrationSection.Extensions, $"Extension '{Printable(number)}' was not imported.",
                        "Its number is not 2 to 6 digits, which is what a TNPBX extension number is.",
                        "Create it by hand under a number TNPBX accepts, if it is still needed.");
                    continue;
                }

                if (!seen.Add(number))
                {
                    this.Warn(MigrationSection.Extensions, $"A second extension {number} was not imported.",
                        "The export lists that number twice; the first one was imported.",
                        "Check the FreePBX box for the duplicate.");
                    continue;
                }

                var extension = new Extension
                {
                    Enabled = true,
                    Number = number,
                    VoicemailAttachRecording = source.VoicemailAttach,
                    VoicemailEnabled = source.VoicemailEnabled,
                };

                extension.Name = this.ExtensionName(number, source.Name);

                var secret = Text(source.Secret);
                if (secret.Length > 0 && Accepted(e => e.Secret = secret))
                {
                    extension.Secret = secret;
                }
                else
                {
                    extension.Secret = SecretGenerator.Create();
                    this.Warn(MigrationSection.Extensions, $"Extension {number} was given a new SIP secret.",
                        secret.Length == 0
                            ? "FreePBX had no secret for it."
                            : "Its FreePBX secret is not 16 to 64 letters and digits, which TNPBX requires.",
                        "Phones TNPBX provisions get the new one automatically. Enter it (Extensions page, Show secret) on any device set up by hand.");
                }

                var callerID = Text(source.OutboundCallerID);
                if (CallerIDFormat.Error(callerID, "Outbound caller ID") == null)
                {
                    extension.OutboundCallerID = callerID;
                }
                else
                {
                    this.Warn(MigrationSection.Extensions, $"Extension {number}'s outbound caller ID '{Printable(callerID)}' was dropped.",
                        "It is not a number of up to 15 digits or \"Name\" <number>.",
                        "Set it by hand on the Extensions page if the extension should call out as its own number.");
                }

                var pin = Text(source.VoicemailPin);
                if (Accepted(e => e.VoicemailPin = pin))
                {
                    extension.VoicemailPin = pin;
                }
                else if (source.VoicemailEnabled)
                {
                    extension.VoicemailPin = SecretGenerator.CreatePin();
                    this.Warn(MigrationSection.Extensions, $"Extension {number}'s mailbox was given a new voicemail PIN.",
                        pin.Length == 0 ? "FreePBX had a blank PIN for it." : "Its FreePBX PIN is not 4 to 8 digits, which TNPBX requires.",
                        "Tell the user their new PIN (Extensions page), or set one they choose.");
                }

                var email = Text(source.VoicemailEmail);
                if (email.Length > 0)
                {
                    if (Accepted(e => e.VoicemailEmail = email))
                    {
                        extension.VoicemailEmail = email;

                        if (this.options.UserEmailFromVoicemail)
                            extension.UserEmail = email;
                    }
                    else
                    {
                        this.Warn(MigrationSection.Extensions, $"Extension {number}'s voicemail email '{Printable(email)}' was dropped.",
                            "It is not an email address TNPBX accepts (one address, up to 128 characters).",
                            "Set it by hand on the Extensions page.");
                    }
                }

                var context = Text(source.VoicemailContext);
                if (context.Length > 0 && !string.Equals(context, "default", StringComparison.Ordinal))
                {
                    this.Warn(MigrationSection.Extensions, $"Extension {number}'s mailbox moves from voicemail context '{Printable(context)}' to 'default'.",
                        "Every TNPBX mailbox lives in the one context.",
                        "Nothing, unless something outside TNPBX dialled that context by name.");
                }

                if (string.Equals(Text(source.Tech), "sip", StringComparison.OrdinalIgnoreCase))
                    this.plan.ChanSipExtensions++;

                var errors = extension.Validate();
                if (errors.Count > 0)
                {
                    this.Warn(MigrationSection.Extensions, $"Extension {number} was not imported.",
                        string.Join(" ", errors),
                        "Create it by hand on the Extensions page.");
                    continue;
                }

                this.plan.Extensions.Add(extension);
            }

            if (this.plan.ChanSipExtensions > 0)
            {
                this.Warn(MigrationSection.Extensions,
                    $"{this.plan.ChanSipExtensions} chan_sip extension(s) become PJSIP endpoints.",
                    "TNPBX speaks PJSIP only (D170). The number and secret are unchanged.",
                    "Nothing for phones TNPBX provisions. A device set up by hand may need TNPBX's SIP port and transport.");
            }
        }

        /// <summary>
        /// The inbound routes: DID routes and a catch-all, extension destinations only, one copy
        /// per imported trunk because a FreePBX route takes calls from any trunk and ours belong
        /// to one.
        /// </summary>
        private void PlanInboundRoutes()
        {
            var trunks = this.plan.Trunks.Select(t => t.Name).ToList();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var source in this.manifest.InboundRoutes ?? new List<ManifestInboundRoute>())
            {
                var did = Text(source.DID);
                var callerID = Text(source.CallerIDMatch);
                var raw = Text(source.Destination);
                var description = CleanText(source.Description, 64);

                var label = did.Length > 0
                    ? $"The inbound route for DID {Printable(did)}"
                    : callerID.Length > 0 ? "The any-DID inbound route" : "The catch-all inbound route";
                if (description.Length > 0)
                    label += $" ('{description}')";

                if (callerID.Length > 0)
                {
                    this.Warn(MigrationSection.InboundRoutes, $"{label} was not imported.",
                        $"It only matched calls from caller ID {Printable(callerID)}, and a TNPBX inbound route matches on the DID alone.",
                        "Decide where those calls should go; there is no caller ID routing in TNPBX v1.");
                    continue;
                }

                var catchAll = did.Length == 0;
                if (!catchAll && !DIDPattern().IsMatch(did))
                {
                    this.Warn(MigrationSection.InboundRoutes, $"{label} was not imported.",
                        "A TNPBX DID is 1 to 15 digits, matched exactly; FreePBX DID patterns are not carried.",
                        "Add a route by hand for each number the provider actually sends.");
                    continue;
                }

                var destination = FreePbxDestination.Translate(raw);
                if (destination == null)
                {
                    this.Warn(MigrationSection.InboundRoutes, $"{label} was not imported.",
                        $"Its FreePBX destination '{Printable(raw)}' is not an extension. Only extension destinations " +
                        "(ext-local, from-did-direct) are carried; IVRs, ring groups, queues and voicemail-direct are not.",
                        "Build the destination on TNPBX, then add this inbound route by hand pointing at it.");
                    continue;
                }

                if (!this.Reachable(destination.Value))
                {
                    this.Warn(MigrationSection.InboundRoutes, $"{label} was not imported.",
                        $"It rings extension {destination.Value}, which neither this TNPBX nor the import has.",
                        "Create the extension, then add the route by hand.");
                    continue;
                }

                if (!seen.Add(catchAll ? "" : did))
                {
                    this.Warn(MigrationSection.InboundRoutes, $"A second copy of {label.ToLowerInvariant()} was not imported.",
                        "The export has two routes for the same number; the first one was imported.",
                        "Check which one FreePBX was really using.");
                    continue;
                }

                if (trunks.Count == 0)
                {
                    this.Warn(MigrationSection.InboundRoutes, $"{label} was not imported.",
                        "A TNPBX inbound route belongs to the trunk the call arrives on, and no trunk was imported.",
                        "Add the trunk, then the route, by hand.");
                    continue;
                }

                var moh = Text(source.MohClass);
                if (moh.Length > 0 && !string.Equals(moh, "default", StringComparison.Ordinal))
                {
                    this.Warn(MigrationSection.InboundRoutes, $"{label} lost its music on hold class '{Printable(moh)}'.",
                        "Music on hold classes are not part of the export.",
                        "Create the class on the Music on hold page and pick it on the route.");
                }

                foreach (var trunk in trunks)
                {
                    var route = new InboundRoute
                    {
                        CatchAll = catchAll,
                        DID = catchAll ? "" : did,
                        Description = description,
                        DestinationType = destination.Type.ToString(),
                        DestinationValue = destination.Value,
                        Enabled = true,
                        TrunkID = PlaceholderTrunkID,
                    };

                    var errors = route.Validate();
                    if (errors.Count > 0)
                    {
                        this.Warn(MigrationSection.InboundRoutes, $"{label} was not imported.", string.Join(" ", errors),
                            "Add it by hand on the Inbound routes page.");
                        break;
                    }

                    route.TrunkID = 0;
                    this.plan.InboundRoutes.Add(new PlannedInboundRoute { Route = route, SourceDestination = raw, TrunkName = trunk });
                }

                this.plan.InboundRouteCount++;
            }

            if (trunks.Count > 1 && this.plan.InboundRouteCount > 0)
            {
                this.Warn(MigrationSection.InboundRoutes,
                    $"Each inbound route was created once for every imported trunk ({string.Join(", ", trunks)}).",
                    "A FreePBX inbound route takes a call from any trunk; a TNPBX route belongs to one.",
                    "After cutover, delete the copies on trunks that never carry that number.");
            }
        }

        /// <summary>
        /// One route per FreePBX pattern. A FreePBX route with several patterns becomes several
        /// routes named <c>route-01</c>, <c>route-02</c>… at the same priority, numbered in the
        /// export's order so that the name, which breaks priority ties, keeps that order.
        /// </summary>
        private void PlanOutboundRoutes()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = (this.manifest.OutboundRoutes ?? new List<ManifestOutboundRoute>())
                .GroupBy(r => (Name: Text(r.Name), r.Priority))
                .ToList();

            foreach (var group in groups)
            {
                var items = group.ToList();
                var baseName = SectionName(group.Key.Name, "route");
                var width = items.Count >= 100 ? "000" : "00";
                var routeLabel = $"Outbound route '{Printable(group.Key.Name)}'";

                var first = items[0];
                var sequence = (first.TrunkSequence ?? new List<string?>()).Select(Text).Where(t => t.Length > 0).ToList();
                if (sequence.Count > 1)
                {
                    this.Warn(MigrationSection.OutboundRoutes, $"{routeLabel} lost its failover trunks ({string.Join(", ", sequence.Skip(1))}).",
                        "A TNPBX route goes out over one trunk.",
                        "Nothing, unless the failover mattered — then add a second route at a later priority by hand.");
                }

                if (items.Any(i => i.Emergency))
                {
                    this.Warn(MigrationSection.OutboundRoutes, $"{routeLabel} was FreePBX's emergency route.",
                        "TNPBX has no emergency-route flag; its patterns imported as ordinary routes.",
                        "After cutover, test emergency dialling from a real phone with the provider's test number.");
                }

                // A route whose trunk did not make it is one warning for the route, not one per
                // pattern: FreePBX's "Default" route on a box that lost its trunk is twenty lines
                // saying the same thing otherwise.
                var orphans = items.Where(r => !this.trunkNames.ContainsKey(Text(r.TrunkName))).ToList();
                if (orphans.Count > 0)
                {
                    var trunkName = Text(orphans[0].TrunkName);
                    this.Warn(MigrationSection.OutboundRoutes,
                        $"{routeLabel} was not imported ({orphans.Count} pattern(s): {string.Join(", ", orphans.Select(r => Printable(r.DialPattern)))}).",
                        trunkName.Length == 0 ? "It has no trunk." : $"Its trunk '{Printable(trunkName)}' was not imported (see Trunks).",
                        "Add the trunk by hand, then the routes.");
                }

                for (var i = 0; i < items.Count; i++)
                {
                    var source = items[i];
                    var label = $"{routeLabel} pattern '{Printable(source.DialPattern)}'";

                    if (!this.trunkNames.TryGetValue(Text(source.TrunkName), out var plannedTrunk))
                        continue;

                    if (!FreePbxPattern.TryTranslate(source.DialPattern, out var pattern, out var strip, out var problem))
                    {
                        this.Warn(MigrationSection.OutboundRoutes, $"{label} was not imported.", problem,
                            "If the pattern is still needed, add a route by hand. International dialling is refused by design (D47).");
                        continue;
                    }

                    var name = items.Count == 1
                        ? baseName
                        : Fit(baseName, "-" + (i + 1).ToString(width, CultureInfo.InvariantCulture));
                    name = UniqueSectionName(name, taken);

                    var route = new OutboundRoute
                    {
                        DialPattern = pattern,
                        Enabled = true,
                        Name = name,
                        PrependDigits = Text(source.PrependDigits),
                        Priority = Math.Clamp(source.Priority, 1, OutboundRoute.MaxPriority),
                        StripDigits = strip + Math.Max(0, source.StripDigits),
                        TrunkID = PlaceholderTrunkID,
                    };

                    var callerID = Text(source.CallerID);
                    if (CallerIDFormat.Error(callerID, "Caller ID") == null)
                    {
                        route.CallerID = callerID;
                    }
                    else
                    {
                        this.Warn(MigrationSection.OutboundRoutes, $"{label} lost its caller ID '{Printable(callerID)}'.",
                            "It is not a number of up to 15 digits or \"Name\" <number>.",
                            "Set it by hand on the Routes page.");
                    }

                    var errors = route.Validate();
                    if (errors.Count > 0)
                    {
                        this.Warn(MigrationSection.OutboundRoutes, $"{label} was not imported.", string.Join(" ", errors),
                            "Add it by hand on the Routes page.");
                        continue;
                    }

                    route.TrunkID = 0;

                    var twin = this.plan.OutboundRoutes.FirstOrDefault(p => string.Equals(p.Route.DialPattern, pattern, StringComparison.Ordinal));
                    if (twin != null)
                    {
                        this.Warn(MigrationSection.OutboundRoutes, $"Routes {twin.Route.Name} and {name} both match {pattern}.",
                            "Only the one tried first (lower priority, then name) is ever used; FreePBX had the same overlap.",
                            "After checking which prepend is right, delete the other on the Routes page.");
                    }

                    taken.Add(name);
                    this.plan.OutboundRoutes.Add(new PlannedOutboundRoute
                    {
                        Route = route,
                        SourcePattern = Text(source.DialPattern),
                        TrunkName = plannedTrunk,
                    });
                }
            }
        }

        /// <summary>
        /// The phones: MAC and model, the line as key 1, and FreePBX's attendant keys after it.
        /// A phone whose keys cannot all be carried still lands; the keys that could not are named.
        /// </summary>
        private void PlanPhones()
        {
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var parkingMapped = false;

            foreach (var source in this.manifest.Phones ?? new List<ManifestPhone>())
            {
                var mac = Phone.NormalizeMac(Text(source.Mac));
                var label = $"Phone {Printable(mac)}";

                if (!Phone.IsValidMac(mac))
                {
                    this.Warn(MigrationSection.Phones, $"{label} was not imported.", "That is not a MAC address.",
                        "Nothing: a phone that provisions from TNPBX adds itself.");
                    continue;
                }

                if (!seen.Add(mac))
                {
                    this.Warn(MigrationSection.Phones, $"A second {label.ToLowerInvariant()} was not imported.",
                        "The export lists that MAC twice; the first one was imported.", "");
                    continue;
                }

                var phone = new Phone
                {
                    Brand = PhoneBrand.Polycom,
                    Enabled = true,
                    Mac = mac,
                    Model = PolycomModel(source.Model),
                };

                if (Text(source.Model).Length > 0 && phone.Model.Length == 0)
                {
                    this.Warn(MigrationSection.Phones, $"{label}'s model '{Printable(source.Model)}' was left blank.",
                        "It is not in a form the provisioning endpoint compares against (D78).",
                        "Nothing: the phone's first config fetch fills it in.");
                }

                var buttons = new List<PhoneButton>();
                var line = Text(source.Line);
                var keys = (source.Keys ?? new List<ManifestPhoneKey>()).OrderBy(k => k.Position).ToList();

                if (line.Length == 0)
                {
                    if (keys.Count > 0)
                    {
                        this.Warn(MigrationSection.Phones, $"{label} was imported without its {keys.Count} key(s).",
                            "It has no line in FreePBX, and a TNPBX phone's keys hang off its line.",
                            "Assign the phone a line on the Phones page, then its keys.");
                    }
                }
                else if (!this.Reachable(line))
                {
                    this.Warn(MigrationSection.Phones, $"{label} was imported without keys.",
                        $"Its line is extension {Printable(line)}, which neither this TNPBX nor the import has.",
                        "Create the extension, then assign the phone's keys on the Phones page.");
                }
                else if (claimed.Contains(line))
                {
                    this.Warn(MigrationSection.Phones, $"{label} was imported without keys.",
                        $"Another phone already registers as extension {line}, and two phones cannot register as one (schema 020).",
                        "Decide which phone is the user's and assign keys on the Phones page.");
                }
                else
                {
                    buttons.Add(new PhoneButton { Position = PhoneButton.FirstPosition, TargetType = PhoneButtonTarget.Line, TargetValue = line });

                    foreach (var key in keys)
                    {
                        var button = this.PhoneKey(label, key, ref parkingMapped);
                        if (button != null)
                            buttons.Add(button);
                    }

                    var errors = PhoneButton.ValidateSet(buttons);
                    if (errors.Count > 0)
                    {
                        this.Warn(MigrationSection.Phones, $"{label} was imported without keys.", string.Join(" ", errors),
                            "Assign its keys on the Phones page.");
                        buttons.Clear();
                    }
                    else
                    {
                        claimed.Add(line);
                    }
                }

                var phoneErrors = phone.Validate();
                if (phoneErrors.Count > 0)
                {
                    this.Warn(MigrationSection.Phones, $"{label} was not imported.", string.Join(" ", phoneErrors),
                        "Nothing: a phone that provisions from TNPBX adds itself.");
                    continue;
                }

                this.plan.Phones.Add(new PlannedPhone { Buttons = buttons, Phone = phone });
            }

            if (parkingMapped)
            {
                this.Warn(MigrationSection.Phones,
                    "Parking keys were mapped from FreePBX slots 71-79 to TNPBX slots 1-9.",
                    "TNPBX parks in single-digit slots (D119); FreePBX's default lot numbers its slots from 71.",
                    "Switch parking on (Parking page) and check it has enough slots: the keys stay dark until it does.");
            }
        }

        /// <summary>
        /// One announcement per recording. FreePBX keeps a recording in several formats side by
        /// side (<c>Greeting.wav</c>, <c>Greeting.g722</c>); the WAV is the one converted, and the
        /// others are listed rather than imported as announcements of their own.
        /// </summary>
        private void PlanSounds()
        {
            var soundsRoot = Path.Combine(this.root, "files", "sounds") + Path.DirectorySeparatorChar;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var groups = (this.manifest.Sounds ?? new List<ManifestSound>())
                .GroupBy(s => StripAudioExtension(Text(s.Filename)), StringComparer.Ordinal)
                .ToList();

            foreach (var group in groups)
            {
                var files = new List<(ManifestSound Sound, string Path)>();

                foreach (var sound in group)
                {
                    var file = Text(sound.Filename);
                    var full = Path.GetFullPath(Path.Combine(soundsRoot, file));

                    if (file.Length == 0 || MigrationArchive.NameProblem(file) != null ||
                        !full.StartsWith(soundsRoot, StringComparison.Ordinal) || !File.Exists(full))
                    {
                        this.Warn(MigrationSection.Sounds, $"Sound '{Printable(file)}' was not imported.",
                            "The manifest names it, but it is not a file inside the export's files/sounds folder.",
                            "Upload it by hand on the Announcements page if you still have it.");
                        continue;
                    }

                    files.Add((sound, full));
                }

                if (files.Count == 0)
                    continue;

                var chosen = files.OrderBy(f => AudioPreference(f.Path)).First();
                var chosenFile = Text(chosen.Sound.Filename);

                var rawName = files.Select(f => Text(f.Sound.AnnouncementName)).FirstOrDefault(n => n.Length > 0)
                              ?? Path.GetFileNameWithoutExtension(chosenFile);
                var name = CleanText(rawName, 64);
                if (name.Length == 0)
                    name = "Imported sound";

                name = UniqueAnnouncementName(name, taken);

                var announcement = new Announcement
                {
                    Description = "Imported from FreePBX",
                    Enabled = true,
                    Name = name,
                };

                var errors = announcement.Validate();
                if (errors.Count > 0)
                {
                    this.Warn(MigrationSection.Sounds, $"Sound '{Printable(chosenFile)}' was not imported.", string.Join(" ", errors),
                        "Upload it by hand on the Announcements page.");
                    continue;
                }

                var convertible = AudioPreference(chosen.Path) < 2;
                if (!convertible)
                {
                    this.Warn(MigrationSection.Sounds, $"Announcement '{name}' will be created with no audio.",
                        $"FreePBX's only copy is '{Printable(chosenFile)}', a raw {Path.GetExtension(chosenFile)} file the converter does not read (it takes WAV and MP3 from an export).",
                        "Convert it to WAV elsewhere and upload it on the Announcements page.");
                }

                taken.Add(name);
                this.plan.Sounds.Add(new PlannedSound
                {
                    Announcement = announcement,
                    Convertible = convertible,
                    IgnoredFiles = files.Where(f => f.Path != chosen.Path).Select(f => Text(f.Sound.Filename)).ToList(),
                    SourceFile = chosenFile,
                    SourcePath = chosen.Path,
                });
            }
        }

        /// <summary>
        /// The trunks, every one disabled (D170): a migration runs while production is live, and
        /// a trunk that registered from here would fight it for the provider's registration.
        /// </summary>
        private void PlanTrunks()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chanSip = 0;

            foreach (var source in this.manifest.Trunks ?? new List<ManifestTrunk>())
            {
                var raw = Text(source.Name);
                var name = UniqueSectionName(SectionName(raw, "trunk"), taken);
                var label = $"Trunk '{Printable(raw)}'";

                if (this.trunkNames.ContainsKey(raw))
                {
                    this.Warn(MigrationSection.Trunks, $"A second {label.ToLowerInvariant()} was not imported.",
                        "The export lists that trunk name twice; the first one was imported.", "");
                    continue;
                }

                var username = Text(source.Username);
                var authUsername = Text(source.AuthUsername);

                var trunk = new Trunk
                {
                    AuthUsername = string.Equals(authUsername, username, StringComparison.Ordinal) ? "" : authUsername,
                    Enabled = false,
                    Name = name,
                    Password = Text(source.Password),
                    Register = source.Register,
                    ServerHost = Text(source.ServerHost),
                    ServerPort = source.ServerPort == 0 ? 5060 : source.ServerPort,
                    Username = username,
                };

                var codecs = SipCodecs.Parse(source.Codecs);
                var kept = codecs.Where(SipCodecs.IsAllowed).ToList();
                trunk.Codecs = kept.Count > 0 ? string.Join(",", kept) : SipCodecs.Default;

                var dropped = codecs.Except(kept).ToList();
                if (dropped.Count > 0)
                {
                    this.Warn(MigrationSection.Trunks, $"{label} lost codecs {string.Join(", ", dropped)}.",
                        $"TNPBX loads {string.Join(", ", SipCodecs.Allowed)} only (D31).",
                        "Nothing, unless the provider insists on one of them.");
                }

                var addresses = Text(source.MatchAddresses)
                    .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                var matched = addresses.Where(a => Ipv4Networks.TryParse(a, out _)).ToList();
                trunk.MatchAddresses = string.Join(",", matched);

                if (matched.Count < addresses.Count)
                {
                    this.Warn(MigrationSection.Trunks, $"{label} lost provider addresses {string.Join(", ", addresses.Except(matched).Select(Printable))}.",
                        "TNPBX matches a provider by IPv4 address or CIDR range; host names and IPv6 are not carried.",
                        "Look up the provider's signalling addresses and add them on the Trunks page.");
                }

                if (matched.Count == 0)
                {
                    this.Warn(MigrationSection.Trunks, $"{label} has no provider addresses.",
                        "Without them TNPBX cannot tell a call from this provider from a stranger's, and refuses it.",
                        "Before cutover, add the provider's signalling addresses on the Trunks page.");
                }

                var callerID = Text(source.CallerIDNumber);
                if (callerID.Length > 0)
                {
                    if (CallerIDFormat.TryParse(callerID, out var callerName, out var callerNumber))
                    {
                        trunk.CallerIDName = callerName;
                        trunk.CallerIDNumber = callerNumber;
                    }

                    if (trunk.CallerIDNumber.Length == 0 || trunk.Validate().Any(e => e.StartsWith("Caller ID", StringComparison.Ordinal)))
                    {
                        trunk.CallerIDName = "";
                        trunk.CallerIDNumber = "";
                        this.Warn(MigrationSection.Trunks, $"{label} lost its caller ID '{Printable(callerID)}'.",
                            "It is not a number of 2 to 20 digits or \"Name\" <number>.",
                            "Set it by hand on the Trunks page.");
                    }
                }

                var fromUser = Text(source.FromUser);
                if (fromUser.Length > 0 && !string.Equals(fromUser, trunk.Username, StringComparison.Ordinal))
                {
                    this.Warn(MigrationSection.Trunks, $"{label}'s From user '{Printable(fromUser)}' was not carried.",
                        "TNPBX sends the trunk's username as the From user.",
                        "If the provider needs a different one, that is not something TNPBX v1 can set.");
                }

                var fromDomain = Text(source.FromDomain);
                if (fromDomain.Length > 0 && !string.Equals(fromDomain, trunk.ServerHost, StringComparison.OrdinalIgnoreCase))
                {
                    this.Warn(MigrationSection.Trunks, $"{label}'s From domain '{Printable(fromDomain)}' was not carried.",
                        "TNPBX sends the server host as the From domain.",
                        "If the provider needs a different one, that is not something TNPBX v1 can set.");
                }

                // A password with nobody to send it as: FreePBX keeps a chan_sip trunk's username
                // in places the export may not have read. The trunk lands disabled whatever
                // happens (D170), so landing it without the password — and its routes with it —
                // beats losing every route that goes out over it.
                if (trunk.Username.Length == 0 && trunk.AuthUsername.Length == 0 && trunk.Password.Length > 0)
                {
                    trunk.Password = "";
                    trunk.Register = false;
                    this.Warn(MigrationSection.Trunks, $"{label} was imported without its password, and not registering.",
                        "The export has a password for it but no username, and a password needs a username to go with it.",
                        "Before cutover, enter the provider's username and password on the Trunks page and switch on registration if it registers.");
                }

                var errors = trunk.Validate();
                if (errors.Count > 0)
                {
                    this.Warn(MigrationSection.Trunks, $"{label} was not imported.", string.Join(" ", errors),
                        "Add it by hand on the Trunks page; the routes that used it are listed below.");
                    continue;
                }

                if (!string.Equals(name, raw, StringComparison.Ordinal))
                {
                    this.Warn(MigrationSection.Trunks, $"{label} is called '{name}' here.",
                        taken.Contains(SectionName(raw, "trunk"))
                            ? "This TNPBX already has a trunk with that name."
                            : "A TNPBX trunk name starts with a letter and is letters, digits and dashes, up to 32 (D37).",
                        "Nothing.");
                }

                if (string.Equals(Text(source.Tech), "sip", StringComparison.OrdinalIgnoreCase))
                    chanSip++;

                taken.Add(name);
                this.trunkNames[raw] = name;
                this.plan.Trunks.Add(trunk);
            }

            if (chanSip > 0)
            {
                this.Warn(MigrationSection.Trunks, $"{chanSip} chan_sip trunk(s) become PJSIP trunks.",
                    "TNPBX speaks PJSIP only (D170).",
                    "At cutover, check each registers and carries a test call before relying on it.");
            }

            if (this.plan.Trunks.Count > 0)
            {
                this.Warn(MigrationSection.Trunks,
                    $"Every imported trunk is DISABLED: {string.Join(", ", this.plan.Trunks.Select(t => t.Name))}.",
                    "This migration runs while the FreePBX box is still live. A trunk that registered from here would " +
                    "fight production for the provider's registration and could take its inbound calls (D170).",
                    "At cutover, once FreePBX's trunks are off, switch each one on on the Trunks page and apply.");
            }
        }

        /// <summary>
        /// The messages under <c>files/voicemail/&lt;ext&gt;/INBOX|Old</c>, counted from the files
        /// rather than the manifest. Only mailboxes this import creates receive any: a mailbox
        /// that already exists here is somebody's, and app_voicemail numbers its messages itself.
        /// </summary>
        private void PlanVoicemail()
        {
            var voicemailRoot = Path.Combine(this.root, "files", "voicemail");
            if (!Directory.Exists(voicemailRoot))
                return;

            var imported = this.plan.Extensions.ToDictionary(e => e.Number, StringComparer.Ordinal);

            foreach (var directory in Directory.EnumerateDirectories(voicemailRoot).OrderBy(d => d, StringComparer.Ordinal))
            {
                var mailbox = Path.GetFileName(directory);
                var planned = new PlannedMailbox { Mailbox = mailbox, SourceDirectory = directory };

                foreach (var folder in new[] { "INBOX", "Old" })
                {
                    var folderPath = Path.Combine(directory, folder);
                    if (!Directory.Exists(folderPath))
                        continue;

                    planned.Files.AddRange(Directory.EnumerateFiles(folderPath)
                        .Select(Path.GetFileName)
                        .Where(f => f != null && MessageFilePattern().IsMatch(f))
                        .OrderBy(f => f, StringComparer.Ordinal)
                        .Select(f => $"{folder}/{f}"));
                }

                if (planned.Files.Count == 0)
                    continue;

                if (!Extension.IsValidNumber(mailbox) || !imported.TryGetValue(mailbox, out var extension))
                {
                    this.Warn(MigrationSection.Voicemail, $"{planned.Messages} voicemail message(s) for mailbox {Printable(mailbox)} were not copied.",
                        $"Extension {Printable(mailbox)} was not imported.",
                        $"If they matter, copy them by hand from files/voicemail/{Printable(mailbox)} in the export.");
                    continue;
                }

                if (!extension.VoicemailEnabled)
                {
                    this.Warn(MigrationSection.Voicemail, $"Mailbox {mailbox}'s {planned.Messages} message(s) are copied, but its voicemail is off.",
                        "FreePBX had messages in a mailbox it no longer had switched on.",
                        "Switch voicemail on for the extension if the user should hear them.");
                }

                this.plan.Mailboxes.Add(planned);
            }
        }

        /// <summary>
        /// FreePBX's Polycom model as the phone's own User-Agent says it, which is what the
        /// provisioning endpoint compares (D78): <c>VVX-VVX_410-UA</c> is <c>VVX_410</c>. A model
        /// this cannot read is left blank, which matches anything until the phone's first fetch
        /// fills it in — a wrong model would have the phone refused.
        /// </summary>
        private static string PolycomModel(string? raw)
        {
            var text = Text(raw);

            var wrapped = WrappedModelPattern().Match(text);
            if (wrapped.Success)
                return wrapped.Groups["model"].Value;

            return BareModelPattern().IsMatch(text) ? text : "";
        }

        /// <summary>A manifest value fit for a message: no control characters, not a page long.</summary>
        private static string Printable(string? raw)
        {
            var clean = new string(Text(raw).Select(c => char.IsControl(c) ? '?' : c).ToArray());
            return clean.Length <= 80 ? clean : clean[..80] + "…";
        }

        /// <summary>Whether a call or a key can point at this extension: the import clears first (D174), so about-to-be is the whole question.</summary>
        private bool Reachable(string number) =>
            this.plan.Extensions.Any(e => string.Equals(e.Number, number, StringComparison.Ordinal));

        /// <summary>The file name without the audio extension FreePBX added, so that formats of one recording group.</summary>
        private static string StripAudioExtension(string file) => AudioExtensionPattern().Replace(file, "");

        /// <summary>A manifest string, trimmed, null as empty.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary><paramref name="name"/>, or the first "name (imported)", "name (imported 2)"… that is free.</summary>
        private static string UniqueAnnouncementName(string name, ISet<string> taken)
        {
            if (!taken.Contains(name))
                return name;

            for (var n = 1; ; n++)
            {
                var suffix = n == 1 ? " (imported)" : $" (imported {n.ToString(CultureInfo.InvariantCulture)})";
                var head = name.Length > 64 - suffix.Length ? name[..(64 - suffix.Length)].TrimEnd() : name;
                var candidate = head + suffix;

                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        private void Warn(string section, string what, string why, string action) =>
            this.plan.Warnings.Add(new MigrationWarning(section, what, why, action));

        [GeneratedRegex(@"\.(wav|gsm|ulaw|alaw|g722|g729|sln|sln16|mp3)$", RegexOptions.IgnoreCase)]
        private static partial Regex AudioExtensionPattern();

        [GeneratedRegex(@"^[A-Za-z0-9_]{1,32}\z")]
        private static partial Regex BareModelPattern();

        [GeneratedRegex(@"^[0-9]{1,15}\z")]
        private static partial Regex DIDPattern();

        /// <summary>What app_voicemail writes: <c>msg0000.txt</c>, <c>msg0000.wav</c>, <c>msg0000.WAV</c>, <c>msg0000.gsm</c>.</summary>
        [GeneratedRegex(@"^msg[0-9]{4}\.[A-Za-z0-9]{1,5}\z")]
        private static partial Regex MessageFilePattern();

        [GeneratedRegex(@"^7(?<slot>[1-9])\z")]
        private static partial Regex ParkingSlotPattern();

        /// <summary>FreePBX's Polycom model as the module stores it: <c>VVX-VVX_410-UA</c>.</summary>
        [GeneratedRegex(@"^[A-Za-z0-9]+-(?<model>[A-Za-z0-9_]{1,32})-UA\z")]
        private static partial Regex WrappedModelPattern();
    }
}
