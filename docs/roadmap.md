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
| 14 | Email notifications (voicemail to email first, then alerts) | F4 | |
| 15 | Ring groups: ring all + hunt | F3 | |
| 16 | Follow me | F2b | |
| 16a | Announcements (upload/record, convert, play ext, destination type) | F7 | **Done 2026-09-19** (D55–D57), lab-verified: MP3 uploaded through the UI -> ffmpeg converted to 8 kHz mono WAV -> apply -> `dialplan show 7100@internal` shows Answer/Playback/Hangup live, file at `/var/lib/asterisk/sounds/tnpbx/announcements/1/` 0640 group asterisk. IVR (17) reuses the audio handling. |
| 17 | IVRs (uses announcement audio) | F6 | **Done 2026-09-19** (D58–D61), lab-verified: IVR "Main menu" at 7102 created through the UI, greeting = holiday-test announcement, key 1 → ext 1003 — dialed live from MicroSIP, greeting played, digit routing + direct dial confirmed by the user. Model + schema `009_ivrs.sql` + repository, `ivr-<IvrID>` contexts in `extensions.conf`, `Ivr` destination type, `func_timeout.so` in the allowlist, IVRs page. See [piece 17 detail](#piece-17-detail-built-2026-09-19-not-yet-lab-verified). |
| 17a | Time conditions | F8 | **Built 2026-09-19, pending lab verification** (D62–D66). Model + schema `010_time_conditions.sql` + repository with validation, `tc-<TimeConditionID>` contexts (holidays first, then weekly GotoIfTime, then closed), `TimeCondition` destination type, Timezone setting, and the Time Conditions page (open-hours rows with weekday pickers, holiday date rows with per-date override, three destination selects). No module additions. |
| 17b | Settings UI: general key/value page + SIP Settings page (transports, NAT, STUN, codecs) | supporting | **Built 2026-09-20, pending lab verification** (D67–D73). Settings dropdown with the general page (every key, modal editing, masked secrets, reset-to-default) and a SIP Settings page (bind address, UDP/TCP/TLS ports, external address, local nets, STUN default stun.l.google.com:19302, codecs from the allowlist's modules). pjsip.conf gains a conditional transport-tcp and settings-driven `allow=`; rtp.conf gains stunaddr/icesupport. TLS port stored only (D71). |
| 18 | Call reports | F5 | |
| 19 | Helper: Unix socket, peer credential check, first commands (firewall) | | |
| 20 | fail2ban setup, then own AMI-security-event blocker via Helper | | |
| 21 | Installer script for fresh Debian (users, permissions, hardened systemd units, polkit rule, Asterisk build) | | |

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
- **MWI** (the message-waiting light on the phone) is not configured: it needs `mailboxes =` on
  the PJSIP endpoint and a decision about subscriptions.
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
- Restarting Asterisk is still a manual step. A button for it needs the polkit rule and a
  decision about dropping live calls.

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
- **No strip or prepend digits** (D44), so "dial 9 for an outside line" is not possible. Ask if it
  is wanted; it is one field and one line of renderer.
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
  up in the dialplan. There is no warning anywhere else that it needs fixing.

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
