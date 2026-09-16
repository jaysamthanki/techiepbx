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

### NAT

Cloud VMs only see a private IP; the public IP is 1:1 NAT in front of them. Without telling
PJSIP, calls connect with no audio. `PjsipTransport` holds `LocalNets` (the private subnets)
and `ExternalAddress` (the public IP). When `ExternalAddress` is set, the renderer adds
`local_net`, `external_media_address` and `external_signaling_address` to the transport.

## Web UI

Server rendered. Four vendored client libraries and nothing else: Bootstrap, bootstrap-table,
sweetalert2 and htmx (D9, D21). No CDN, no npm build step, and the JavaScript we write is glue:
`site.js` (toasts, `hx-confirm` asked with sweetalert2, starting bootstrap-table on tables htmx
brought in) and one small file per page.

The extensions page is the pattern every later list should follow:

```
 /Extensions                      page shell: buttons, empty containers, nothing else
   ├─ hx-get ?handler=Table  ───▶ _Table       bootstrap-table, one _StatusBadge per row
   ├─ hx-get ?handler=Status ───▶ _Status      every 5s: hx-swap-oob badges, one request
   ├─ ?handler=Form          ───▶ _Form        shown inside a sweetalert2 modal
   ├─ ?handler=Save/Delete   ───▶ 204 + HX-Trigger: extensionsChanged, configChanged, pbxToast
   └─ fetch /api/…           ───▶ JSON         apply config, show/regenerate a SIP password
```

An action that changes data does not decide what to redraw: it names what happened, and the
page's containers listen. The table re-fetches on `extensionsChanged`, the apply reminder
appears on `configChanged`, and `pbxToast` closes the modal and says what happened.

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
