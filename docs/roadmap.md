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
| 8 | Destinations: shared "send call to X" model + dialplan helper | supporting | **In progress** 2026-09-17: model, catalog, dialplan helper and shared picker done (D35, D36). No schema: destinations are derived, not stored. Nothing consumes the picker until piece 9. |
| 9 | Generic SIP trunk | F1 | |
| 10 | Outbound routes (international restricted by default) | supporting | |
| 11 | Inbound routes (DID → destination) | supporting | |
| 12 | Callcentric wizard (verify settings on lab VM first) | F1 | |
| 13 | Voicemail | F2a | Done 2026-09-16, except email (piece 14). Lab-verified: UI enable -> voicemail.conf + dialplan -> real unanswered call -> message recorded in INBOX. |
| 14 | Email notifications (voicemail to email first, then alerts) | F4 | |
| 15 | Ring groups: ring all + hunt | F3 | |
| 16 | Follow me | F2b | |
| 17 | IVRs (with audio file handling) | F6 | |
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
  create, edit and delete in sweetalert2 modals, "show password", "regenerate password" and
  "apply config". The page is a shell; htmx fetches every part of it from page handlers as HTML
  partials, and changes answer 204 with `HX-Trigger` events (D21).
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
