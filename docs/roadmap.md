# Roadmap

Built piece by piece. Each piece should build, pass tests and be verified on the lab VM
before the next one starts. **What** we build is defined in [features.md](features.md);
this file is the **order**.

| # | Piece | Feature | Status |
|---|---|---|---|
| 0 | Architecture decisions | | Done (see decisions.md) |
| 1 | Spike: Asterisk LTS built from source on a Debian VM, hand-written config, echo test | | Done 2026-09-13 |
| 2 | Solution layout, log4net, cookie auth | | Done 2026-09-13 |
| 3 | Extensions: model, schema, repository, pjsip/extensions renderers, atomic writer, tests | F2 | Done 2026-09-13 |
| 4 | AMI client + "apply config" (DB → render → write → reload only what changed) | | Done 2026-09-15 |
| 5 | Extensions UI: table + modals, live registration status, local auth bypass (D24), Data folder (D25), config-pending marker (D26) | F2 | Done 2026-09-15 (verified on lab VM: add/edit/apply/status/secret all exercised end-to-end) |
| 6 | Auth hardening: app role requirement (break-glass deferred) | | |
| 7 | Generate the remaining base config: `modules.conf` allowlist, `logger.conf`, `rtp.conf`, `manager.conf`, `asterisk.conf` | | Done 2026-09-16. Lab-verified: restart onto the 32-module allowlist, SIP registration, AMI login via regenerated `manager.conf`, and voicemail all pass. `/etc/asterisk` is now 100% DB-generated. |
| 8 | Destinations: shared "send call to X" model + dialplan helper | supporting | Done 2026-09-17 (D35 derived model, D36 helper; golden extensions.conf unchanged; deployed + lab-verified) |
| 9 | Generic SIP trunk | F1 | Done 2026-09-17 (D37–D41). Lab-verified: Callcentric trunk created in the UI, config applied, `callcentric-reg` **Registered** to sip.callcentric.net, renewal confirmed, AMI status endpoint live. Outbound audio test deferred to piece 10 (needs a route). |
| 10 | Outbound routes (international restricted by default) | supporting | **Done 2026-09-18** (D44–D48). Lab-verified end-to-end: real call from MicroSIP over the Callcentric trunk, answered and audio-bridged. Fixed en route: allowlist was missing `res_pjsip_pubsub` (chan_pjsip would not load — calls silently stalled), and trunk dial needs the full URI form. International patterns refused by validation. |
| 11 | Inbound routes (DID → destination) | supporting | **Done 2026-09-19** (D49–D51). Lab-verified end-to-end: call to DID 19498800842 from a cell rings ext 1003 on MicroSIP with caller ID. DID dispatched from the To header per Callcentric's routing doc (`PJSIP_HEADER` + `CUT`, `func_cut` in the allowlist); catch-all flag per trunk; unmatched calls hang up unanswered (D50). |
| 12 | Callcentric wizard (verify settings on lab VM first) | F1 | **Skipped by user 2026-09-19**: no wizard; provider-specific settings (identify CIDRs, To-header DID matching) were verified live with Callcentric and noted in D51 — **"tested with Callcentric"**. Revisit if another provider (voip.ms) is tried. |
| 13 | Voicemail | F2a | Done 2026-09-16, except email (piece 14). Lab-verified: UI enable -> voicemail.conf + dialplan -> real unanswered call -> message recorded in INBOX. |
| 14 | Email notifications (voicemail to email first, then alerts) | F4 | **Partly built 2026-09-19, pending lab verification** (D114, D115). How mail leaves the box is done: the embedded alert template, the seven `Mail.*` settings on the new System page's Email tab, both transports (Graph via this app's own Entra registration, SMTP via `SmtpClient`), and a "send test mail" button that is the only thing sending anything today. **Not built:** voicemail to email, which is still Asterisk's own job through a local MTA and unchanged by D115, and the alerts themselves — nothing raises one yet. Graph additionally needs `Mail.Send` application permission with admin consent and `AzureAd:ClientSecret` on the server, neither of which is a code change. |
| 15 | Ring groups: ring all + hunt | F3 | |
| 16 | Follow me | F2b | |
| 16a | Announcements (upload/record, convert, play ext, destination type) | F7 | **Done 2026-09-19** (D55–D57), lab-verified: MP3 uploaded through the UI -> ffmpeg converted to 8 kHz mono WAV -> apply -> `dialplan show 7100@internal` shows Answer/Playback/Hangup live, file at `/var/lib/asterisk/sounds/tnpbx/announcements/1/` 0640 group asterisk. IVR (17) reuses the audio handling. |
| 17 | IVRs (uses announcement audio) | F6 | **Done 2026-09-19** (D58–D61), lab-verified: IVR "Main menu" at 7102 created through the UI, greeting = holiday-test announcement, key 1 → ext 1003 — dialed live from MicroSIP, greeting played, digit routing + direct dial confirmed by the user. Model + schema `009_ivrs.sql` + repository, `ivr-<IvrID>` contexts in `extensions.conf`, `Ivr` destination type, `func_timeout.so` in the allowlist, IVRs page. See [piece 17 detail](#piece-17-detail-built-2026-09-19-not-yet-lab-verified). |
| 17a | Time conditions | F8 | **Built 2026-09-19, pending lab verification** (D62–D66). Model + schema `010_time_conditions.sql` + repository with validation, `tc-<TimeConditionID>` contexts (holidays first, then weekly GotoIfTime, then closed), `TimeCondition` destination type, Timezone setting, and the Time Conditions page (open-hours rows with weekday pickers, holiday date rows with per-date override, three destination selects). No module additions. **2026-09-20 amendment**: timezone became a dropdown and every `GotoIfTime` names the zone as its fifth argument, so open hours are entered in the customer's local time and evaluated there (DST included) while the server clock is meant to be UTC — the installer (piece 21) will set that. D74, D75. |
| 17b | Settings UI: general key/value page + SIP Settings page (transports, NAT, STUN, codecs) | supporting | **Done 2026-09-20** (D67–D73). Lab-verified end-to-end: values saved through the UI's form endpoint, Apply reloaded pjsip and reported the rtp.conf restart honestly; after restart `pjsip show transports` shows transport-tcp 0.0.0.0:5062, `rtp show settings` shows ICE Yes + STUN stun.l.google.com:19302, ext 1001 re-registered by real SIP digest, endpoints show settings-driven `allow = ulaw,alaw`. TLS port stored only (D71) until certificate management is a piece. |
| 18 | Call reports | F5 | |
| 22 | Phone provisioning: Polycom (DHCP option 160 -> http://user:pass@host/polycom, MAC capture on first contact, generated per-device config) | supporting | **Done 2026-09-21** (D77–D83). Lab-verified with a spoofed VVX 450 User-Agent: no/bad Basic auth → 401, valid auth + non-Polycom UA → 403; first contact auto-registered the MAC (model, firmware, IP); unassigned phone got a minimal no-reg config; after assigning ext 1001 in the UI the generated config carries reg.1 with the real secret, server, MWI and the deterministic local SIP port; a model mismatch on a known MAC → 403. Schema `011_phones.sql`, `Phone` model + `PhoneRepository`, `PolycomUserAgent`, `BasicAuth`, the `/polycom` controller outside the Entra cookie, master + per-phone renderers with golden files, `Provisioning.Username`/`Provisioning.Password` settings keys, and the Phones page. **Round 2 (D84–D87)**: NTP server as a setting (default pool.ntp.org), Polycom device web passwords as secret settings (admin one doubles as the push digest credential), push-on-save `Action:UpdateConfig` + Reboot phone button — best-effort, polling is the safety net. **Round 3 — Yealink (D88–D91)**: `/yealink` endpoint on the same gate, plain key=value config generated per request, Brand column (schema 012), tabbed Phones page (Phones | Settings → Polycom | Yealink), SIP-NOTIFY signal via generated `notify.conf` + AMI PJSIPSendNotify (res_pjsip_notify added to the allowlist); Yealink uses DHCP option 66. Yealink path pending lab verification. See [piece 22 detail](#piece-22-detail-built-2026-09-21-not-yet-lab-verified). |
| 26 | Call parking + music on hold | supporting | **Built 2026-09-19, pending lab verification** (D119). One parking lot, off by default: `*3` parks the call you are on, Asterisk speaks the slot number to whoever parked it, and dialling that single digit from any phone picks the call up. Five `Parking.*` settings (all Asterisk-scoped), three new generated files — `features.conf` (`parkcall`), `res_parking.conf` (the lot; **not** parking.conf, which Asterisk 12 retired) and `musiconhold.conf` (one class called `parking`, `mode=files` on `/var/lib/asterisk/moh`) — plus one `ParkedCall()` entry per slot in `extensions.conf`, written out rather than generated from a `parkext` (D12, D46, D60). **Every generated `Dial()` now carries `tTkK`**: `k`/`K` are what make the feature code reachable at all, and `t`/`T` come with them, which also turns on Asterisk's default `#` blind transfer. Silence is implemented as having no class to start, so the generated `musiconhold.conf` deliberately never defines a class called `default`. New table `MohFiles` (017) + `MohFileRepository` + `MohStore` (the announcements convert-on-upload pattern, D55, one flat directory), three modules on the allowlist (`res_parking.so`, `res_musiconhold.so`, `bridge_holding.so`), and a `/Parking` page under Call Handling. |
| 19 | Helper: Unix socket, peer credential check, first commands (firewall) | | |
| 20 | fail2ban setup, then own AMI-security-event blocker via Helper | | |
| 23 | Certificate management: in-process ACME (Certes, HTTP-01), Kestrel on 80/443, SIP TLS, auto-renewal | security | **Done 2026-09-22, verified live end to end** (D97–D101). `013_certificates.sql` + `CertificateRepository` (Current() = newest usable), Certes-based `AcmeCertificateService` answering its own HTTP-01 challenges on port 80, `WebBindings` (80 + 8080 always, 443 with a usable cert; `CAP_NET_BIND_SERVICE` in the unit), daily renewal at ≤30 days, Certificates page, combined `tnpbx-cert.pem` feeding the generated pjsip `transport-tls` on `Sip.TlsPort` (D71 real). **Live verification**: staging order first (proved the ACME loop through the Azure NAT, caught a Certes cross-signed-chain bug we fixed by taking leaf/issuers off the chain object), then a **production order — `pbx.example.com` serves a real Let's Encrypt cert on 443**, port 80 redirects everything except provisioning + the ACME challenge (a redirect-on-8080 bug fixed en route: 8080 stays plain HTTP as the way back in), and Asterisk serves `transport-tls` on 5061 with the same cert. 857 tests. |
| 21 | Installer script for fresh Debian (users, permissions, hardened systemd units, polkit rule, Asterisk build) | | **Done 2026-09-22** (D92–D96). Part 1 (`scripts/install.sh`) verified one-shot on a freshly wiped lab VM (Debian 13.7, zero manual steps): UTC clock + NTP synced, `tnpbx` in the `asterisk` group, `/etc/asterisk` root:asterisk 2770 and empty, `/opt/tnpbx` deploy target, Asterisk 22.11.0, unit enabled but inactive by design. Part 2 (`scripts/app-deploy.sh <publish.tgz>`, D95) deployed the app the same day: hardened `tnpbx-web.service` (User=tnpbx, ProtectSystem=strict), polkit rule scoped to exactly `asterisk.service`, `appsettings.json` + `Data/` preserved across re-deploys. Full first-run proven on the fresh box (D96): sign in → settings → extension → first apply wrote all nine conf files → Asterisk started on generated config → AMI/pjsip/provisioning live. See [piece 21 detail](#piece-21-detail-part-1-built-2026-09-22-not-yet-verified). |
| 24 | Smarter apply: settings scopes, and a confirmed Asterisk restart after apply | supporting | **Built 2026-09-23, pending lab verification** (D103, D104). Settings carry a `SettingScope` (Asterisk / Phones / App) classified by what actually reads each key, and only an Asterisk-scoped write raises the config-pending marker — a provisioning password or the phones' NTP server no longer lights the apply button, and the toast says phones pick it up at their next poll. The apply response now carries `RestartRequired` + `RestartFiles`; when an apply writes a startup-only file the page asks with a sweetalert2 confirm and, on yes, posts `/api/config/restartAsterisk`, which runs `systemctl restart asterisk.service` as `tnpbx` through the existing polkit rule (no sudo, no shell, 30s timeout). A declined restart leaves an "Asterisk restart required" badge + Restart button in the navbar poll area, backed by `AsteriskRestartMarker` beside the database. 890 tests. |
| 25 | Status home page + Logs page (Status menu) | supporting | **Done 2026-09-19, lab-verified** (D106, D107). Deployed to the lab VM and checked live: tiles read a real Asterisk (uptime, 1 of 1 trunk, 1 of 2 extensions, cert 89 days), the attention list named the two unassigned phones, the Logs page tailed `messages.log` / the app log with filter and Follow working, `tnpbx` reads `/var/log/asterisk/*.log` as-is (Asterisk creates them 0644), and a path as a source name got a 400. Not yet seen live: a bridged call in the calls table (no call was placed). `/` is the status page: six health tiles + calls in progress polled every 5 s over one AMI session (`CoreShowChannels` / `CoreStatus` added to `AmiSession`, no permission change), a "needs attention" list from the pure, tested `AttentionRules` (dangling destinations, unregistered / rejected trunks, trunks with no routes, phones without extensions, certificate expiry and failed orders, disk usage, UTC time conditions, AMI down, apply / restart owed), and counts. `/Status/Logs` tails `messages.log`, `security.log` or the log4net file — fixed allowlist, no path from the browser, 4 MB scan cap, follow mode. New setting `Asterisk.LogDirectory`. 954 tests. Seen on the lab VM: every 5-second poll is an AMI login, and each login is a `SuccessfulAuth` line in `security.log` and `messages.log` — the extensions and trunks pages already did this, but the home page makes it constant while anyone has it open. Left as is; a long-lived AMI session is the fix if it matters. **2026-09-19 amendment (D116)**: the Logs page gained a fourth source, **Web requests (W3C)**. Kestrel now keeps a W3C request log of its own (`AddW3CLogging`, first in the pipeline so it wraps auth) in `logs/requests/` inside the install — client address, port, method, path, status, time taken, User-Agent and Referer, no cookie and no `sc-bytes` (the logger has no bytes field). It is the answer to "did the phone even reach us": `/polycom` and `/yealink` have no session, and `RequestLogUserMiddleware` names those requests after the Basic username so `cs-username` is not blank for them. New setting `Web.RequestLog` (App scope, on by default, **read at startup — restart tnpbx-web after changing it**) and the first `Toggles` (`on`/`off`) setting. No `app-deploy.sh` change: `/opt/tnpbx` is already in `ReadWritePaths`. |
| 27 | Navbar regroup + printable cheat sheet | supporting | **Built 2026-09-20, pending lab verification** (D120). The navbar is now four dropdowns — **Connectivity** (Extensions, Phones, Trunks, then the cheat sheet under a divider), Call Handling (unchanged), Settings (which gains Certificates as a menu item; the page stays at `/Certificates`) and Status, now last. `/Connectivity/CheatSheet` is a read-only, server-rendered page for office staff rather than admins: every enabled extension by number, and every code the generated config really carries, from the pure `FeatureCodes.All(parking, voicemailInUse)` — `*2`, `#`, the park code and slot range when parking is on, `*97` when anyone has a mailbox, `*43`. No schema, no settings, no new permission, no secrets on the page. Printing is Bootstrap's `d-print-none` on the layout's navbar and footer plus one `@media print` block in `site.css` that forces light colours, so dark mode does not print white text on white paper. **Not verified without a browser:** the printed result itself — margins, one-page fit and the dark-mode override all need a real print preview on the lab VM. |
| 28 | Phone buttons: eight assignable keys per phone (BLF + quick dial) | supporting | **Built 2026-09-20, pending lab verification** (D121). The phone edit modal is now two tabs, **Buttons** (which opens) and Details. Eight dropdowns, each nothing, an extension, or a parking slot when parking is on. New table `PhoneButtons` (018) + `PhoneButtonRepository` — `TargetType` is plain `TEXT` with no `CHECK`, so call flow control becomes a third kind with no schema script. The dialplan grows hints in `[internal]`: `PJSIP/<number>` per enabled extension and `park:<slot>@parkedcalls` per slot, the park device name read off `res/res_parking.c`. Polycom renders the keys as an attendant resource list (`attendant.reg` + `resourceList.N.address/label/type`), numbered 1..n in key order so an unassigned key leaves no gap; Yealink renders nothing for them yet. Two modules join the allowlist for the other half of a lamp: `res_pjsip_exten_state.so` and `res_pjsip_dialog_info_body_generator.so`. `modal-dialog-centered` is gone from every modal. **Not verified:** a real handset lighting up — the lamps, the labels and how many keys a VVX actually shows need a phone on the lab VM. |

The order after piece 7 is a proposal. The reasoning: trunks and routes first so the system can
make and take real calls; voicemail before email because voicemail-to-email is the first email
use; ring groups, follow me and IVR all send calls to destinations, so destinations come early;
reports last because they only need call data to exist. Helper, fail2ban and installer can move
earlier whenever deployment needs them. Record changes here.

## Piece 4 detail (done 2026-09-15)

- `Techie.Pbx.Asterisk/Ami/`: minimal AMI client over TCP. `Login`, `Logoff`, action/response
  with ActionID matching, event lists, event reading, `Reload` and `PJSIPShowContacts`.
  `Action: Command` was dropped (D17), so the AMI user needs no `command` permission.
- `ConfigApplier`: renders every file from the database, writes with `WriteAtomic`, then reloads
  only the modules whose files changed (D16). `ConfigApplier.FromDatabase` is the normal wiring.
- Settings: `Settings` key/value table, script `002_settings.sql`, keys in `SettingsKeys`,
  read through `SettingsRepository` and turned into objects by `AsteriskSettings`. The AMI
  secret lives there too (D14, D15).
- Tests: AMI protocol and session against canned responses, settings repository and loader,
  applier from database to files. The end-to-end reload check happens on the lab VM.

Left for later, on purpose: nothing writes the settings yet (no UI, and `manager.conf` with the
AMI user is generated in piece 7), so a lab VM has to have its AMI rows inserted by hand or by
the lab script until then.

## Piece 5 detail (code done 2026-09-15)

- `/Extensions`: bootstrap-table of number, name, enabled and a live registration badge, with
  create, edit and delete in modals, "show password", "regenerate password" and
  "apply config". The page is a shell; htmx fetches every part of it from page handlers as HTML
  partials, and changes answer 204 with `HX-Trigger` events (D21). The create/edit form moved
  from a sweetalert2 popup to a Bootstrap modal on 2026-09-18 (D42).
- `RegistrationStatus` (in `Techie.Pbx.Asterisk/Ami`) turns `PJSIPShowContacts` into a state per
  extension number and never throws: no AMI means every badge says Unknown. The page polls it
  once every 5 seconds and htmx swaps the badges out of band.
- `PbxDatabase` opens the database at startup from `Database:Path` (D22); pages and controllers
  build their own repositories over it.
- API: `POST /api/config/apply`, `GET`/`POST /api/extensions/{id}/secret`. Unauthenticated calls
  now get 401 rather than a redirect (D20), and every browser call carries an antiforgery header
  (D23).

Left over from this piece: per-extension caller ID for outbound calls (listed under F2) is not
built; it waits for trunks and outbound routes to exist.

## Piece 13 detail (in progress, started 2026-09-16)

Pulled forward from its place in the order because extensions are the only thing that exists to
hang it on. Scope is F2a minus the email itself.

- Schema `003_voicemail.sql`: five `Voicemail*` columns on `Extensions` (D27).
- `VoicemailConfRenderer` writes `voicemail.conf`: a `[general]` section (wav49, 100 messages,
  5 minutes each) and one mailbox line per enabled extension that asked for one, in context
  `default`. `ConfigApplier` reloads `app_voicemail` when that file changes.
- `ExtensionsConfRenderer`: busy and unanswered calls fall back to the mailbox (D29), and `*97`
  plays your own messages. Extensions without voicemail are unchanged.
- The extension modal gains the voicemail fields, and says plainly that no email is sent yet.

Still to do:

- **Email delivery is not built** (F4, piece 14): addresses and the attach/delete toggles are
  stored and written into `voicemail.conf`, but with no `serveremail` or template Asterisk sends
  nothing. The open question there — SMTP relay versus Microsoft Graph, and whether Asterisk
  sends or the app does — is still open.
- Voicemail as a **destination** for inbound routes, IVRs and ring group failover waits for
  piece 8 (destinations).
- ~~**MWI** (the message-waiting light on the phone) is not configured: it needs `mailboxes =` on
  the PJSIP endpoint and a decision about subscriptions.~~ Done 2026-09-19 (D108), after Claude
  saw the phones' subscribe failures in the lab VM logs: `res_pjsip_mwi` +
  `res_pjsip_mwi_body_generator` on the allowlist and `mailboxes = <ext>@default` on endpoints
  with voicemail. Needs an Asterisk restart (modules.conf, D33) to take effect.
- Run it on the lab VM: leave a message, listen to it with `*97`, check the busy greeting.

## Piece 7 detail (in progress, started 2026-09-16)

`/etc/asterisk` is now generated end to end: eight files, no hand-written ones left. The lab
script still writes its own versions, and it has to — Asterisk must be running before the app can
apply anything — but the first apply replaces every one of them.

| File | Renderer | Applied by |
|---|---|---|
| `asterisk.conf` | `AsteriskConfRenderer` | restart (D33) |
| `modules.conf` | `ModulesConfRenderer` | restart (D33) |
| `rtp.conf` | `RtpConfRenderer` | restart (D33) |
| `logger.conf` | `LoggerConfRenderer` | `logger` reload |
| `manager.conf` | `ManagerConfRenderer` | `manager` reload, last and disconnect-tolerant (D34) |
| `pjsip.conf` | `PjsipConfRenderer` | `res_pjsip` reload |
| `extensions.conf` | `ExtensionsConfRenderer` | `pbx_config` reload |
| `voicemail.conf` | `VoicemailConfRenderer` | `app_voicemail` reload |

`ApplyResult` grew `RestartRequiredFiles`/`RestartRequired`, and the apply toast says so.

Still to do:

- **Verify the module allowlist on the lab VM** (D31). This is the one that needs real hardware
  in front of it: restart on the generated `modules.conf`, then `core show modules`, register a
  phone, place a call, leave a message, and read the log for "Error loading module". Expect to
  iterate — a missing module usually shows up as a feature that silently does nothing.
- **The AMI secret has to be in the database before the first apply**, or rendering
  `manager.conf` fails with "AMI secret is required" and nothing is written. The lab script still
  generates its own secret into its own `manager.conf`; until there is a settings UI, the two are
  reconciled by putting the script's secret into the `Settings` table by hand.
- Confirm whether `res_rtp_asterisk` reloads `rtp.conf` cleanly (D33), and move it out of the
  restart group if it does.
- ~~Restarting Asterisk is still a manual step.~~ Done in piece 24 (D104): an apply that owes a
  restart offers it, the app runs it through the polkit rule, and a declined restart leaves a
  banner in the navbar.

## Piece 8 detail (in progress, started 2026-09-17)

The shared "send the call to X" model, for the four features that will send calls somewhere.
**No schema script**: a destination is a reference to something that already has a table, so the
list is computed and a stored choice is two columns on whichever feature stores it (D35).

- `Core/Models`: `DestinationType` (Extension, Hangup, Voicemail), `Destination` (type + value,
  `Key`/`TryParse` for the string form), `DestinationChoice`, and `DestinationCatalog` — a pure
  function from the rows a caller loaded to the list a picker shows.
- `Asterisk/Config/DestinationDialplan`: the only code that writes "and then the call goes here"
  (D36). `ExtensionsConfRenderer` now uses it for the voicemail fallback, and the golden
  `extensions.conf` did not change.
- `Pages/Shared/_DestinationSelect.cshtml`: the shared picker, grouped by type, which shows a
  destination that no longer resolves instead of silently repointing it.

Still to do:

- **Nothing renders the picker yet.** It compiles, and the logic behind it is tested in Core, but
  it is not on a page until inbound routes (piece 11) or IVRs (piece 17) need it. The extensions
  page had no field it belonged in, and adding one would have been building a feature that is not
  on the list.
- Each feature that stores a destination adds `DestinationType` / `DestinationValue` columns in
  its own schema script, and validates on save that the destination still resolves.
- New destination types are additive: a member on the enum, a case in `DestinationDialplan`, a
  source in `DestinationCatalog.All`. Ring groups and IVRs will each add one.

## Piece 10 detail (in progress, started 2026-09-18)

Which numbers go out, and over which trunk. Schema `005_outbound_routes.sql`, a repository that
refuses international patterns outright (D47), and a dialplan of one context per route included in
priority order, ending in a context that stops everything nothing matched (D45, D46).

Two UI changes landed alongside it, both user decisions: the apply button moved into the navbar and
only exists when there is something to apply (D43), and table rows became clickable with their
actions in the edit modal's footer (D48). Extensions and Trunks were converted too.

Still to do:

- **Place a real outbound call from the lab VM.** Nothing here has dialled a provider yet: worth
  checking the route order, that an unmatched number fails closed, and that `ss-noservice` is
  actually installed (D45) rather than leaving the caller in silence.
- ~~**No strip or prepend digits** (D44), so "dial 9 for an outside line" is not possible.~~
  Added 2026-09-19 (D109): `PrependDigits` / `StripDigits` per route, schema `014`, the
  pattern's underscore stored on save, and the prepend door closed to the international guard.
  Golden file `extensions-routes-digits.conf`; existing goldens unchanged.
- **The international guard assumes North American dialling** (D47). A UK or European site cannot
  write a route at all under it, and will need a deliberate per-system escape hatch.

## Piece 11 detail (in progress, started 2026-09-19)

Which number arriving on which trunk goes where. Schema `006_inbound_routes.sql`, a repository that
checks the trunk and the destination still exist, and entries in each trunk's own context sent on
by the shared destination helper (D36). The `from-trunk-<name>` placeholder from piece 9 is now
only what a trunk with no routes gets (D50).

This is the first consumer of the destination picker built in piece 8 (D35): the form renders
`_DestinationSelect` and stores what it posts as `DestinationType` / `DestinationValue`, exactly
as that decision said a feature would.

Still to do:

- **Take a real inbound call on the lab VM**, and above all **read the log for what Callcentric
  actually puts in `${EXTEN}`** (D51). DIDs are matched exactly, so if the provider sends 10 digits
  where an admin typed 11, the call goes to the catch-all instead. A normalisation step may be
  needed once that is known.
- Destinations are still only Extension, Voicemail and Hangup. Ring groups (piece 15) and IVRs
  (piece 17) each add one, and inbound routes pick them up without changing (D35).
- A route whose destination has since been deleted shows as "gone" in the table and hangs the call
  up in the dialplan. ~~There is no warning anywhere else that it needs fixing.~~ The status
  page's attention list now names every dangling destination (D106).

## Piece 17 detail (built 2026-09-19, not yet lab-verified)

The auto attendant (F6), in two parts: the model, schema `009_ivrs.sql`, repository and renderer
(D58–D61), then the page.

The page is the same shape as the other list pages — a shell htmx fills, the shared Bootstrap
modal for the form (D42), rows that open their own edit form (D48) — with one thing of its own:
the **digit map editor is twelve fixed rows**, one per key a caller can press, each a shared
`_DestinationSelect`. A row left on "Not used" is dropped on save, so adding and removing keys is
choosing and clearing destinations and the editor needs no JavaScript at all. The menu being
edited is in its own keys' picker, because "press 9 to hear this again" is a feature (D59); the
final-destination loop check is the repository's, not the form's.

Still to do:

- **Verify on the lab VM**: `func_timeout.so` is actually built (D61), the core `invalid` prompt is
  installed, and a real call to a menu's play extension plays the greeting, takes a key, times out
  to the final destination and — with direct dial on — reaches an extension.
- **The inbound routes and ring groups forms still offer only the destinations they knew about when
  they were written**: inbound routes offer extensions and voicemail only, ring groups those plus
  ring groups. Both repositories accept announcements and IVRs, and both tables label them, so this
  is one line in each page's `Fill` — but until it is done, **an inbound DID cannot be pointed at an
  IVR from the UI**, which is the main way a menu is meant to be reached.
- The loop check follows IVR → IVR only, so ring group → IVR → ring group is still possible to
  build (D59).

## Piece 22 detail (built 2026-09-21, not yet lab-verified)

Polycom phones configure themselves from the database. Nothing is written to disk and nothing is
applied: both files a phone fetches are generated per request (D79), so this piece adds no
renderer to `ConfigApplier`, no module to the allowlist, and no reason for the apply button to
appear.

| Piece | Where |
|---|---|
| `Phones` table, MAC unique, nullable `ExtensionID` (`ON DELETE SET NULL`, D80) | `Data/Schema/011_phones.sql` |
| `Phone` model (MAC rules, `MatchesModel`, derived local SIP port) | `Core/Models` |
| `PolycomUserAgent` (the header regex, D78) | `Core/Models` |
| `BasicAuth` (parse + fixed-time compare, D77) | `Core/Security` |
| `PhoneRepository`, including `Register` — the write a phone causes | `Core/Data` |
| `PolycomFiles`, `PolycomXml`, `PolycomMasterRenderer`, `PolycomConfigRenderer` | `Asterisk/Provisioning` |
| `GET /polycom/{file}`, `[AllowAnonymous]`, outside the Entra cookie (D77) | `Web/Controllers/PolycomController.cs` |
| `Provisioning.Username` / `Provisioning.Password` (the second is a secret) | `SettingsKeys`, `SettingsValidation`, `SettingsCatalog` |
| Phones page: table, edit modal, no Add button and no apply (D78, D79) | `Web/Pages/Phones` |

Still to do:

- **Verify against a real handset on the lab VM.** In order: set the two provisioning settings,
  point a phone at `http://user:pass@<vm>/polycom` with DHCP option 160, watch the log for the
  auto-add, check the row appears on the Phones page, assign an extension, reboot the phone and
  confirm it registers and can call. Then confirm the 403s: a `curl` with the right credentials
  and no Polycom User-Agent, and a second phone claiming the first one's MAC.
- **`voIpProt.SIP.local.port` is the spelling the approved design specifies; Polycom's own docs
  say `voIpProt.local.port`** (D81). If the phone ignores it — two phones behind one NAT still
  both sourcing from 5060 — it is one constant.
- **The digit map is fixed at four-digit extensions** (`xxxx|*xx.T|[2-9]11|0T`). Extensions here
  may be 2 to 6 digits, so a site numbered 101–199 gets a phone that waits 3 seconds before
  dialling rather than dialling at once. Making the map follow the real extension lengths is a
  question for the user, not something to guess.
- **Daylight saving reaches a phone at its next poll, up to a day late** (D82). Open question:
  accept it, or derive Polycom's `tcpIpApp.sntp.daylightSavings.*` parameters from the zone.
- **Kestrel has to be listening on the port the DHCP option names.** D76 has the app on 80/443
  directly; until the installer (piece 21) sets that up, the lab VM's port goes in the option 160
  URL. Provisioning is exempt from the HTTPS redirect for exactly this reason (D77).
- **No upload endpoints** for the `logs`, `overrides` and `contacts` directories the master file
  names (D79), **no firmware serving** (D83), no BLF, no attendant console, no softkey layout, no
  second line, and no non-Polycom phones. Each is its own piece.

## Piece 21 detail (part 1 built 2026-09-22, not yet verified)

`src/Techie.Pbx.Core/scripts/install.sh` turns a fresh Debian 12/13 server into everything a
TNPBX box is **except the application** (D92). It is the lab spike
(`build-asterisk-vm.sh`) with the hand-written config removed, the `tnpbx` user added and the
layout the lab proved made explicit (D94).

What it produces, in order: preflight (root, Debian version, architecture, memory-aware build
jobs) → the clock set to `Etc/UTC` with NTP before anything else (D74) → one `apt-get` for build
and runtime dependencies, with `libicu72`/`libicu76` chosen by Debian version and `ffmpeg`
because announcements require it (D55) → the `asterisk` and `tnpbx` system users, `tnpbx` in the
`asterisk` group, and the directory layout including `/etc/asterisk` at **2770 root:asterisk**
(D18) and `/opt/tnpbx` at 0750 `tnpbx:asterisk`, created and left empty → Asterisk 22 built from
the tarball with a verified sha256, bundled pjproject and `BUILD_NATIVE` off → our own systemd
unit, `daemon-reload`, `enable`, and **no start** (D93).

`--dry-run` prints every mutating step instead of running it, and skips the root check, so the
whole install can be read before it is run. `--rebuild` forces the Asterisk build the way the
spike script does; otherwise an existing `22.x` binary is left alone.

Still to do:

- **Run it on a genuinely fresh Debian 12 and Debian 13 VM.** Nothing here has been executed
  yet — only `bash -n` and a `--dry-run` pass. The two things most likely to be wrong are the
  package list (a missing runtime dependency only shows up when the app is deployed) and the
  `libicu` name on Debian 12.
- **Part 2: deploy the application.** Publishing it into `/opt/tnpbx`, `tnpbx-web.service` with
  the hardening named in [security.md](security.md) (`NoNewPrivileges`, `ProtectSystem=strict`,
  explicit `ReadWritePaths`), binding 80/443 as an unprivileged user (D76), and the polkit rule
  that lets the web user restart `asterisk.service` (D33).
- **The firewall (piece 19), the Helper (piece 19) and fail2ban (piece 20)** are not installed
  and not referenced. The script prints them as "deliberately not done" so the operator is not
  left believing the box is protected.
- **`make samples` is not run**, so `/etc/asterisk` is empty until the first apply. That is the
  point (D93), but it means an operator who starts Asterisk by hand before deploying the app
  gets an Asterisk with no config at all.
- **The announcements directory is created `asterisk:asterisk` 2770**, which is what the lab VM
  has; D56 describes it as `root:asterisk`. Both work — `tnpbx` writes there through the
  `asterisk` group either way — but the two should be reconciled.
