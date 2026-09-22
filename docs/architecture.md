# Architecture

## Goal

Cover the small set of PBX features most sites actually use (extensions, trunks, inbound and
outbound routes, voicemail, basic call handling) with as little code, privilege and exposed
surface as possible. Install onto a fresh Debian server with one script.

## Components

```
                 ┌──────────────────────────────────────────────┐
 Browser ──443──▶│ Techie.Pbx.Web      (user: tnpbx, no root)   │
  (Entra ID)     │  Razor Pages + API, SQLite DB, renders confs │
                 └───────┬─────────────────────┬────────────────┘
                         │ AMI 127.0.0.1:5038  │ Unix socket /run/tnpbx/helper.sock
                         ▼                     │ (0660, group tnpbx, peer UID checked)
                 ┌───────────────┐     ┌───────▼────────────────────────────┐
 Phones/trunks ─▶│ Asterisk      │     │ Techie.Pbx.Helper   (root)         │
  SIP 5060/UDP   │ (user:        │     │  no network, typed command         │
  RTP 10000-20000│  asterisk)    │     │  allowlist only (firewall, updates)│
                 └───────────────┘     └────────────────────────────────────┘
```

| Process | Runs as | Responsibility |
|---|---|---|
| Web | `tnpbx` (unprivileged) | UI, API, database, rendering and writing Asterisk config, AMI control |
| Asterisk | `asterisk` | The actual PBX: SIP, media, dialplan |
| Helper | `root` | The few operations that need root. Not built yet. |

### What needs root and what doesn't

Most PBX management doesn't need root if file ownership is set up correctly:

| Operation | How | Root? |
|---|---|---|
| Write Asterisk config | Web is in the `asterisk` group; `/etc/asterisk` is setgid 2770 (D18) | No |
| Reload config, live status | AMI on localhost | No |
| Restart `asterisk.service` | polkit rule scoped to that one unit | No |
| Bind 443 | `AmbientCapabilities=CAP_NET_BIND_SERVICE` | No |
| TLS certificates (ACME) | In-process, into a directory the web user owns | No |
| Firewall (nftables), OS updates | Helper | Yes |
| Initial install | Installer script | Yes |

Sudo rules were rejected: see [decisions.md](decisions.md).

## Projects

| Project | Contents | Depends on |
|---|---|---|
| `Techie.Pbx.Web` | Razor Pages + htmx partials, API controllers, auth, `PbxDatabase`, startup | Core, Asterisk, Contracts |
| `Techie.Pbx.Core` | Models + validation, `Database` (SQLite + schema scripts), repositories, `SettingsKeys`, `SecretGenerator`, shell scripts | Dapper, Microsoft.Data.Sqlite, log4net |
| `Techie.Pbx.Asterisk` | Conf renderers, `ConfFileWriter`, `ConfigApplier`, AMI client | Core |
| `Techie.Pbx.Contracts` | Messages between Web and Helper | none |
| `Techie.Pbx.Helper` | Root helper (stub) | Core, Contracts |
| `Techie.Pbx.Tests` | xUnit tests, expected conf files | Core, Asterisk |

## Config flow

The database is the source of truth. Asterisk config files are an output, like a build artifact.

```
 UI / API edit
      │
      ▼
 Repository ── validates model ──▶ SQLite (Extensions, ...)
      │
      ▼  "apply config" (button on the extensions page)
 Load all rows
      │
      ▼
 Renderers (static, pure)          PjsipConfRenderer, ExtensionsConfRenderer
  - re-validate every row
  - ConfText.Safe on every value
      │
      ▼
 ConfFileWriter.WriteAtomic        temp file → fsync → rename; skips unchanged files
      │
      ▼
 AMI: pjsip reload / dialplan reload (only for files that changed)
```

Generated files (current):

| File | Renderer | Contains |
|---|---|---|
| `pjsip.conf` | `PjsipConfRenderer` | UDP transport (with NAT settings when needed), one endpoint + auth + aor per enabled extension |
| `extensions.conf` | `ExtensionsConfRenderer` | `[internal]` context: `*43` echo test, `*97` voicemail (when any mailbox exists), one explicit `Dial` entry per enabled extension, falling back to the mailbox when there is one (D29) |
| `voicemail.conf` | `VoicemailConfRenderer` | `[general]` recording settings and one mailbox line per enabled extension with voicemail switched on, in context `default` |
| `tnpbx-voicemail-options.json` | `VoicemailOptionsRenderer` | Not an Asterisk file: mailbox → `{Transcribe}` for the `mailcmd` script, which app_voicemail has no way to tell (D128) |

| `asterisk.conf` | `AsteriskConfRenderer` | `[options]`: verbose, and `live_dangerously`/`execincludes` off. Restart, not reload (D33) |
| `modules.conf` | `ModulesConfRenderer` | `autoload = no` and an explicit `load =` allowlist (D31). Restart, not reload |
| `rtp.conf` | `RtpConfRenderer` | The media port range, 10000-20000. Restart, not reload |
| `logger.conf` | `LoggerConfRenderer` | Console, `messages.log`, and `security.log` for the events fail2ban and our own blocker read |
| `manager.conf` | `ManagerConfRenderer` | AMI on 127.0.0.1 only, and the one account, from the same settings the app connects with (D32) |

Nothing in `/etc/asterisk` is hand written any more. Three of these files are only read when
Asterisk starts, so an apply writes them and reports that a restart is owed rather than claiming
they are live (D33).

### Dialplan approach

Each extension gets an explicit `exten =>` line rather than a pattern like `_1XXX`, so only
numbers that exist in the database can be dialled. Feature codes use FreePBX-style numbers
(`*43` echo test) since that's what users already know.

### Destinations

Most features end the same way: "and then the call goes *there*". Inbound routes, IVR keys, ring
group failover and follow-me all need the same list and the same dialplan, so there is one of
each (D35, D36).

```
 Extensions table ─┐
 (ring groups)  ───┼─▶ DestinationCatalog.All ─▶ choices ─▶ _DestinationSelect.cshtml
 (IVRs)         ───┘                                            │  posts Destination.Key
                                                                ▼
 feature table: DestinationType + DestinationValue ─▶ Destination ─▶ DestinationDialplan ─▶ conf
```

A destination is a **reference** (type + value), never a copy, and the list is computed rather
than stored, so there is nothing to keep in step when an extension is renamed or deleted. What a
feature stores is those two fields in two columns of its own table; what it writes into the
dialplan comes from `DestinationDialplan`, which is the only code that knows the syntax. An
extension destination is `Goto(internal,<number>,1)` — in by the same door an internal call uses,
so the extension's own voicemail fallback applies without being written twice.

### NAT

Cloud VMs only see a private IP; the public IP is 1:1 NAT in front of them. Without telling
PJSIP, calls connect with no audio. `PjsipTransport` holds `LocalNets` (the private subnets)
and `ExternalAddress` (the public IP). When `ExternalAddress` is set, the renderer adds
`local_net`, `external_media_address` and `external_signaling_address` to the transport.

## Web UI

Server rendered. Four vendored client libraries and nothing else: Bootstrap, bootstrap-table,
sweetalert2 and htmx (D9, D21). No CDN, no npm build step, and the JavaScript we write is glue:
`site.js` (the JSON calls, toasts, `hx-confirm` asked with sweetalert2, keeping the form modal
tidy, starting bootstrap-table on tables htmx brought in) and one small file per page.

**Forms live in Bootstrap modals; sweetalert2 does alerts, confirms and toasts** (D42). A page has
one empty `_FormModal` shell; the button that opens it carries `hx-get` for the form partial and
Bootstrap's `data-bs-toggle`, so neither opening nor closing needs JavaScript of ours.

The extensions page is the pattern every later list should follow:

```
 /Extensions                      page shell: buttons, empty containers, nothing else
   ├─ hx-get ?handler=Table  ───▶ _Table       bootstrap-table, one _StatusBadge per row
   ├─ hx-get ?handler=Status ───▶ _Status      every 5s: hx-swap-oob badges, one request
   ├─ hx-get ?handler=Form   ───▶ _Form        into #form-modal-content, a Bootstrap modal
   ├─ ?handler=Save/Delete   ───▶ 204 + HX-Trigger: extensionsChanged, pbxToast
   └─ fetch /api/…           ───▶ JSON         apply config, show/regenerate a SIP password
```

An action that changes data does not decide what to redraw: it names what happened, and the
page's containers listen. The table and the apply reminder re-fetch on `extensionsChanged`
(`trunksChanged` on the trunks page), and `pbxToast` closes the form modal and says what happened.

The status page (`/`, `Pages/Status`) is the same shape on two clocks: `?handler=Live` polls
Asterisk every five seconds over one AMI session for the health tiles and the calls in progress,
and `?handler=Attention` reads the database on load and on `configChanged` / `configApplied` for
the findings (`AttentionRules`, a pure function in the Asterisk project) and the counts.
`/Status/Logs` tails one of four named logs — `messages.log`, `security.log`, the application log
and the W3C web request log — chosen by name and never by path (D106, D107, D116).

The database itself is opened once at startup by `PbxDatabase` and read from `Database:Path`
(default `/var/lib/tnpbx/tnpbx.db`); pages and controllers construct their repositories over it
with `new` rather than taking them from the container (D22).

## Authentication

- Microsoft Entra ID (Azure AD) via `Microsoft.Identity.Web`, OpenID Connect with a cookie session.
- API controllers use the same cookie. There is no bearer-token API because only our own pages
  call it. Revisit if external integrations are added.
- The admin UI is intended to be reachable publicly over HTTPS. Further restriction is the
  customer's choice.
- Known gaps, deferred on purpose: see [security.md](security.md#known-gaps).

## Logging

log4net everywhere. The Web project bridges ASP.NET Core's logging into log4net
(`Microsoft.Extensions.Logging.Log4Net.AspNetCore`) and has console + rolling file appenders.
The Helper logs to the console only, which systemd sends to the journal.

One exception, and it is a different kind of log: **the web request log** (D116). Kestrel writes it
directly, in W3C format, through `Microsoft.AspNetCore.HttpLogging`'s `W3CLogger` — one line per
request rather than one line per thing our code decided to say, which is what makes it useful for
phone provisioning, where the interesting requests never reach our code at all. It is first in the
pipeline, so it wraps authentication and records anonymous provisioning fetches too.

| | Where | Written by | Switched by |
|---|---|---|---|
| Application log | `logs/tnpbx-web.log` | log4net (`log4net.config`) | always on |
| Web request log | `logs/requests/tnpbx-requests-*.txt` | Kestrel's `W3CLogger` | `Web.RequestLog`, read at startup |

Both live inside the install (`/opt/tnpbx`), which is the only place the hardened unit grants write
access to — never `/var/log`, which `ProtectSystem=strict` makes read-only to this process. Both are
therefore cleared by a re-deploy, which keeps `appsettings.json`, `Data/`, `bin/`, `Config/` and
`whisper/` (D95, D126, D128).

## Mail out of the box

Two senders, one credential set, no MTA (D115, D126).

| | Sends | Runs as | Reads the relay details from |
|---|---|---|---|
| Alerts and the test button | this app, over Graph or SMTP | `tnpbx` (the web process) | the `Mail.*` settings in the database |
| Voicemail | `app_voicemail`, composed there and handed to `mailcmd` | `asterisk`, via `/opt/tnpbx/bin/voicemail-mail` | `/opt/tnpbx/Config/mail.json` |

The script is the whole interface between the two: a process running as `asterisk` has no business
opening this app's database, so the app writes the SMTP half of those settings to `mail.json`
(0640, group `asterisk`) whenever a setting is saved and at every start. Nothing in `/etc/asterisk`
carries a mail credential — the generated `voicemail.conf` names the script's fixed path and
nothing else, which is why a mail setting is not an apply.

That script also transcribes the recording on its way past, when the mailbox asked for one (D128):
whisper.cpp and its model on this machine, so a voicemail is never sent anywhere to be read. Which
mailboxes asked is the one thing it cannot learn from the email, because `app_voicemail` has no
transcription option and runs `mailcmd` with no arguments, so the apply renders
`/etc/asterisk/tnpbx-voicemail-options.json` — mailbox → `{Transcribe}`, no PIN and no address —
alongside `voicemail.conf`. Both the engine and the model are optional parts of the install, and
every way transcription can fail still relays the message.
