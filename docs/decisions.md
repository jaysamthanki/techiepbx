# Decisions

Newest at the bottom. Don't edit old entries; if a decision changes, add a new entry that
supersedes it and say which one.

---

### D1. .NET for the management app (2026-09-13)
Cross-platform, secure defaults, can publish as a self-contained single file so the target
server doesn't need a .NET install.

### D2. Asterisk as the PBX engine (2026-09-13)
Mature, widely understood, and what FreePBX users already know. Use the current **LTS** major
version (22 at time of writing) rather than the newest standard release, because standard
releases have short support windows. Built from source by our scripts because Debian's
packaging of Asterisk is unreliable. PJSIP only.

### D3. Privilege separation: unprivileged web + tiny root helper (2026-09-13)
The web process runs as its own user. The few things that need root go to a separate Helper
process over a Unix socket, which only accepts a fixed set of typed commands.

Rejected:
- **Everything as root:** any web bug becomes full server compromise.
- **Unprivileged web + sudo rules:** sudo rules are easy to get wrong (wildcards, commands that
  take paths), sudo can't be used with systemd's `NoNewPrivileges=true`, and it pushes the web
  code towards building shell commands.

Most operations turn out not to need root at all (see architecture.md).

### D4. Database is the source of truth; conf files are generated (2026-09-13)
Same model as FreePBX, but the app owns the Asterisk config completely. No `_custom.conf`
override files. Hand edits are unsupported and will be overwritten.

### D5. Razor Pages with cookie-authenticated API (2026-09-13)
Server-rendered pages keep the client side small. API controllers share the Entra ID cookie.
JWT bearer auth was removed because nothing outside our pages calls the API. Revisit if
integrations are added.

### D6. Entra ID (Azure AD) authentication (2026-09-13)
Admin UI is publicly reachable over HTTPS with Entra ID sign-in. A break-glass login for when
Entra is unreachable was discussed and **deferred**.

### D7. fail2ban first, own blocker later (2026-09-13)
Use fail2ban initially. Later, replace it with an integrated blocker that reads Asterisk
security events from AMI and asks the Helper to add addresses to an nftables set.

### D8. log4net for logging, minimal dependency injection (2026-09-13)
User preference. Classes get a static log4net logger. Objects are constructed directly;
DI is used only where ASP.NET Core requires it.

### D9. Bootstrap-native UI, tables and modals (2026-09-13)
No extra UI frameworks. Lists are tables, editing happens in modals.

### D10. Dapper + SQLite for data access (2026-09-13)
Plain SQL with Dapper over `Microsoft.Data.Sqlite`. Rejected EF Core (heavier, more implicit
behaviour, DI-oriented) and raw ADO.NET (too much mapping boilerplate). Schema changes are
numbered SQL scripts tracked with `PRAGMA user_version`.

### D11. Primary keys named `<Entity>ID` (2026-09-13)
`ExtensionID`, not `Id`. Makes it obvious which ID you're looking at in queries, joins and
code. Foreign keys use the same name. Tables and columns are PascalCase.

### D12. Explicit dialplan entries, FreePBX-style feature codes (2026-09-13)
One `exten =>` per extension instead of patterns, so only real extensions are dialable.
Echo test is `*43`.

### D13. Solution layout (2026-09-13)
`src/` with Web, Core, Asterisk, Contracts, Helper; `tests/` with one xUnit project. Shared
build settings in `Directory.Build.props`.

### D14. The AMI secret lives in the database (2026-09-15)
`Settings.Ami.Secret`, added by schema script `002_settings.sql`. The database file is already
0600 and already holds every SIP secret, so it is the one place that has to be protected.

Rejected:
- **`appsettings.json`:** a second secret store to protect and back up, and easy to commit by
  accident.
- **A file only the Helper can read:** the web process is the one that talks to AMI, so the
  Helper would only be forwarding the secret back to it.

The secret is never logged (`SettingsKeys.IsSecret`) and only ever rendered into `manager.conf`
(piece 7). Nothing else in the app reads it.

### D15. Non-secret settings in a `Settings` key/value table (2026-09-15)
One table, `SettingID` / `Key` / `Value`, rather than typed columns per setting: conf directory,
AMI host/port/user/timeout and the SIP transport (bind address, port, local nets, external
address) all live in it. Adding a setting then needs no schema script.

The cost of a key/value table is that it can become a junk drawer with no schema to check it
against, so the key names are constants in a static `SettingsKeys` class and `SettingsRepository`
refuses to write a key it doesn't know. `AsteriskSettings` turns the rows into `AmiSettings` and
`PjsipTransport`; a missing or blank value falls back to the default on those objects, which
stay ignorant of where the values are stored.

### D16. Reload via typed `Action: Reload`, per changed file (2026-09-15)
Apply renders every file, writes only the ones whose content changed, then reloads only the
modules those files belong to: `res_pjsip` for `pjsip.conf`, `pbx_config` for `extensions.conf`.
When nothing changed, no AMI connection is opened at all.

A typed action rather than the CLI equivalents (`pjsip reload`, `dialplan reload`) through
`Action: Command`, so the AMI user does not need `command` permission. See D17.

### D17. No `Action: Command` in the AMI client (2026-09-15)
Dropped as unjustified surface. It is a general "run any CLI command" channel, and granting the
AMI user `command` permission would make a web bug that reaches AMI far more valuable to an
attacker.

Everything the app needs has a dedicated action: `Reload` for applying config, and
`PJSIPShowContacts` for live registration status. `Response: Follows`, which only the Command
action produces, is no longer treated as success. If something in future genuinely has no typed
action, add it here as a new decision rather than reintroducing Command quietly.

### D18. Generated conf files: setgid directory, 0640 files, group `asterisk` (2026-09-15)
Found in live testing: the web user wrote `pjsip.conf` and `extensions.conf` as
`-rw-r----- techie:techie`, so the asterisk process could not read them. `pbx_config` declined
to load the dialplan and said nothing about why, which made it look like a renderer bug.

The model, in two halves:

- **Deploy side.** `/etc/asterisk` is `root:asterisk` mode **2770** (was 0750). The group write
  bit is what lets the web user create files there at all; the **setgid** bit is what makes
  every file it creates group-owned by `asterisk` rather than by the web user's own group. The
  web user is a member of the `asterisk` group and of nothing else that matters.
- **App side.** `ConfFileWriter.WriteAtomic` sets the mode explicitly to **0640** after the
  rename, rather than leaving it to the umask. Owner read/write for the web user, group read for
  asterisk, nothing for anyone else: these files hold every SIP secret.

Group *write* on the files is deliberately not granted. The web user replaces a file by renaming
over it, which needs write on the directory and not on the file, so nothing needs it.

Rejected: **making the web user's primary group `asterisk`** (it would then own unrelated files
it creates elsewhere), and **having the Helper write the config as root** (writing files is
exactly the kind of work that does not need root, per D3).

### D19. PJSIPShowContacts emits `ContactList`, and errors when empty (2026-09-15)
Two facts about Asterisk 22.11 that the AMI documentation does not make obvious, both confirmed
on the wire:

- The list items are **`ContactList`** events, terminated by `ContactListComplete` with
  `EventList: Complete`. Not `ContactStatusDetail`, which is what `ContactStatus`-style events
  are called elsewhere; we filtered for that name and so always saw zero contacts.
- `ContactList` has **no `Aor` header**. It carries `ObjectName` (`1001;@<hash>`), `Endpoint`
  (`1001`), `Uri`, `UserAgent`, `Status`, `ViaAddr`/`ViaPort` and the qualify fields.
  `PjsipContact.Aor` is read from **`Endpoint`**, because our generated config names the endpoint,
  the AOR and the extension identically. If that ever stops being true, this has to be revisited.
- With nothing registered, the action itself is answered **`Response: Error` /
  `Message: No Contacts found`**, not an empty list. That is an empty result, not a failure, so
  `SendEventList` takes an `emptyListMessage` that turns exactly that one message into an empty
  list. Any other error still throws.

### D20. Unauthenticated API and htmx calls get 401, not a redirect to Entra (2026-09-15)
Closes the known gap recorded in [security.md](security.md#known-gaps), now that the first
API-backed page exists. The default behaviour challenges OpenID Connect, so a `fetch` or htmx
request with an expired session gets a 302 to `login.microsoftonline.com`, which the browser
cannot follow cross-origin: the page sees a CORS failure instead of "your session ended".

`OnRedirectToIdentityProvider` in `Program.cs` answers **401** instead when the path starts with
`/api` or the request carries an `HX-Request` header. htmx requests also get `HX-Refresh: true`,
which makes the browser reload as a full navigation and sign in normally; our own `fetch` calls
reload the page themselves on a 401. Ordinary page requests are untouched and still redirect.

### D21. The extensions UI: Razor Pages partials for htmx, a JSON API only where it earns it (2026-09-15)
Page handlers on `/Extensions` return **HTML partials** (`_Table`, `_Form`, `_Status`,
`_StatusBadge`); htmx fetches them. Anything that changes data answers **204 with an `HX-Trigger`
header** naming events (`extensionsChanged`, `configChanged`, `pbxToast`) that the page reacts
to, rather than each action deciding what to re-render. Lists are bootstrap-table, create/edit
happen in sweetalert2 modals around the form partial (D9).

Live registration status is **one poll every 5 seconds for the whole page**, answered with a set
of `hx-swap-oob` badges, one per row. Rejected: one poll per row (N requests every 5s) and
re-fetching the whole table (loses sort, search and focus every 5 seconds). The cost is that
bootstrap-table re-renders its rows from the HTML it captured, so a sort or search shows the
badge as it was when the table was fetched until the next poll, at most 5 seconds later.

API controllers are used only where the answer is data rather than a piece of the page:
`POST /api/config/apply`, and showing and regenerating an extension's SIP password. Secrets are
never rendered into the table; they are fetched one at a time, and who asked is logged.

### D22. The web app's database is a static holder, path from configuration (2026-09-15)
`PbxDatabase.Open` runs at startup and `PbxDatabase.Current` is what pages and controllers build
their repositories over with `new` (D8). A `Database` is only a connection string, so a
repository per request costs nothing, and nothing has to be registered in the container.

The file is `Database:Path` in configuration, defaulting to **`/var/lib/tnpbx/tnpbx.db`** for a
real install; `appsettings.Development.json` points at `tnpbx.db` beside the project, which is
git-ignored. The directory has to be writable by the web user: the installer (piece 21) creates
it, and until then the database is created on first run.

### D23. Antiforgery tokens on every browser call, including the API (2026-09-15)
The API shares the session cookie (D5), so it needs the same CSRF protection as a form post.
The token goes in the `RequestVerificationToken` **header**: the layout puts it on `<body>` as
`hx-headers`, so every htmx request carries it, and in a meta tag for our own `fetch` calls.
Controllers get `AutoValidateAntiforgeryToken` globally rather than per action, so a new endpoint
is protected by default instead of when someone remembers.

### D24. Local authentication bypass for the lab (2026-09-15)
`LocalAuthenticationBypass` in appsettings: `Enabled` (default false) plus `AllowedNetworks`,
a CIDR whitelist (`Ipv4Networks` matcher, single IPs as /32). When enabled and the remote IP
matches, `LocalBypassMiddleware` authenticates the request as identity `local-bypass` before
the Entra challenge; non-matching IPs get the normal Entra flow. It covers pages, htmx
partials and API controllers alike. A startup WARNING is logged when it is on, and each
bypass-authenticated request is logged with the remote IP. Failsafe only: enabled on the lab
VM, never in production.

### D25. The database and state files live in a Data folder inside the app (2026-09-15)
Amends D22: the default path is `<content root>/Data/tnpbx.db` (still overridable via
`Database:Path`), and the app creates the folder at startup. Backing up the app directory
backs up everything.

### D26. "Config pending" is a marker file in Data (2026-09-15)
Every repository write that changes rendered config touches `Data/config-pending`
(`ConfigPendingMarker`); a successful `Apply()` clears it. The UI reads the file rather than
browser-local state, so the "changes not applied" indicator is true even across browsers and
restarts. File over a DB row: it is throwaway state, and it disappears with a restore of the
app folder instead of surviving inside a database backup.

### D30. `*97` skips the voicemail PIN when called from the extension's own phone (2026-09-16)
FreePBX convention, using Asterisk's `VoiceMailMain(ext@context,s)` option: calling `*97`
goes straight into the caller's own mailbox without the PIN prompt. Safe because the mailbox
is chosen from `CALLERID(num)` — the authenticated SIP endpoint — not from anything the
caller can dial, so you can only skip into your own box.

### D27. Voicemail is columns on Extensions, not a table of its own (2026-09-16)
A mailbox belongs to exactly one extension, is optional, and is five fields: enabled, PIN,
email, attach, delete-after-email. Schema script `003_voicemail.sql` adds them to `Extensions`
with `Voicemail` prefixes, so one query still loads everything the renderers need, `Extension`
stays the one model, `ExtensionRepository` stays the one repository, and there is no join, no
second write path and no orphan row to worry about.

Rejected: a **`Voicemails` table** keyed by `ExtensionID`. It would be the right shape for
mailboxes that are not attached to an extension — a "general delivery" box an IVR drops callers
into — and that is exactly what would make us revisit this: if standalone mailboxes are ever
wanted, move the columns into their own table then, with a schema script that copies the rows.
Until then it would be a join and a second repository bought with nothing.

### D28. The voicemail PIN is stored and written as typed (2026-09-16)
`Extensions.VoicemailPin` holds the digits, and `voicemail.conf` gets them in the mailbox line:
`1001 => 4321,Front Desk,...`. That is how app_voicemail works — it reads the password from the
file (and rewrites it there when a user changes it by phone), so there is nothing to hash
against.

This is the same exposure the SIP secrets already have (security.md: "SIP secrets stored in
plain text in DB and `pjsip.conf`"), and it is defended the same way: the database is 0600 and
the conf files are 0640 to group `asterisk` (D18). Two differences worth naming: a voicemail PIN
buys an attacker someone's messages rather than a phone line to sell calls on, so it is the
lesser secret of the two; and unlike the SIP secret it **is** shown in the edit modal, because
an admin setting up a phone has to be able to read it out. PINs are 4 to 8 digits, a new
extension is offered a random 6-digit one, and trivial PINs are not refused.

### D29. Voicemail fallback: explicit GotoIf on DIALSTATUS, busy versus unavailable (2026-09-16)
An extension with a mailbox gets:

```
exten => 1001,1,Dial(PJSIP/1001,30)
 same => n,GotoIf($["${DIALSTATUS}" = "BUSY"]?busy:unavailable)
 same => n(busy),VoiceMail(1001@default,b)
 same => n,Hangup()
 same => n(unavailable),VoiceMail(1001@default,u)
 same => n,Hangup()
```

so the caller hears the busy greeting when the phone is busy and the unavailable greeting for
everything else (no answer, unregistered, congestion). Without a mailbox the line stays
`Hangup()`, as before. `*97` is `VoiceMailMain(${CALLERID(num)}@default)` — your own mailbox,
FreePBX's number — and is only written when at least one mailbox exists.

Rejected: the one-liner
`VoiceMail(1001@default,${IF($["${DIALSTATUS}"="BUSY"]?b:u)})`. It is the same behaviour in less
space, but the generated dialplan is something an admin reads while a phone system is down, and
two labels are easier to follow than a nested expression.

Mailboxes live in voicemail context **`default`**, Asterisk's own, named explicitly in every
`VoiceMail()` call; one context is enough for a single-tenant PBX.
`app_voicemail` autoloads and nothing in the generated `modules.conf` allowlist work (piece 7)
may `noload` it.

### D31. modules.conf is an allowlist: `autoload = no` and nothing else (2026-09-16)
Closes the known gap in [security.md](security.md#known-gaps). A stock Asterisk loads well over a
hundred modules; we call perhaps a quarter of them. The rest are channel drivers for protocols we
do not speak, applications we never dial and subsystems we never configure — all of it code that
a packet can reach. `ModulesConfRenderer` writes `autoload = no` and one `load =` line per module,
grouped by why it is there, because a list nobody can read is a list nobody will keep honest.

The list as of this piece (32 modules): `res_pjproject`, `res_rtp_asterisk`,
`res_timing_timerfd`, `res_security_log`; `res_sorcery_config/memory/astdb`; `res_pjsip`,
`res_pjsip_session`, `chan_pjsip`, `res_pjsip_authenticator_digest`,
`res_pjsip_endpoint_identifier_user`, `res_pjsip_registrar`, `res_pjsip_sdp_rtp`,
`res_pjsip_caller_id`, `res_pjsip_nat`, `res_pjsip_dtmf_info`; `bridge_simple`,
`bridge_native_rtp`; `pbx_config`, `app_dial`, `app_playback`, `app_echo`, `app_voicemail`,
`func_callerid`; `codec_alaw`, `codec_ulaw`, `codec_gsm`, `format_gsm`, `format_pcm`,
`format_wav`, `format_wav_gsm`.

Deliberately absent, each waiting for the piece that needs it: `res_pjsip_outbound_registration`,
`res_pjsip_outbound_authenticator_digest` and `res_pjsip_endpoint_identifier_ip` (trunks, piece
9); `res_pjsip_pubsub` and `res_pjsip_mwi` (message waiting); `app_stack` and `func_logic`
(destinations, piece 8); `res_musiconhold`; `cdr_*` (reports, piece 18); `res_pjsip_refer`
(transfers, not a feature yet). Permanently absent and worth naming:
`res_pjsip_endpoint_identifier_anonymous`, which is how a PBX ends up taking calls from
strangers. Adding a feature means adding its modules in the same change. This supersedes the last
paragraph of D29: `app_voicemail` is not autoloaded any more, it is on the list.

**The exact list is an open question until the lab VM confirms it.** A missing module shows up as
a feature that quietly does not work, so it is to be verified by restarting the VM on this file
and watching `core show modules`, `pjsip show endpoints` and a real call with voicemail.

**Added 2026-09-17 (trunks, piece 9)**, moving four modules from "waiting for the piece that needs
it" to the list, because that piece is now here:

- `res_pjsip_outbound_registration.so` — registering with a provider at all.
- `res_pjsip_outbound_authenticator_digest.so` — answering the provider's auth challenge. Without
  it a registration is sent, challenged, and never completed.
- `res_pjsip_endpoint_identifier_ip.so` — what makes a trunk's `identify` section work, i.e. what
  lets Asterisk recognise an inbound call from the provider's addresses (D39).
- (`res_pjsip_registrar.so` and the rest were already there for phones.)

The list is now 36 modules. Still absent and still waiting: `res_pjsip_refer`, `res_musiconhold`,
`cdr_*`, `app_stack`, `res_pjsip_pubsub`/`res_pjsip_mwi`.

### D32. AMI binds to loopback, and manager.conf refuses to say otherwise (2026-09-16)
`manager.conf` is generated from the same `AmiSettings` the app connects with, so the secret in
the database and the secret in the file are one value with one source. `[general]` is always
`bindaddr = 127.0.0.1`, `webenabled = no`, and the account is always `deny = 0.0.0.0/0.0.0.0`
with `permit = 127.0.0.1/255.255.255.255`. None of that is configurable: AMI is a "do anything to
this PBX" socket, and the app that uses it runs on the same box.

Because the binding is fixed, a setting that points `Ami.Host` anywhere else is a mistake, and
`ManagerConfRenderer` throws rather than writing a file that disagrees with the settings — a
misconfiguration should fail where it is made, not as a broken apply at three in the morning.

The account gets `read = system` and `write = system,config`: between them, what `Reload` and
`PJSIPShowContacts` need and nothing more. No `command` (D17), no `originate`. Security events
(`read = security`) come with the blocker in piece 20, CDR with reports in piece 18.

### D33. asterisk.conf, modules.conf and rtp.conf are written but never reloaded (2026-09-16)
Asterisk reads `asterisk.conf` once, at startup, and module loading is decided once as well.
Pretending otherwise would mean an apply that says "done" while Asterisk runs the old file.

So `GeneratedFile.Module` is nullable: null means no reload can apply this file. `Apply()` writes
those files like any other, reloads nothing for them, and reports them in
`ApplyResult.RestartRequiredFiles` with `RestartRequired` set, which the UI repeats to the admin.
An apply where only those files changed opens no AMI connection at all and still succeeds.

`rtp.conf` is in this group as the conservative choice. `res_rtp_asterisk` most likely does
re-read it on a module reload, but the RTP range is static in the renderer and effectively never
changes after the first write, so a reload that might fail and break an apply buys nothing.
Worth confirming on the lab VM; if it reloads cleanly, moving `rtp.conf` to `res_rtp_asterisk` is
a one-line change.

The restart itself stays manual for now. Restarting Asterisk drops live calls, so it is not
something an apply should do on its own; the polkit rule for `asterisk.service` (architecture.md)
means it can be offered as its own button later.

### D34. Reloading manager.conf may hang up on us, and that is not a failure (2026-09-16)
Reloading `manager.conf` makes Asterisk rebuild its AMI sessions, and the session giving the
reload order is one of them: it can answer and then close, or close without answering.

`manager` is therefore reloaded **last** — everything else has already happened by then — and
only its reload tolerates a dropped connection, which is logged at INFO as the expected outcome.
Every other module's reload still has to succeed, and a failure still leaves the config-pending
marker up (D26). `ConfigApplier.ReloadOrder` is public so this ordering can be tested without a
live Asterisk.

`logger` and `manager` are not `.so` files: the logger and AMI are part of the Asterisk core, and
both names are reload classes the `Reload` action accepts alongside real module names.

### D35. Destinations are derived, not stored: no Destinations table (2026-09-17)
A destination is "extension 1001" or "the mailbox on 1001" or "hang up" — a **reference**, and
everything it refers to already exists in a table of its own. So there is no `Destinations`
table, and **this piece adds no schema script**:

- **The list** an admin picks from is computed. `DestinationCatalog.All(extensions)` is a pure
  function that turns the rows a caller already loaded into choices: enabled extensions, the
  mailboxes that are actually switched on, and Hangup. Ring groups, IVRs and trunks each add
  their own source to it as they are built, and every picker in the UI gains the entries without
  being touched.
- **A stored choice** is two columns on the feature that made it —
  `InboundRoutes.DestinationType` / `DestinationValue`, `IvrKeys.DestinationType` /
  `DestinationValue` — written by the feature's own schema script when that feature is built.
  `Destination` (Type + Value) is what they hand around; `Destination.Key` ("Extension:1001") is
  the single-string form a `<select>` posts, and `TryParse` reads it back.

Why not rows in a table: a `Destinations` row per extension would be a copy of something that
already exists, and every rename, renumber, disable or delete would have to be mirrored into it.
That is the sync problem FreePBX-style schemas have, and there is nothing to buy with it here —
we never need to attach anything to a destination, only point at one.

The cost is that a stored choice can dangle: delete extension 1001 and an inbound route still
says `Extension:1001`. Foreign keys would have caught that; instead `DestinationCatalog.Find`
answers null for a destination that no longer resolves, the shared picker shows it as "no longer
available" rather than quietly selecting the first entry, and each consuming feature validates on
save. **This is the trade-off to revisit** if dangling references turn out to be common in
practice: the answer then is not a `Destinations` table but a "what points at this extension?"
check before delete.

Type is stored **by name**, never by ordinal, so reordering the enum cannot repoint live routes.
The enum lives in `Core.Models`, not `Contracts` — `Contracts` is only for messages between the
web app and the root helper, and a destination is not one.

### D36. One dialplan helper writes every "send the call here" (2026-09-17)
`DestinationDialplan.Steps` / `.Lines` is the only code that knows what sending a call to a
destination looks like: `Goto(internal,1001,1)` for an extension, `VoiceMail(1001@default,u)` +
`Hangup()` for a mailbox, `Hangup()` for hanging up. Inbound routes, IVR keys and ring group
failover will all call it rather than each writing its own `VoiceMail(...)`.

An extension destination goes in through `Goto(internal,<number>,1)` — the same dialplan entry an
internal call uses — so an inbound call gets the extension's voicemail fallback for free, and
there is one description of "what happens when you call 1001" rather than two that drift.

`ExtensionsConfRenderer` now writes its own busy/unavailable fallback through the helper, and
the golden `extensions.conf` is **unchanged**, which is the proof that the helper says what the
renderer used to say. What stayed in the renderer is the `GotoIf` on `DIALSTATUS`: choosing
*which* greeting a caller hears is the extension feature's decision, not part of what a
destination is. The helper takes that choice as a parameter (`VoicemailGreeting`).

### D37. A trunk name starts with a letter (2026-09-17)
A trunk's name becomes its PJSIP section names — `[callcentric]`, `[callcentric-auth]`,
`[callcentric-reg]`, `[callcentric-identify]` — in the same file where extensions already own
`[1001]`. A trunk called "1001" would therefore collide with extension 1001's endpoint and quietly
merge two objects.

`Trunk.Name` is `^[A-Za-z][A-Za-z0-9-]{0,31}$`: letters, digits and dashes, starting with a
letter. Extension numbers are all digits, so the two namespaces cannot meet. Dashes are allowed
because they read well in `from-trunk-<name>`; underscores and dots are not, to keep one shape.

### D38. One inbound context per trunk, hanging up until piece 11 (2026-09-17)
Every trunk endpoint has `context = from-trunk-<name>`, and the dialplan gets a context of that
name per enabled trunk. One per trunk rather than one shared `from-trunk`, so an inbound route can
say "calls arriving on *this* provider" without inspecting headers, and so a misconfigured trunk
cannot reach another provider's routing.

Right now that context contains one entry — `_X.` — which NoOps and hangs up, written through the
shared destination helper (D36). **Inbound routes (piece 11) replace the hangup with a
destination.** Ending the call is the right placeholder: the alternative, falling through to the
`internal` context, would let a stranger who reached the trunk dial our extensions.

### D39. Trunk sections: order, and registering versus a static contact (2026-09-17)
Trunks are appended to `pjsip.conf` **after** the extensions, in name order. A system with no
trunks therefore renders exactly the file it rendered before trunks existed, which the tests
assert: no pointless diff, no needless `res_pjsip` reload on upgrade.

Within a trunk the order is the one the Asterisk and provider documentation uses — registration,
auth, aor, endpoint, identify — so that this file and the provider's guide can be read side by
side. The registration object is `<name>-reg`, and the AMI status lookup reads the trunk name back
off that suffix (`PjsipConfRenderer.RegistrationSuffix`), so both ends share one constant.

The aor depends on whether we register:

- **Registering:** no `contact`, and `qualify_frequency = 60`. The registration is what tells the
  provider where we are, and qualify is what notices the provider going away.
- **Not registering:** `contact = sip:<host>[:port]` and no qualify. Nothing has told the provider
  anything, so the aor has to carry the address itself; qualifying a provider that never agreed to
  be pinged just makes noise in the log.

Other choices worth naming: the port is only written into a URI when it is not 5060; `callerid` is
only written when a caller ID number is set; `from_user` only when there is a username; an auth
section and `outbound_auth` only when there is a password, so an IP-authenticated trunk gets
neither. Codecs are limited to the three `modules.conf` loads (D31) — offering g722 or g729 would
generate config Asterisk has no module to honour.

### D40. Reading trunk registration status over AMI (2026-09-17)
`AmiSession.ShowRegistrations` sends **`PJSIPShowRegistrationsOutbound`**: our registrations with
providers, not the phones registering with us (`ShowContacts`).

Two deliberate pieces of caution, because this wire format has not been seen on the lab VM yet:

- **Every event in the list is mapped**, rather than filtering by event name as the contacts list
  does. `SendEventList` has already narrowed the list to events carrying our ActionID, so whatever
  Asterisk calls them, they answer this question — one less name to be wrong about.
- **Several "empty list" messages are tolerated.** D19 found that PJSIPShowContacts answers
  `Response: Error / No Contacts found` when nothing is registered; the outbound registration
  wording is unknown, so `SendEventList` now takes a list of tolerated messages and is given the
  plausible ones. Being wrong here would turn "no trunks registered" into a failed page.

`RegistrationState` gained **Rejected**, which only outbound registrations can be: the provider has
our credentials and refuses them. It is the one trunk state an admin has to act on, so it gets its
own red badge rather than being folded into "not registered".

### D41. A trunk password is the provider's, so there is no "regenerate" (2026-09-17)
Extensions have **show** and **regenerate**, because we choose an extension's SIP password. A
trunk's password is issued by the provider: generating a new random one locally would not change
anything at the provider, it would only stop the trunk authenticating at the next apply. So the
trunks page has **show** only.

Changing a trunk password — because the provider rotated it — is done in the edit form, where the
password box is always empty and blank means "keep the stored one". The password is never rendered
into the form or the table; reading it back is the explicit per-row action, and like the extension
secret it is logged as who asked, never what they saw.

### D42. Forms live in Bootstrap modals; sweetalert2 is for alerts, confirms and toasts (2026-09-18)
Supersedes the form half of D21, which put the create/edit forms in sweetalert2 modals. Every
create/edit form now opens in a Bootstrap modal instead. sweetalert2 keeps the three jobs it is
actually built for: alerts, confirms (`hx-confirm`, the delete and regenerate questions) and
toasts.

Why: a sweetalert2 popup is an *alert* that we were handing a form to. It meant our JavaScript
fetched the partial, handed the HTML to `Swal.fire`, then called `htmx.process` on the popup to
wake the form up, and closing it was `Swal.close()` from an event handler. Bootstrap is already
loaded, its modal is the thing forms are supposed to go in, and it does the opening itself.

The flow, which has no JavaScript of ours in the opening or the closing:

```
 button  hx-get=<form handler>  hx-target="#form-modal-content"
         data-bs-toggle="modal" data-bs-target="#form-modal"
            │ htmx fetches the partial      │ Bootstrap opens the modal
            ▼                               ▼
 _FormModal.cshtml  ── one empty shell per page, shared (Pages/Shared)
            │
            ▼
 _Form.cshtml  ── the whole modal-content: header, body, footer, all inside one <form>
            │ hx-post, hx-target="this", hx-swap="outerHTML"
            ▼
 invalid → the form again, errors on it, modal still open
 valid   → 204 + HX-Trigger: extensionsChanged/trunksChanged + pbxToast
                  └─ site.js hides the modal and toasts; the table and the apply
                     banner were already listening for those events
```

The event names did not change, so the apply reminder (D26) and the toasts kept working
untouched. The form partial owning its own header and footer is what lets a validation failure be
one swap of one element.

What is left of our JavaScript is smaller than before: `openModal` is gone from both page files,
and `send`/`failed`/`applyConfig` moved to `site.js` where both pages share one copy. The two
hooks that remain are a listener that hides the modal when a save succeeds, and one that puts the
placeholder back when the modal closes so the next open does not flash the last form.

Note that `.modal-dialog-scrollable` is deliberately not used: with the form between
`.modal-content` and the header/footer, that class's flex layout does not apply cleanly. A long
form scrolls the page, as it did before.

### D43. One apply button, in the navbar, and only when there is something to apply (2026-09-18)
"Asterisk is running config older than the database" is true of the whole system, not of the page
you happen to be looking at. So the per-page **Apply config** buttons and the per-page "changes
not applied" banners are gone, replaced by a single red button at the end of the navbar that
exists **only while the marker file does** (D26).

`/ConfigStatus` is a page with nothing but a handler: it answers with the button, or with nothing
at all. The navbar polls it every five seconds, and also on `configChanged` (which every save now
fires) and `configApplied`, so it appears the moment something changes and disappears the moment
an apply succeeds. A button that is only there when it has work to do needs no banner explaining
itself, and no page has to remember to draw one.

Polling from the layout means every open page asks every five seconds; the answer is a
`File.Exists`, which is why that is affordable.

### D44. Outbound routes: the fields, and the ones left out (2026-09-18)
`OutboundRoutes` is Name, DialPattern, TrunkID, Priority, Enabled. Name is letters/digits/dashes
starting with a letter, like a trunk (D37), because it names a dialplan context. TrunkID is a real
foreign key, so a trunk a route still points at cannot be deleted — `TrunkRepository.Delete` turns
that constraint into "delete the route first" rather than an exception.

**Left out, deliberately: strip and prepend digits.** features.md says "dial pattern → trunk", and
that is what this is. The consequence is worth naming: a site that wants "dial 9 for an outside
line" cannot have it, because the 9 would be sent to the provider as part of the number. If that
is wanted, it is one field (`StripDigits`, rendered as `${EXTEN:n}`) and one line of renderer —
ask for it rather than assume it.

Also left out: per-route caller ID (the trunk's is used), time-of-day conditions, and failover to
a second trunk. Each is a feature, not a field.

### D45. A number that matches no route does not go out (2026-09-18)
The last context included from `outbound` is `outbound-blocked`, whose only entry is `_X.`: NoOp,
play "not in service", hang up. It can never reach a `Dial`, and the tests assert that.

This is the fail-closed half of the toll-fraud guard in [security.md](security.md): the danger is
not the route an admin wrote, it is the number nobody thought about. When there are **no** routes
at all, no outbound context is written and no catch-all either — an outside number simply does not
match anything, which fails closed as well, and keeps the generated file identical to what a
trunk-less, route-less system produced before this piece.

The prompt is `ss-noservice` from Asterisk's core sounds. **To verify on the lab VM**: if that file
is not installed, Playback logs a warning and the call falls through to the `Hangup()` on the next
line, so the call still ends — it just ends in silence rather than with an explanation.

### D46. One context per route, included in order, because Asterisk picks the best match (2026-09-18)
Within one context Asterisk does not try patterns in file order: it picks the one it considers
most specific. "First match wins, in the order the admin listed" therefore cannot be done by
writing the patterns into a single context.

What Asterisk *does* honour is the order of `include =>` lines. So each route gets a context of its
own (`outbound-<name>`), `[outbound]` is nothing but includes in priority order, and `[internal]`
includes `[outbound]` at the end. A context's own extensions are searched before its includes,
which is what keeps a route pattern from ever stealing a call meant for an extension or a feature
code.

Pattern syntax is restricted to what we can reason about: digits, `X`, `N`, `Z`, a `[...]` set of
digits and ranges, and a trailing `.`. `!` is refused — it matches as soon as it can, which
surprises people — and so is anything else.

### D47. International dialling cannot be routed at all (2026-09-18)
The repository refuses to store a route whose pattern could reach an international number. There
is no checkbox, no "advanced" section and no override: **a route that starts with `0`, or with a
wildcard that can match `0`, is a validation error** with a message that says why.

That covers both halves of the real-world problem: the explicit `_011.` or `_00.` route, and the
`_X.` "one route to everywhere" that is how most compromised PBXes actually pay out. Ordinary
dialling is untouched — `_1NXXXXXXXXX`, `_NXXXXXXX`, `_911`, `_[2-9]XXXXXX` all pass, because none
of them can begin with a zero.

Two things this does **not** do, both worth knowing:

- **It assumes North American dialling.** A country where national numbers begin with 0 (the UK,
  most of Europe) cannot write a route at all under this rule. That is the escape hatch to design
  when it is needed, and it should be a deliberate per-system setting rather than a per-route
  checkbox.
- **It only looks at the start of the pattern.** `_9011.` is allowed, and would be nonsense today
  because the 9 is sent to the provider as part of the number — but if strip-digits is ever added
  (D44), that pattern becomes a real international route and this check has to grow to match.

### D48. A table row is a link to its edit form (2026-09-18)
Clicking anywhere on a row opens that row's edit form; the column of Edit/Password/Regenerate/
Delete buttons is gone. Those actions now live in the **footer of the edit modal**, where there is
room to label them properly and where they read as "things I can do to this extension" rather than
four small buttons competing with the data.

The table is the thing an admin looks at most, and it was becoming mostly buttons. One click
anywhere on the row is also fewer pixels to hit than a specific button.

How it works: each `<tr>` carries `data-edit-url`, and one delegated click handler on the body
calls `pbx.openEdit`, which is the same htmx fetch into the same shared modal that the Add button
does with attributes (D42). Delegation rather than a handler per row is what survives htmx swapping
the table and bootstrap-table re-rendering its rows on sort and search; bootstrap-table carries
`data-*` from the source row through that re-render, which is why the attribute is on the `<tr>`.
A click on anything interactive inside a row is that thing's click, not the row's.

`tr[data-edit-url]` gets `cursor: pointer` in site.css, because a row that does something when
clicked should look like it.

The alerts opened from inside the modal — show password, the regenerate confirm — are given
sweetalert2's `heightAuto: false`, or opening one shifts the modal underneath it.

### D49. Inbound routes belong to a trunk, and a catch-all is a flag rather than a pattern (2026-09-19)
`InboundRoutes` is TrunkID, DID, CatchAll, DestinationType, DestinationValue, Description,
Enabled.

**Per trunk, not global.** A DID is bought from one provider and only ever arrives on that
provider's trunk, and the dialplan already has a context per trunk (D38). So `TrunkID` is a real
foreign key, like an outbound route's (D44), and a trunk that still has inbound routes cannot be
deleted.

**Catch-all is a boolean, not a magic DID.** `CatchAll = 1` with an empty DID, rather than a `*`
or an empty box meaning "everything". An empty text box is something you reach by accident; a
checkbox is something you tick. `UNIQUE(TrunkID, DID)` then does two jobs at once: no two routes
claim one number on a trunk, and — because every catch-all stores the same empty DID — a trunk
cannot have two of them.

**No priority field.** Outbound routes needed one because their patterns overlap (D46). Inbound
routes do not: a DID is a literal extension and the catch-all is the pattern `_X.`, and Asterisk
always prefers a literal match to a pattern. The dialplan's own rules give "the specific one wins"
for free, so there is no order for an admin to get wrong.

`Description` is optional and ends up as a comment above the entry in the generated dialplan,
which is where someone reading `extensions.conf` at 3am would want it.

### D50. An inbound call that matches nothing is not answered (2026-09-19)
Each trunk context ends with `_X.` → NoOp → `Hangup()`, unless that trunk has a catch-all route,
in which case the catch-all *is* the `_X.` entry and sends the call to its destination.

Deliberately **not** answered first and not given a prompt, unlike the outbound blocked context
(D45): answering an unrouted inbound call means paying for it and telling a scanner that something
is here. Hanging up without answering lets the caller's own carrier say the number is unobtainable.

The important half is what this context never contains: no `include`, and no `Dial` to a trunk. A
call that arrives from outside cannot fall through to anywhere that dials out — that is how a PBX
becomes somebody else's long distance carrier. There is a test that asserts it.

The wording of the placeholder changed slightly from the piece-9 version (it now reads "No inbound
route for..."), so two golden files were updated; the extensions-only file, which has no trunks, is
untouched.

### D51. DIDs are matched exactly, and what a provider sends is still unverified (2026-09-19)
A route's DID is stored as digits and written into the dialplan as a literal extension, matched
character for character against what the provider puts in the request URI.

**This has not been seen on our lab VM yet.** Callcentric may send the full 11-digit number with
the country code, the 10-digit number, or the account number — we do not know, and guessing in
code would be worse than matching exactly and being told. Exact matching fails safely: a mismatch
falls through to the catch-all, or to the hangup, rather than routing a call somewhere wrong.

The form says so, and points at the Asterisk log as the way to find out. If it turns out a
normalisation step is needed — strip a leading `1`, or match on the last N digits — it belongs in
one place (the renderer, or a setting on the trunk), and this decision gets a dated note saying
which.

**Addendum 2026-09-19 (verified on the wire):** Callcentric puts the *account user* in the request
URI and the real DID in the `To:` header — the trunk contexts now dispatch on
`DID=${CUT(CUT(PJSIP_HEADER(read,To),@,1),:,2)}` (Callcentric's own DID-routing doc describes the
same thing for chan_sip). Exact matching on the extracted DID stands; the request URI is never
used for matching.

### D52. Ring groups are generated Dial() dialplan, not Asterisk queues (2026-09-19)
Both strategies we need — ring all and hunt — are one line of Dial() each: ring all is one Dial
with the members joined by `&`, hunt is one Dial per member in order. `app_queue` would add
agents, penalties, statistics and a queue application to learn, and would still need its own
config file. External numbers as members are deliberately deferred: a group that can dial out
is a toll-fraud hole, and nothing on the feature list needs it yet.

The group's ring time applies to each Dial attempt (so hunt rings member 1 for N seconds, then
member 2 for N seconds), and the no-answer destination is the shared D35 shape, so a group can
fail over to voicemail, another group, or an extension.

### D53. Ring group members are an ordered list in one column, not a table of their own (2026-09-19)
The order is the whole point for Hunt, and the same comma-list shape is already used for a
trunk's codecs and match addresses. A members table would add a join for what is always read
as a list. Members are extension numbers, validated against existing enabled extensions, and
the group number is collision-checked against extensions and feature codes.

### D55. ffmpeg is a required system dependency, and 8 kHz mono WAV is the one stored format (2026-09-19)
Announcements accept what browsers and phones actually produce — MP3, MP4/M4A, WAV, WebM, Ogg —
and store exactly one thing: **16-bit 8 kHz mono PCM WAV**. Every upload is converted on the way
in by `ffmpeg`, run by the unprivileged web user as a fixed argv list, never a shell string:

```
ffmpeg -nostdin -hide_banner -loglevel error -y -i <temp> -vn -map_metadata -1
       -ac 1 -ar 8000 -acodec pcm_s16le -f wav <temp>.wav
```

One format in the store means no transcoding at call time, no format negotiation to reason about,
and a file size that is a reliable clock (16,000 bytes a second), which is where the length in the
table comes from. 8 kHz is what a narrowband SIP call carries anyway, so nothing is being thrown
away that would have reached the caller.

**ffmpeg is therefore required, not optional**: the installer does `apt install ffmpeg`, and the
lab VM script already installs it. If it is missing, an upload **fails with a message that names
ffmpeg and the apt command**, and nothing is written. The alternative — storing the file as it
arrived — was rejected outright: Asterisk would accept the config, and the fault would only appear
as silence on a real call.

What guards the upload, in order, before ffmpeg is started at all:

- **A 20 MB cap**, counted while spooling, so a large upload is refused rather than buffered.
- **A content signature check**, not the file name: RIFF/WAVE, OggS, the EBML header, `ftyp`, an
  ID3 tag or an MPEG frame sync. A name is attacker-controlled and ffmpeg is a large program to
  hand an arbitrary file to. A test asserts this ordering by pointing the converter at a program
  that cannot start and checking the error is about the format, not about ffmpeg.

Recording in the browser uses **MediaRecorder** and posts the blob through the same form and the
same endpoint as a chosen file, so there is one upload path with one set of checks. Chrome and
Firefox produce WebM/Opus, Safari and iPhones produce MP4/AAC; both are on the accepted list, and
the container is read from the bytes rather than trusted from the blob's type. The JavaScript is a
few lines: `DataTransfer` puts the blob into the form's own file input. No new client library.

**The `modules.conf` allowlist is unchanged.** `app_playback.so`, `format_wav.so` and
`format_pcm.so` are already on it (D31) and are between them exactly what playing this file needs,
so this feature widens nothing. That was checked rather than assumed, and the comments in
`ModulesConfRenderer` now say which modules the announcements depend on.

### D56. Announcement audio: one directory per announcement under the Asterisk sounds path (2026-09-19)
Audio lives at **`<base>/announcements/<AnnouncementID>/<name-slug>.wav`**, where the base is the
`Announcements:SoundsPath` setting, default **`/var/lib/asterisk/sounds/tnpbx`**. The dialplan
plays it as `Playback(tnpbx/announcements/<ID>/<name-slug>)` — no extension, so Asterisk picks the
format it has, and relative, because Asterisk resolves a relative prompt name under its own sounds
directory.

Choices worth naming:

- **The directory is the ID, the file is the name.** The ID never changes, so nothing has to move
  when an announcement is renamed except the file itself; the file carries the name so that an
  admin reading `extensions.conf` or listing the directory can tell what a prompt is without
  looking it up. Renaming an announcement renames the file, in the same save.
- **The stored name is derived, never the uploaded one.** `Announcement.SlugFor` reduces the name
  to lower-case letters, digits and dashes. `,`, `&` and `)` would each end a `Playback()` argument
  early, and a path separator would leave the directory entirely, so none of them survive. The
  result is matched against `^[a-z0-9][a-z0-9-]{0,47}\.wav$` again by the model, again by the
  store before it touches a path, and again by `ConfText.Safe` in the renderer.
- **Nothing from the browser reaches a path.** A path is an integer and a derived name, and
  `AnnouncementStore` still resolves it and checks it is inside the base directory before writing
  or deleting. It cannot fail today; it is there so that it fails loudly the day something builds
  a path from something else.
- **Permissions follow D18.** Files are 0640 and directories 0750 **plus setgid (02750)**, owner the
  web user, group read
  for `asterisk` — the same model the generated conf files use. The installer creates the base
  directory `root:asterisk` and **setgid**, so what the web user creates under it is group-owned by
  `asterisk`; our code sets the mode on directories it creates itself and leaves an existing base
  alone rather than chmod-ing the installer's work away.
- **The database says what the file is called; the disk says whether it is there.** The renderer
  writes an entry for any announcement with a file name stored, and the UI reads the file to show
  its size and length — so a row naming a file that is missing shows "File missing" in the table
  and a warning in the form, rather than a length for nothing.

The one sharp edge: **`SoundsPath` has to stay at `<asterisk sounds dir>/tnpbx`**, because the
`tnpbx/announcements` prefix the dialplan uses is a constant. Point the setting somewhere else and
the files are written where Playback will not look. See the open question in
[roadmap.md](roadmap.md#piece-16-detail-started-2026-09-19).

### D57. A play extension is optional, and is what makes an announcement a destination (2026-09-19)
`Announcements.PlayExtension` is digits, 2 to 6, or blank. When it is set the announcement gets a
dialplan entry in the `internal` context — `Answer()`, `Playback(...)`, `Hangup()` — so anyone can
dial it to hear what callers hear. That is the test button, and it costs one dialplan entry.

It is also the whole of the destination mechanism. `DestinationType.Announcement` stores the play
extension as its value, and `DestinationDialplan` writes `Goto(internal,<number>,1)` — in by the
same door a user dialling it uses, exactly as an extension and a ring group destination already do
(D36). So there is one description of what an announcement does, not two.

The consequence, and it is deliberate: **an announcement with no play extension cannot be chosen as
a destination.** There is nothing to Goto. `DestinationCatalog` leaves it out, along with ones that
are switched off or have no audio yet, for the same reason disabled extensions are left out (D35) —
offering a choice that leads nowhere is worse than not offering it.

Rejected: giving every announcement a hidden internal extension so it could always be a
destination. It would put a dialplan entry in for every row whether anyone wanted one or not, and
an admin reading `extensions.conf` would find numbers nobody chose.

Because a play extension shares the number space with extensions and ring groups, it is
collision-checked against both, **in both directions** — `AnnouncementRepository` refuses a number a
ring group has, and `RingGroupRepository` now refuses a number an announcement plays on. A one-sided
check would leave the hole open from whichever side happened to be created second. Feature codes
need no check: they all start with `*` and a play extension is digits only, which a test pins down.

`Answer()` is written unconditionally rather than guarded by a `${CHANNEL(state)}` test. On a
channel that is already up, Asterisk's Answer application returns immediately and does nothing, so
the guard would buy nothing and cost two modules (`func_channel`, `app_exec`) on the allowlist.

### D58. An IVR's greeting is a reference to an announcement, not audio of its own (2026-09-19)
`Ivrs.AnnouncementID` is a required foreign key, and the menu plays that announcement's stored
file. The user's own description of the feature is "the IVR plays an announcement", and taking it
literally means this piece adds **no audio handling at all**: no second upload endpoint, no second
ffmpeg call, no second place a file name could come from, and one answer to "where do prompts
live" (D55, D56).

What follows from it:

- **Recording a greeting is recording an announcement.** Upload or record it on the announcements
  page, and the menu that references it is playing the new audio at the next apply. An announcement
  that exists only to be a greeting simply has no play extension, so it gets no dialplan entry of
  its own — it is audio with a name.
- **The reference cannot dangle.** The foreign key means an announcement an IVR still greets with
  cannot be deleted; `AnnouncementRepository.Delete` turns that constraint into "point the IVR at
  another announcement first", the way a trunk with routes does (D44).
- **A menu with nothing to play is not written.** The renderer leaves out any IVR whose greeting is
  missing, switched off or has no audio yet, exactly as it leaves out an announcement with no audio
  (D56), and the repository refuses to save one so the admin is told where they can see it. Answering
  a call and then sitting in silence is worse than the number not existing.

The one judgement here worth revisiting: an announcement's **Enabled** flag silences it as a
greeting as well as removing its own dial-in entry. "Switched off" reading as "not used anywhere"
is the least surprising meaning, but it does mean switching one off takes its menus off the air
with it. If that turns out to be the wrong trade, the change is one clause in `Ivr.GreetingIn`.

Rejected: **audio columns on `Ivrs`**, i.e. an IVR owning its own recording. It would double the
upload path, the conversion, the permissions and the file naming to save one dropdown, and it would
make "the same greeting on two menus" a copy instead of a reference (D35's whole argument).

### D59. One dialplan context per IVR, and the digit map is a table (2026-09-19)
**A context of its own, `ivr-<IvrID>`.** The keys a caller presses are matched as extensions of the
context the call is in: "press 1" is an extension called `1`. Putting those in `[internal]` would
make single digits dialable from every phone in the building and would collide with the numbering
plan, so each menu gets a context and the internal context gets one entry — the play extension,
which does `Goto(ivr-<n>,s,1)`. Named by ID because the ID never changes and a menu name is free
text a context name could not survive (D37's problem, solved the way D56 solved it for directories).

Like a trunk's context (D38, D50), a menu **includes nothing**. A caller who reached an IVR from
outside can never fall through to an outbound route, and a test asserts it.

The menu itself, in the shape Asterisk already has for this:

```
exten => s,1,Answer()
 same => n,Set(TIMEOUT(digit)=10)      ; the gap allowed between digits
 same => n,Set(IVR_RETRIES=0)
 same => n(start),Background(tnpbx/announcements/2/menu-greeting)
 same => n,WaitExten(10)
 same => n,Goto(t,1)
```

`Background` rather than `Playback`, so a caller who already knows the menu can press a key over the
greeting; `t` and `i` are Asterisk's own "pressed nothing" and "pressed something we have no
extension for" extensions, which is what makes a free digit need no row and no flag. Both count the
try, and both give up to a named `final` extension once there have been more tries than `Retries`
allows — so the final destination is written once, through the shared helper (D36), however many
ways lead to it. `Retries = 0` means the first mistake ends the menu. An empty final destination is
`Hangup()`.

**The digit map is `IvrEntries`, a table**, unlike a ring group's members (D53): each key carries a
destination of its own, and order means nothing (the renderer sorts into keypad order, 0-9 then `*`
and `#`). `ON DELETE CASCADE`, because a key has no life of its own; an update replaces the whole
map rather than merging it, because the form posts the whole menu.

**An IVR is a destination the same way an announcement is** (D57): `DestinationType.Ivr` stores the
play extension, `DestinationDialplan` writes `Goto(internal,<number>,1)`, and an IVR with no play
extension is not offered. The play extension is collision-checked against extensions, ring groups,
announcements and other IVRs, **in both directions** as D57 requires.

Loops are refused where they are silent: an IVR's **final** destination may not lead back round to
itself (the same chain-walk ring groups use, D54), because a caller who says nothing would be passed
between menus for ever. A **key** pointing at its own menu is allowed — that is "press 9 to hear this
again", and it only happens when somebody presses it. Known gap: the walk follows IVR-to-IVR only,
so a cross-feature ring, e.g. ring group → IVR → ring group, is still possible to build.

### D60. Direct dial is off by default and is one entry per extension (2026-09-19)
`EnableDirectDial` writes an explicit `exten => 1001,1,Goto(internal,1001,1)` per **enabled**
extension into the menu's context, rather than a pattern like `_XXXX`. That is D12's rule applied
where it matters most: only numbers that exist can be reached, and a caller feeling around a menu
cannot discover which numbers do.

Off by default, because it is a convenience that quietly widens what an outside caller can reach —
turning it on should be a decision, not something a site gets by accident.

The cost, worth knowing before someone reports it as a bug: with direct dial on and a key `1` in the
menu, a caller who presses 1 waits `TIMEOUT(digit)` before the call goes to option 1, because
Asterisk can see that more digits could still make `1001`. FreePBX behaves the same way for the same
reason. Single-digit keys and extension numbers cannot actually collide — an extension is 2 to 6
digits — so this is a delay, never a wrong destination.

### D61. `func_timeout.so` added to the module allowlist (2026-09-19)
`TIMEOUT(digit)` is a dialplan **function**, and functions live in modules: without
`func_timeout.so` the menu's `Set(TIMEOUT(digit)=...)` fails at call time with nothing in the config
to explain it. It is the only thing this piece has to widen (D31), and it was checked rather than
assumed — everything else an IVR calls is either already on the list (`app_playback` for the
greeting and the invalid prompt, `app_dial`, `app_voicemail`, `pbx_config`) or is an Asterisk
**builtin** that no module provides: `Answer`, `Background`, `WaitExten`, `Set`, `Goto`, `GotoIf`,
`NoOp` and `Hangup` are all registered by the core.

The list is now 37 modules. Still absent and still waiting: `res_pjsip_refer`, `res_musiconhold`,
`cdr_*`, `app_stack`, `res_pjsip_mwi`.

**To verify on the lab VM**, in the same spirit as D45's note about `ss-noservice`: that
`func_timeout.so` is actually built (it is a standard function module, but our Asterisk is built
from source), and that the `invalid` prompt is installed in the core sounds. A missing prompt is not
fatal — Playback logs a warning and the menu carries on to the next line — but the caller hears
silence instead of "that's not a valid extension".

### D62. Time conditions are built as one form per condition, not FreePBX's two (2026-09-19)
FreePBX splits the feature into "time groups" (ranges of time) and "time conditions" (a group plus
two destinations). That split is its data model showing through, not what an admin thinks: nobody
builds "9–5 Mon–Fri" first and wires it up second. Here one **`TimeConditions`** row is the whole
answer for one number — three destinations (open, closed, holiday) chosen in one form (F8) — and
the rules that say when each applies sit in **`TimeConditionRules`**, written and replaced in one
save the way an IVR's digit map is (D59). A condition that needs the same hours twice is a second
row, and since conditions are destinations, one can even route into another.

### D63. Holidays are checked before open hours, and a holiday can carry its own destination (2026-09-19)
A condition's context checks the holiday dates first, so a holiday wins over hours that would
otherwise be open — the order an admin expects. Each holiday date may carry a **destination of its
own**, overriding the condition's holiday destination for that one day ("Christmas goes to the
closed message, every other holiday to voicemail"); empty means the condition's holiday
destination is used. Both kinds of rule live in one table with a `Kind` column, because they are
edited in one form and always read together.

### D64. A holiday repeats every year: `GotoIfTime` has no year field (2026-09-19)
The date field of a `GotoIfTime` spec is day-of-month and month — no year — so a holiday rule
matches "25 December", every year, forever. The stored `HolidayDate` keeps its year (it is real
metadata: when the admin entered it), but the renderer strips it, and validation refuses two
holidays on the same month/day because the second `GotoIfTime` could never be reached. FreePBX
has the same limitation; there is no honest way to widen it without evaluating time in the
dialplan with functions rather than `GotoIfTime`, which is not worth the surface.

### D65. The clock is Asterisk's own local time; the timezone setting records which one that is (2026-09-19)
`GotoIfTime` evaluates against the system clock of the machine Asterisk runs on. Rather than
build a timezone engine, the one **`Timezone`** setting stores the IANA name of the zone the
server is expected to run in (`America/Los_Angeles`); the generated context states it in a
comment, and it is the installer's job to agree `/etc/localtime` with it. Documentation, not
behaviour — the honest version of a setting that would otherwise silently not work.

### D66. Time conditions need no new module (2026-09-19)
`GotoIfTime` ships in `pbx_config`, which the allowlist already loads; the contexts use nothing
but `Answer`, `NoOp`, `GotoIfTime`, `Goto`, `Set` and the destination helper, all already allowed.
The allowlist is unchanged.

### D67. The general Settings page lists every key in one table (2026-09-20)
The Settings table got a UI: a "Settings" menu with a general page showing every key in
`SettingsKeys`, each row clickable into the usual edit modal, with the built-in default shown
next to the stored value. Descriptions live in one catalog (`SettingsCatalog`) so no row can
claim a default the code does not use. Pages that edit a single setting (Timezone on the time
conditions page) keep their shortcut; both routes write the same table. Editing a setting is a
config change like any other — it marks config pending so the apply button lights up.

### D68. Secret settings are written like extension passwords: never shown, blank means "leave alone" (2026-09-20)
`Ami.Secret` is the only secret key. The table shows it masked, the modal never renders the
stored value, and a reveal goes through the API the way an extension's password does. A blank
field in the edit form means "keep the current value", not "clear it" — resetting to default
still works through the footer button with a confirm.

### D69. Setting edits mark config pending (2026-09-20)
Every setting the Settings pages can write is read while config is generated, so editing one is
a config change like editing an extension: the pending marker is set and the red apply button
appears. Nothing is written to `/etc/asterisk` until Apply.

### D70. A TCP transport is generated only when a TCP port is set (2026-09-20)
pjsip.conf renders `transport-udp` always (bind address + UDP port) and `transport-tcp` only
when `Sip.TcpPort` is set — an unset port means no listener at all, not a broken one. Both
transports share the bind address, `local_net` list and external addresses, because those
describe the machine rather than the protocol. Nothing binds endpoints to the TCP transport
by name: a phone that connects over TCP is matched by the endpoint it authenticates as, and
trunks stay on UDP.

### D71. The TLS port is stored only: no TLS transport until certificate management exists (2026-09-20)
`Sip.TlsPort` exists as a setting (and the SIP Settings page edits it, with help text saying
so), but nothing renders a TLS transport: a pjsip TLS transport without `cert_file` stops
Asterisk loading the file, so generating one would break every call. TLS becomes real when
certificate management is its own piece.

### D72. STUN renders into rtp.conf, verified on Asterisk 22 first (2026-09-20)
`Sip.StunServer` (default `stun.l.google.com:19302`, empty = disabled) renders as
`stunaddr = host[:port]` in rtp.conf, and `icesupport = yes` is written when either STUN or
the NAT external address is set. Verified on the lab VM before writing the renderer: Asterisk
22's `res_rtp_asterisk` supports `stunaddr` in rtp.conf (including `host:port` and periodic
DNS re-resolution) and `icesupport`. rtp.conf is written but never reloaded (D33), so changing
these settings reports RestartRequired honestly, like modules.conf changes.

### D73. Codecs come from settings, limited to the modules the allowlist loads (2026-09-20)
The hardcoded `allow = ulaw,alaw` in generated endpoints is replaced by the `Sip.Codecs`
setting (default `ulaw,alaw`). The catalog of names a user may pick (`SipCodecs.Allowed`) is
exactly ulaw, alaw and gsm — the codec modules the modules.conf allowlist loads (D31) —
because naming anything else would generate a pjsip.conf Asterisk cannot honour. Adding a
codec means adding its module to the allowlist first, and then to this list.

### D74. GotoIfTime names the zone: open hours are local time, the server clock is UTC (2026-09-20)
This supersedes the documentation-only half of D65. Every `GotoIfTime` in a generated time
condition gets the recorded timezone as its fifth argument (`GotoIfTime(9:00-17:00,mon-fri,
*,*,America/Los_Angeles?open)`), so Asterisk evaluates the rule in that zone — DST included
— whatever the server's own clock says. The clock itself is meant to be UTC: the installer
(piece 21) will set it that way, so a TNPBX server is always predictable. Open hours and
holidays are entered in the customer's local time, which is what the person programming
them means by "nine to five". The zone is appended always, even for Etc/UTC (explicit beats
implicit). Verified against the Asterisk 22 source on the lab VM before writing the renderer:
`pbx_builtins.c` documents GotoIfTime as `<time range>,<days>,<dates>,<months>[,<timezone>]`
and evaluates via `ast_check_timing2` in that zone.

### D75. The timezone is chosen from a dropdown of the server's own zone list (2026-09-20)
`System.Timezone` is edited as a dropdown built from `TimeZoneInfo.GetSystemTimeZones()`
(Linux IANA ids, `Etc/UTC` first as the default), not free text — both on the time conditions
page and in the general settings modal, which now renders a dropdown for list-backed keys
(`SettingDescriptor.Choices`). Validation for the key becomes membership of that list, so a
zone that cannot be evaluated can never reach the generated dialplan.



### D76. Kestrel serves 80/443 directly; no reverse proxy in front (2026-09-20)
The web app is the whole appliance: one app, one box, one purpose. Kestrel is
Microsoft-supported as an internet-facing server, so nginx/Caddy would add a second daemon
to patch and a second config to drift, for capabilities we do not have (vhosts, multiple
backends, static offload). Port 80 binds redirect-only, 443 is the real listener once
certificates are managed (a cert piece is needed anyway for SIP TLS, D71 — one piece solves
both). Escape hatch: a customer needing to share 443 with another site on the same box can
put Caddy in front, but that is not the default posture.

### D77. Phone provisioning is a trust path of its own: Basic auth, plain routes, either scheme (2026-09-21)
A desk phone cannot sign in to Entra ID. It has no browser, no cookie jar and no way to complete an
interactive flow, so the admin UI's authentication is simply not available to it. Provisioning
therefore gets its own gate, and the design keeps it visibly separate from the rest of the app:

- **The routes are plain `/polycom/...`, not `/api/...`.** Everything under `/api` shares the Entra
  cookie (D5) and carries an antiforgery token (D23). Putting an unauthenticated endpoint in there
  would make "is this behind the cookie?" a question you have to read the controller to answer. A
  different path prefix means the exception is visible in the URL.
- **HTTP Basic against two settings keys**, `Provisioning.Username` and `Provisioning.Password`.
  They are the user:pass half of the DHCP option 160 URL the phones are given
  (`http://user:pass@this-server/polycom`), which is the only credential a phone can carry.
  `Provisioning.Password` is the second secret key in the Settings table, handled exactly like
  `Ami.Secret` (D68): masked in the table, never rendered into the form, read back only through the
  API, never logged. Both are restricted to URL-safe characters, because a colon or an `@` in
  either would split the option 160 URL.
- **Unset credentials mean provisioning is off, not open.** `BasicAuth.Matches` refuses a blank
  expected username or password before it looks at the header at all, so a system nobody has
  configured serves nobody. The phones page says so in a banner rather than leaving an admin
  wondering why no phone ever appears.
- **Both schemes are answered.** DHCP option 160 carries a scheme, and a phone pointed at `http://`
  has to be answered rather than redirected to a URL whose certificate it may have no root for.
  `/polycom` is therefore the one path exempted from `UseHttpsRedirection`, via `UseWhen` in
  `Program.cs`. The credentials are the gate either way; what a plain-HTTP fetch costs is
  confidentiality of the SIP password in transit on the LAN — the same exposure every phone
  provisioning system has, and why HTTPS is the better answer once certificates are managed (D71,
  D76).
- **Three answers and nothing else.** 401 with a `WWW-Authenticate` challenge for credentials that
  are missing, wrong or not configured; 403 for a User-Agent that is not a phone, a phone that is
  disabled, or a MAC whose model has changed (D78); 404 for any file name that is not one of the
  two we generate. Every refusal is logged with the remote address, so the blocker in piece 20 has
  something to read.

Credentials are compared with `CryptographicOperations.FixedTimeEquals` rather than `==`. Known and
accepted: a length difference is still distinguishable by timing, because that comparison returns
early. That leaks the length of a credential, not its content.

### D78. Auto-registration: a phone we have never seen adds itself (2026-09-21)
Approved by the user, and the behaviour of the FreePBX module they have been running for years
(`jaysamthanki/polycomphones`) — that module is the reference for *what happens*, not a source of
code. The alternative is typing twelve hex digits off the underside of every handset, which is the
part of deploying phones that actually goes wrong.

A request for `exten<mac>.cfg` that gets past the credential gate, carries a User-Agent that parses
as a Polycom phone, and names a MAC we have never seen **inserts a row**: MAC, model, firmware,
source address and timestamp, with no name, no extension, enabled. A known MAC has those same four
fields brought up to date; the name, the extension and the enabled flag are the admin's and are
never touched by a phone.

What the endpoint refuses, and why each one:

- **A User-Agent that does not parse** gets 403. `PolycomUserAgent` matches the real shapes —
  `FileTransport PolycomVVX-VVX_410-UA/5.9.5.0614`, the older SoundStation/SoundPoint names, and
  `PolyEdge` — anchored at the start, with the model and firmware pulled out of it. This is a
  filter, not a security boundary: a header is trivially forged, which is why the credentials are
  the gate. It is worth having because credentials that leak are worth much less to a scanner that
  also has to sound like a desk phone.
- **A known MAC whose stored model no longer matches** gets 403. That is either somebody claiming
  another phone's MAC address or a handset that has been swapped, and both want an admin to look
  rather than a config file with somebody's SIP password in it. A row with no model recorded
  matches anything, because the first request is what fills it in.
- **A disabled phone** gets 403 at its next poll, which is what the enabled flag is for.

Two things follow from the flow. The **master file is served for an unknown MAC**: it holds no
credentials, only the name of the file to fetch next, and it is that next fetch which adds the
phone. And an **unassigned phone still gets a valid config** — the time and a provisioning poll, no
registration — so a phone can be racked, powered on, and assigned an extension later without anyone
touching the handset. It picks the extension up at its next poll (a day, D79) or at a reboot.

The phones page has **no "Add phone" button** for the same reason: a MAC address is something the
phone knows and a human mistypes. If manual entry turns out to be wanted — staging phones before
they arrive — it is a small addition, but ask first.

### D79. The config is generated per request and never stored on disk (2026-09-21)
There is no `/tftpboot`, no generated-files directory for phones, and nothing to apply. A request
comes in, the row and its extension are read, and the XML is built into the response.

What this buys:

- **No apply step and no pending marker.** Every other repository raises the config-pending marker
  (D26) because it changes a file Asterisk reads. `PhoneRepository` raises nothing and the phones
  page fires no `configChanged` event, because saving a phone changes no file — it changes what the
  *next* request returns. A test asserts the marker stays down.
- **No file on disk holding every SIP password.** The one place those live is the database, which is
  0600 (D14). A provisioning directory would be a second one, readable by whatever serves it.
- **Nothing to go stale.** A file written at assign time and a database row can disagree; a file
  generated from the row cannot.

The cost is that a phone only sees a change when it asks. `prov.polling` is set to 24 hours, which
is the trade: short enough that assigning an extension takes effect without walking to the desk,
long enough not to be a load. Rebooting the phone is the impatient version.

The renderers are pure static functions with golden files in `tests/Techie.Pbx.Tests/Expected/`,
exactly like the Asterisk conf renderers, and for a sharper reason: these files are read by the
handsets themselves, so a change to one is a change every phone on a site picks up. An expected
file that has to be updated is the point at which somebody has to say why.

**Deliberately not built, and worth naming as future work:** the PUT endpoints Polycom phones use to
upload logs, per-phone overrides and contact directories. The master file names those directories
(`logs`, `overrides`, `contacts`) because the phone expects the parameters to exist; with no
endpoint behind them the phone's uploads fail and it carries on. Adding them means adding a write
path that barely-authenticated hardware can reach, which is its own decision.

### D80. Deleting an extension unassigns its phone rather than being refused (2026-09-21)
`Phones.ExtensionID` is `ON DELETE SET NULL`, not the usual "you cannot delete this while something
points at it" (D44, D49). The difference is that a route pointing at a deleted trunk is broken
config, whereas a phone whose extension has gone is a piece of hardware still sitting on a desk: the
right outcome is that it stays known, loses its registration, and waits to be assigned again. It
shows as **Unassigned** in the table, and its next config fetch gives it the time and nothing else.

An extension that is merely **disabled** is treated the same way at render time — no PJSIP endpoint
exists for it, so handing the phone credentials that cannot register would only produce a handset
showing an error. The assignment itself is kept, so re-enabling the extension puts the phone back to
work.

### D81. Each phone is told a local SIP port of its own, derived from its ID (2026-09-21)
`voIpProt.SIP.local.port` is `1024 + (PhoneID mod 64512)` rather than the 5060 every phone would
otherwise use. Several phones behind one NAT all sourcing from 5060 is the classic symptom set — one
phone's calls arriving at another, registrations displacing each other — because the NAT has to
rewrite ports it was not told to, and does it differently on different routers.

Derived from the ID rather than allocated from a pool: no allocation table, no reuse problem when a
phone is deleted, and the port for a given phone never changes, so it is a stable thing to write
into a firewall rule. The port is shown in the edit modal, because it is the first thing to check
when phones behind one NAT misbehave.

**Note the parameter name.** Polycom's own documentation calls this `voIpProt.local.port`; the
approved design for this piece specifies `voIpProt.SIP.local.port`, which is what is written.
**To confirm on a real handset** — if the SIP-scoped name is ignored by the firmware in the lab, the
fix is one constant in `PolycomConfigRenderer`.

### D82. The phone's clock is a number of seconds, worked out when the file is generated (2026-09-21)
`tcpIpApp.sntp.gmtOffset` is an offset in seconds with no notion of a zone in it, so there is
nowhere to put "America/Los_Angeles" the way there is in a `GotoIfTime` (D74). The offset is
computed from the `System.Timezone` setting with `TimeZoneInfo` **at the moment the config is
generated**, and the SNTP server the phone is given is this machine — the phones ask the PBX for the
time, and the PBX's own clock is UTC and disciplined by NTP (D74).

The consequence, worth knowing before somebody reports it as a bug: **a phone crosses a daylight
saving boundary when it next polls for its config, not when the clocks change.** With a 24-hour poll
(D79) that is up to a day of phones showing the wrong hour, twice a year. Rebooting fixes it
immediately.

This is the honest small version. The full version is Polycom's own DST parameters
(`tcpIpApp.sntp.daylightSavings.*` — start and stop month, day of week, occurrence), half a dozen
more values that all have to be derived correctly from the zone. That is a real piece of work and it
is not on the feature list, so it is flagged here rather than guessed at. **Open question for the
user:** is a twice-yearly one-day drift acceptable, or should the DST parameters be derived?

### D83. We point phones at a firmware image but do not serve one (2026-09-21)
The master file says `APP_FILE_PATH="sip.ld"`, the name Polycom's combined image uses, which the
phone resolves against its provisioning server. We do not serve it: a phone that cannot fetch an
image keeps the firmware it is running, which is the safe outcome.

So TNPBX does **not** do firmware management. Upgrading a fleet stays a manual job. Serving firmware
would mean hosting hundreds of megabytes of vendor binaries, tracking which model takes which image,
and owning the failure mode where a bad image bricks a desk phone. That is a feature, not a field —
ask before building it.

### D84. The NTP server phones ask is a setting, not this server's own address (2026-09-21)
Until now `tcpIpApp.sntp.address` was whatever host the phone happened to ask us on — this server's
own address, filled in from the request rather than chosen. `System.NtpServer` replaces that with an
explicit setting, defaulting to `pool.ntp.org`: most sites need nothing else, but a site with its own
time source, or one that blocks outbound NTP to the public pool, can point every phone there instead.

### D85. The Polycom web UI's device passwords are settings, and the admin one doubles as the push credential (2026-09-21)
Polycom's firmware fixes the username on both of a phone's built-in web accounts — "Polycom" for
admin, "User" for the other — so only the password is ours to set. `Provisioning.AdminPassword` and
`Provisioning.UserPassword` are written into every generated config as
`device.auth.localAdminPassword` and `device.auth.localUserPassword`. Either one left unset is left
out rather than written blank, and all three flags are needed for a value to apply: `device.set="1"`
on the device element (the Edge E admin guide's master switch, without which the phone stores but
never uses provisioned passwords) plus `<c>.set="1"` on each parameter. The phone also refuses
`456` for the admin password outright — SB-327: once a non-default admin password exists the
default is never accepted again — and each application happens at boot, not at config parse.
out of the file rather than written blank: a phone with a blank web password is worse off than one
with no opinion from us at all, and the two are independent so setting one does not blank the other.

The admin password is reused as the digest credential this app authenticates with when it pushes a
config reload or reboot to a phone (D86): the phone's own web UI is the only thing that account
exists for, and it is already the credential an admin has to know to manage the phone directly.

### D86. Saving a phone pushes it to reload its config immediately; the daily poll (D79) stays the fallback (2026-09-21)
The mechanism is the one from the user's own FreePBX module (`polycomphones_push`), because the
reasoning still holds: an admin who just assigned an extension should not have to wait up to a day,
or walk to the desk and reboot it, to see it take effect. Saving a phone in the UI now sends
`https://<phone>/push` with body `Action:UpdateConfig`, HTTP digest auth as `Polycom` against
`Provisioning.AdminPassword`, a 2 second timeout, and the phone's certificate accepted unchecked —
its web UI is always self-signed, so there is nothing to check it against.

The push is fire-and-forget and its failure is not fatal: a phone that is off, on another network, or
slow to answer still gets the same config at its next poll regardless (D79), so a failed push is
logged as a warning and the save still reports success. It is skipped silently, with no attempt and
no warning, when the phone has never provisioned (no `LastIP`) or when `Provisioning.AdminPassword`
is unset — in either case there is nothing to push with.

Not built: the SIP NOTIFY check-sync fallback FreePBX also has, which needs `notify.conf` and a
dialplan hook of its own (out of scope for this piece). Worth revisiting if a site's phones are
reachable for SIP but not for their web UI — behind a firewall that only forwards port 5060, say.

### D87. A "Reboot phone" button, same push mechanism with a different action (2026-09-21)
The phone edit modal gets a `Reboot phone` button next to Delete, shown only once a phone has an
address to push to (a phone that has never provisioned has nowhere to send it). It calls the same
push as D86 with `Action:Reboot` in place of `Action:UpdateConfig`, confirmed with sweetalert2 first
(D42) because it drops any call the phone is on immediately. Best-effort the same way: a phone that
cannot be reached is named in the toast rather than failing the request, because nothing here is a
config change — there is no apply and no config-pending marker either way.

> **Superseded by D123**: the button sends a SIP NOTIFY instead, which reaches a phone behind NAT.
> The confirm, the toast and the best-effort handling are unchanged; `PolycomPusher.PushReboot` is
> gone. The config-reload push of D86 stays as it is.

### D88. Yealink is a second brand on the same trust path (2026-09-21)
The /yealink endpoint shares the Polycom provisioning gate: the same Provisioning.Username /
Provisioning.Password Basic auth, the same auto-registration rules, and the same Phones table
with a new Brand column (schema 012; existing rows are Polycom). A phone's brand comes from
which endpoint added it; the model-mismatch refusal only compares within a brand. Yealink
phones are pointed here with DHCP option 66 — option 160 is the Polycom mechanism.

### D89. Yealink config is plain text, generated per request, nothing on disk (2026-09-21)
Yealink wants `key = value` lines with a `#!version:1.0.0.1` header, not XML: the boot file
(y000000000000.boot, include lines only — we ship no model-common cfg) and the per-phone
`<MAC>.cfg` are both rendered on the fly like the Polycom files, from the same row and the same
settings (Sip.BindAddress/Port, System.NtpServer, System.Timezone offset in minutes with the
sign tested, Sip.Codecs mapped ulaw→pcmu / alaw→pcma). Unlinked phones get time + auto-provision
lines but no account lines. The config embeds auto_provision.server.url/username/password so
the phone keeps finding this server.

### D90. Yealink has no HTTP push: its signal goes by SIP NOTIFY (2026-09-21)
Polycom phones get Action:UpdateConfig/Action:Reboot pushed over their web UI (D86–D87).
Yealink has no such endpoint, so the same save flow and the same modal button instead send a
SIP NOTIFY through AMI PJSIPSendNotify to the linked extension's endpoint; an unlinked phone
cannot be notified and the push is skipped with a warning. Polling (D79) remains the safety
net for both brands.

> **Amended by D134**: Yealink now has the reboot button too, sending `check-sync;reboot=true`.
> Saving a Yealink phone still sends the config re-read (`reboot=false`).

### D91. pjsip_notify.conf is a generated conf, and res_pjsip_notify joins the allowlist (2026-09-21)
The NOTIFY categories (tnpbx-check-cfg = Event: check-sync, tnpbx-reboot = check-sync;reboot=true)
are generated into **pjsip_notify.conf** — the file Asterisk 22's res_pjsip_notify actually reads
(`notify.conf` belongs to the dead chan_sip; discovered live on the lab VM when the module
declined to load) — and res_pjsip_notify.so joins the modules.conf allowlist to load it.
Per D33, pjsip_notify.conf is written but never live-reloaded: an apply that changes it reports
a restart, which is rare since the categories are fixed.

> **Superseded in part by D123.** The two types are now `polycom-reboot` and `yealink-reboot`,
> each carrying `Content-Length: 0`, and the file has no `[general]` section. The code sends the
> NOTIFY with `Action: PJSIPNotify` and the headers spelled out as `Variable:` lines —
> `PJSIPSendNotify`, named here, is not an action Asterisk has.

### D92. The installer ships everything except the application; deploying the app is a separate step (2026-09-22)
User decision. `install.sh` prepares a fresh Debian server completely — clock, packages, users,
permissions, Asterisk built from source, systemd unit — and then stops. It does not publish
`Techie.Pbx.Web`, does not write `tnpbx-web.service`, and does not install the polkit rule that
lets the web user restart Asterisk.

The reason is sequencing rather than design: the application is still changing, and an installer
that bakes in a deploy would have to be revised every time the publish layout, the configuration
file or the unit's `ReadWritePaths` moved. What the server needs underneath the app is settled
and proven on the lab VM; what the app's own deploy looks like is not. So part 1 is the part that
can be finished, and the deploy lands on top of it as part 2 once the app is finalised.

What this costs, and it is worth being plain about it: **a server this script has finished is not
a working PBX.** It is a machine with Asterisk on it and an empty `/opt/tnpbx`. The script's
closing summary says so in as many words, lists what it deliberately did not do (app, polkit,
Helper, firewall, fail2ban) and names the piece each of those belongs to, because an installer
that finishes quietly is an installer an operator will assume is done.

`/opt/tnpbx` is created anyway — `tnpbx:asterisk`, 0750, empty — so that the deploy step is a
copy into a directory that already has the right owner rather than a second place that has to
know about permissions.

### D93. The installer leaves Asterisk enabled but stopped (2026-09-22)
`systemctl enable asterisk`, no `systemctl start`. The database is the source of truth and the
application generates every file in `/etc/asterisk` on its first apply (D4), so at the end of the
install that directory is **empty** and there is nothing for Asterisk to read.

This is not merely "it would not do anything useful". An Asterisk with no `modules.conf` falls
back to `autoload = yes` and loads every module it was built with — including
`res_pjsip_endpoint_identifier_anonymous`, the one D31 names as how a PBX ends up taking calls
from strangers. Starting it before the generated allowlist exists would open exactly the surface
that allowlist is there to close, on a box that by definition has no firewall yet (piece 19).

For the same reason `make samples` is **not** run. The spike script wiped `/etc/asterisk` after
`make install` for its own reasons; here the sample config is simply never installed, so there is
no window in which a startable-but-unconfigured Asterisk exists. `make install` alone still brings
the core sound files the generated dialplan plays (`ss-noservice` per D45, `demo-echotest`,
`invalid` per D59).

Enabled-but-stopped rather than disabled, because the intended end state is that Asterisk starts
at boot; the only thing missing is the first apply. After it, `systemctl start asterisk` is the
operator's one manual step, and every boot after that is automatic.

### D94. The lab VM's layout is the canonical layout, and the installer reproduces it exactly (2026-09-22)
The users, groups and permissions in `install.sh` are not a fresh design. They are what has been
running on the lab VM through every piece from extensions to phone provisioning, which means the
web process writing `pjsip.conf`, Asterisk reading it, announcement audio being written by one
user and played by the other, and the whole of `/etc/asterisk` being replaced on every apply have
all been exercised against this layout rather than reasoned about.

| Path / principal | Owner | Mode | Why |
|---|---|---|---|
| `asterisk` | system user, `/var/lib/asterisk`, nologin | | Runs the PBX |
| `tnpbx` | system user, `/opt/tnpbx`, nologin | | Runs the web app; never root, never sudo (D3) |
| `tnpbx` in group `asterisk` | | | The whole of how the app writes config (D18) |
| `/etc/asterisk` | `root:asterisk` | **2770** | Group write lets the app create files; setgid makes them group-owned by `asterisk` so the PBX can read them (D18) |
| `/var/lib`, `/var/spool`, `/var/log/asterisk` | `asterisk:asterisk` | 0755 | Asterisk's own working directories |
| `/var/lib/asterisk/sounds/tnpbx/announcements` | `asterisk:asterisk` | 2770 | Same setgid model for uploaded audio (D56) |
| `/opt/tnpbx` | `tnpbx:asterisk` | 0750 | The app and its `Data` folder (D25) |

Two consequences of writing it down. First, the permissions block in the installer is a function
(`apply_layout`) that is called again after `make install`, because Asterisk's own install creates
several of these directories and would otherwise undo the modes set before it. Second, this table
is now the thing to change when the layout changes — the installer, the lab VM and this decision
are meant to say the same thing, and the way that stays true is that there is one description of
it rather than three.

Open, and noted in [roadmap.md](roadmap.md): D56 describes the announcements directory as
`root:asterisk` while the lab VM and the installer make it `asterisk:asterisk`. Group write via
`asterisk` is what matters and both forms give it, so this is a tidying-up question rather than a
functional one.

### D95. app-deploy.sh is the installer's second half; the server's live state lives in /opt/tnpbx (2026-09-22)
Deploying the web app is `scripts/app-deploy.sh <publish.tgz>` run as root on a box prepared by
install.sh: it unpacks the self-contained publish into /opt/tnpbx (tnpbx:asterisk 0750), writes
tnpbx-web.service (User=tnpbx, never root; binds 0.0.0.0:8080 until the certificate piece moves
it to 80/443 per D71/D76; hardened with NoNewPrivileges, ProtectSystem=strict and ReadWritePaths
limited to /opt/tnpbx, /etc/asterisk, the tnpbx sound directories and the voicemail spool),
writes the polkit rule scoped to exactly asterisk.service for user tnpbx, and enables + starts
the service. appsettings.json and Data/ (the SQLite database) are the server's live state and
are preserved across every re-deploy — the tarball always carries repo defaults only. Verified
2026-09-22 on the freshly installed lab VM: app active as tnpbx-web.service, first apply wrote
all nine conf files into the empty /etc/asterisk, Asterisk then started on generated config and
AMI/pjsip/provisioning all came up.

### D96. First apply happens before Asterisk's first start, by design (2026-09-22)
The order proven on the fresh box is the order the installer prints: app up → sign in → set AMI
secret/timezone → add an extension → Apply (this writes /etc/asterisk; its AMI reload fails
harmlessly because Asterisk is still stopped — the message says exactly that) → start Asterisk.
From then on apply-and-reload is live. This is why part 1 leaves the unit enabled but stopped
(D93): an Asterisk started on an empty /etc/asterisk would autoload every module, and Asterisk
cannot read config that does not exist yet.

### D97. Certificate management is Certes in-process, not certbot (2026-09-22)
User decision. The ACME client is the Certes library inside the web app: no second runtime on the
customer's Debian box, no /etc/letsencrypt state to parse, and ordering is just HTTPS calls the app
makes. HTTP-01 only — wildcards would need DNS-01 (Cloudflare API), noted as future and not built.
The only ACME directories the Cert.AcmeServer setting accepts are Let's Encrypt production and
staging; the account key (Cert.AcmeAccountKeyPem, secret) is generated on first use and the account
registered on first order with the Cert.Email contact.

### D98. The app answers its own HTTP-01 challenges on port 80 (2026-09-22)
/.well-known/acme-challenge/<token> is served by the app from an in-memory store of the answers of
the order in flight, anonymous for that path only — outside the Entra cookie and outside the local
bypass logic. Port 80 therefore binds in every mode (D99): a server not listening on 80 can never
obtain its first certificate.

### D99. Kestrel's bindings follow the certificate, and the unit grants one capability (2026-09-22)
A pure rule (WebBindings), decided at startup from the certificate rows: 8080 always (the port
every existing install, lab note and DHCP option URL already names, and the way back in), 80 always
(redirect to HTTPS except the ACME path), 443 only when a usable certificate exists. A certificate
row counts as usable only when it is enabled, issued and not expired — the same test the SIP TLS
transport makes (D101), so the UI and SIP agree. Kestrel is given the certificate at startup, so a
new or renewed one serves after the app restarts (the renewal service's report says so). Ports 80
and 443 are privileged, so the tnpbx-web unit carries AmbientCapabilities=CAP_NET_BIND_SERVICE and
a CapabilityBoundingSet of exactly that — the only capability the app gets.

### D100. Renewal runs daily, thirty days before expiry (2026-09-22)
A hosted service checks every certificate row once a day. A row that is enabled and either never
issued (a first order that failed, retried) or inside 30 days of expiry is ordered again; Let's
Encrypt's 90-day lifetime leaves a month of failed attempts before anything stops working. Failures
land in the row's LastError and the Certificates page shows them — renewal never raises.

### D101. One combined PEM feeds both HTTPS and SIP TLS, and an unusable certificate is no certificate (2026-09-22)
The apply writes certificate, issuers and private key into one file, /etc/asterisk/tnpbx-cert.pem,
and the generated pjsip transport-tls points both cert_file and priv_key_file at it — one file to
write, one mode to get right, no way for a cert and its key to be applied apart. The file is
written even when there is no certificate (empty), so a stale key never lingers, and it joins the
restart set (D33): a transport reads its certificate when it is built, so a renewed one reaches
SIP at the next Asterisk restart. This makes the parked Sip.TlsPort setting real (D71).

### D102. The data-protection key ring persists in Data/keys, with a one-year key lifetime (2026-09-22)
The auth cookie, the OIDC state and correlation cookies and the antiforgery tokens are all
encrypted with the data-protection key ring, and the default ring lives in process memory — every
restart or re-deploy issued a fresh key (the journal showed three key GUIDs in one afternoon),
signing everyone out and killing any sign-in flow that straddled the restart. That is exactly the
"first sign-in took me to an error page but everything works afterwards" report: the OIDC
callback could not read its own state. The ring now persists to Data/keys beside the database,
inside the deploy target — the same protected live state as appsettings.json and Data/ (D95) —
and keys are created with a 365-day lifetime (user decision): the appliance rolls the key over
in the background long before expiry, and a ring that survives re-deploys is the point.

### D105. System.Hostname: the name phones are told to reach the PBX on (2026-09-22)
Provisioning used to be purely request-derived: whatever host a phone asked on went into its
config as the SIP server address and the provisioning URL — clean by default, but it means a
phone that first contacts the box by IP keeps using the IP until it is re-provisioned by name.
System.Hostname (Phones scope, empty by default) pins it: when set, every generated phone config
names that host instead of the request's, so a phone lands on the site's real name however it
first found the box. Empty keeps the request-derived behavior, which is also the only thing that
works before the box has a name at all. A bare hostname or IP only — no scheme, no path — and
because it is Phones-scoped, saving it lights no apply button (D103): phones pick it up at
their next poll. The phones table also grew bootstrap-table's standard refresh button.

### D103. Settings have a scope, and only an Asterisk-scoped one lights the apply button (2026-09-23)
Every settings write raised the config-pending marker, because when the Settings table held only
AMI and SIP keys that was true (D69). It stopped being true when phone provisioning and ACME
arrived: changing a Polycom device password or the NTP server phones are told to use turned the
apply button red, and the apply it asked for then wrote nothing, because a phone's config is
generated per request and never lands in `/etc/asterisk` (D79). An apply button that sometimes
means nothing is worse than no apply button.

So each key in `SettingsKeys` carries a `SettingScope`, classified by the code that actually reads
it rather than by the prefix it happens to start with:

- **Asterisk** — something in `ConfigApplier.Render` writes it into a conf file, or the applier
  needs it to work: `Asterisk.ConfDirectory`, `Ami.Host/Port/Username/Secret` (manager.conf), every
  `Sip.*` key (pjsip.conf and rtp.conf), and `System.Timezone`, which every `GotoIfTime` in the
  generated dialplan names (D74).
- **Phones** — only a phone's generated config carries it: `Provisioning.Username/Password` (the
  gate), `Provisioning.AdminPassword/UserPassword` (D84) and `System.NtpServer`.
- **App** — nothing outside this process reads it: `Ami.TimeoutSeconds` (how long our own client
  waits; no file carries it), and `Cert.AcmeServer`, `Cert.AcmeAccountKeyPem` and `Cert.Email`,
  which the certificate service reads when it orders.

`SettingsRepository.Set`/`Delete` raise the marker only for an Asterisk-scoped key, so the rule
holds however a setting was edited — the Settings page, the SIP page, or the Polycom and Yealink
tabs on the Phones page, which all post to the same handler. The settings page's 204 sends
`configChanged` only in that case too, and a phone-scoped save says so in its toast instead:
"each phone picks this up at its next poll". The edit form says which before you save.

Two deliberate calls. `System.Timezone` is read by both the dialplan and a Polycom config, and
Asterisk wins, because that half is the half that needs an apply. And `Cert.*` is App, not
Asterisk, even though a certificate ends up in a conf file: what an order produces is a row in
`Certificates`, and `CertificateRepository` raises the marker itself (D101), so the file Asterisk
reads is still applied — the ordering details themselves reach no renderer. An unclassified key
falls back to Asterisk, and a test says there is no such key: an apply that writes nothing costs a
click, while a missed one leaves Asterisk running config nobody was told had changed.

### D104. The app restarts Asterisk itself, after asking, and keeps saying so until it happens (2026-09-23)
D33 left the restart manual: some files are only read when Asterisk starts, so an apply writes
them and reports that a restart is owed. The reason it stayed manual was that a restart drops live
calls, not that the app could not do it — the installer has written the polkit rule since piece 21
(D95), scoped to exactly `asterisk.service` for exactly the `tnpbx` user.

So the app does it, and asks first. An apply that wrote a startup-only file answers with
`RestartRequired` and the file names, and the page asks with a sweetalert2 confirm — "Asterisk
needs a restart to load: modules.conf. Calls in progress will be dropped." Confirming posts to
`POST /api/config/restartAsterisk`, which runs `systemctl restart asterisk.service` as the web
user: `Process.Start` with a fixed program and a fixed argv list, **no sudo and no shell** (D3,
D18), a 30-second timeout, INFO on success and ERROR with what systemd said on failure. A failure
comes back as a 502 carrying our own sentence, because it is the admin who has to do something
about it.

Declining is a normal answer, so the state is kept rather than forgotten: `AsteriskRestartMarker`
is a file beside the database and the apply marker (D26), raised by an apply that owes a restart
and cleared by a restart that worked. The navbar poll that already asks about the apply button
(D43) asks about this too, and shows an "Asterisk restart required" badge with a Restart button
beside it — same endpoint, same confirm. No new page and no new nav: a restart being owed is a
property of the whole system, like the apply button next to it.

### D106. The Status page is the home page (2026-09-19)
Signing in lands on `/`, which is now what this PBX is doing right now rather than the ASP.NET
template's welcome text. An admin opens a PBX either to find out whether it is working or to
change something, so the page is a triage screen, not a dashboard: FreePBX's home page of CPU
gauges, call-volume graphs and module nags is the surface-area problem this project exists to
avoid.

Two halves on two clocks. The live half polls every five seconds over **one** AMI connection
asking four questions — `PJSIPShowContacts`, `PJSIPShowRegistrationsOutbound`,
`CoreShowChannels`, `CoreStatus` — and renders six tiles (Asterisk and its uptime, trunks
registered, extensions registered, calls in progress, certificate days left, config
applied / pending / restart owed) and the calls in progress as a plain table: one row per
bridge, the older channel taken as the caller, and not bootstrap-table because markup replaced
every five seconds cannot keep a sort. `CoreShowChannels` and `CoreStatus` are covered by the AMI
account's existing `read = system`, so `manager.conf` is unchanged (D32).

The other half reads the database on load and on `configChanged` / `configApplied` and lists
what needs attention: AMI not answering, a restart owed or an apply pending, a trunk rejected or
unregistered, a trunk nothing routes over, a stored destination the catalog can no longer find
(D35 — the gap the roadmap has noted since piece 11), a phone with no extension or one pointed at
a disabled extension, no usable certificate or one inside fourteen days of expiry, a failed
order, a disk at 80 / 90 %, and time conditions running on UTC because no zone is set.
`AttentionRules` is a pure function over a `StatusSnapshot` in `Techie.Pbx.Asterisk`, tested rule
by rule; a finding names a `FindingSubject`, and only the Web project maps a subject to a page.
Below the list, counts of everything as links.

Deliberately left out: graphs, CPU and memory, call history (F5, which needs storage this page
does not have) and security events (piece 20). Everything shown is either true right now or a
row in a table that already exists.

### D107. Logs page: three named logs, tailed from the end (2026-09-19)
`/Status/Logs` shows Asterisk's `messages.log` and `security.log` and our own log4net file,
read-only. The browser never sends a path: it sends one of three fixed names
(`asterisk-messages`, `asterisk-security`, `app`), `LogSources.Find` is the only code that turns
a name into a file, and anything else is a 400. `LogTail` seeks to the last 4 MB rather than
reading the file, so a poll against a large `messages.log` costs one seek; it applies the filter
and returns the last 100 / 250 / 1000 lines. A missing file, an unreadable one and an empty tail
are three different sentences, never an exception. `Asterisk.LogDirectory` (App scope, D103,
default `/var/log/asterisk`) says where Asterisk writes; the app log's path is read from the live
log4net `FileAppender`, so it cannot drift from `log4net.config`. "Follow" is htmx's conditional
`every 5s [...]` trigger, and the only JavaScript is a scroll to the bottom after each swap.
Asterisk creates its log files 0644 (checked on the lab VM), so `tnpbx` reads them as a group
member with no change to either unit; if a site's Asterisk ever creates them tighter, the fix is
a `UMask=` line in the Asterisk unit, not a privilege.

### D108. MWI: mailboxes on the endpoint, and the two modules that make SUBSCRIBE work (2026-09-19)
Found live by Claude on the lab VM: the phones (Polycom `msg.mwi.1.subscribe`, Yealink
`subscribe_mwi_to_vm`, both written by the piece-22 provisioning config) SUBSCRIBE to
message-summary and the generated `modules.conf` allowlist had no `res_pjsip_mwi` — every
subscribe failed into the log. The module set the installer builds is stock, so this is a
config change, not a build change: **no installer edit was needed**.

Two things were added:

- **`res_pjsip_mwi.so` and `res_pjsip_mwi_body_generator.so` to the allowlist** (D31). The
  first answers the SUBSCRIBE, the second writes the message-summary body. Deliberately not
  added: `res_mwi_devstate.so`, which is for exposing MWI as device state (BLF hints); no
  feature here uses hints.
- **`mailboxes = <number>@default` on every PJSIP endpoint whose extension has voicemail on**,
  in `VoicemailConfRenderer.MailboxContext` (D29's `default`). With the mailbox named on the
  endpoint, Asterisk sends MWI NOTIFYs to subscribed phones when a message arrives or is heard
  (`*97` clears the light too). Endpoints without voicemail get no line, so the file for a
  voicemail-less system is unchanged.

Subscription question from the roadmap (piece 13) resolved implicitly: phones subscribe on
their own — the provisioning config already tells them to — and Asterisk accepting the
SUBSCRIBE is the whole of the server-side decision. No per-endpoint `mwi_subscribe_replaces`,
no polling.

Consequences worth naming: an apply that toggles voicemail now rewrites `pjsip.conf` as well
(`ConfigApplierTests` updated), and `modules.conf` changes mean an **Asterisk restart** is
required (D33) — piece 24's restart offer handles it. Lab VM verified: the two modules load
clean, `voicemail show users` unchanged.

### D109. Route patterns are stored with their underscore, and routes gain prepend and strip digits (2026-09-19)
User request, closing the question D44 deliberately left open. Three changes, one piece:

- **The underscore is added, not demanded.** A pattern without its leading `_` is what a
  caller means, not an error: `OutboundRoute.NormalizePattern` puts it on, and the repository
  calls it on every save, so `NXXXXXX` is stored as `_NXXXXXX`. The form says so in its hint.
  A pattern a human could not have meant — bad characters, a leading 0 — is still refused as
  before (D46, D47).
- **`PrependDigits`** (schema `014_outbound_route_digits.sql`): digits written in front of the
  number before it reaches the trunk. Seven dialled digits, home area code on the front:
  `_NXXXXXX` + prepend `1714` sends `1714XXXXXXX`. Ten dialled digits, long-distance one:
  `_714XXXXXXX` + prepend `1` sends eleven.
- **`StripDigits`**: how many leading dialled digits are dropped first — "dial 9 for an
  outside line" is `_9NXXXXXXXXX` with strip 1. Both are per-route, and both empty means the
  dialplan is exactly what it was before this change (the golden files other than the new one
  did not move).

The renderer sends `prepend${EXTEN:strip}` in the trunk's dial string, and the routes table
shows the number the trunk will get (e.g. `_NXXXXXX → 1714${EXTEN}`) so the transform is
visible without opening the edit form.

The international guard extends to the new door (D47): a prepend may not start with `0`,
because `00`/`011` prepended is the same bill as a pattern starting with 0 — the guard was
otherwise walked around from the other side. This is the same North-American assumption D47
already names; a site that needs a national 0-prefix is the same escape-hatch question.

### D110. Max contacts per extension: an office phone and a softphone on one number (2026-09-19)
User request, from FreePBX's max contacts field. The renderer hardcoded `max_contacts = 1`
with `remove_existing = yes` on every aor, so a second device registering displaced the
first — the softphone would knock the desk phone offline.

`Extensions.MaxContacts` (schema `015_extension_max_contacts.sql`, default 1) is now the aor's
`max_contacts`, capped at 5. With more than one, `remove_existing` flips to **no**: it exists
to replace a device's own stale contact, but with several devices it deletes every *other*
contact on each new REGISTER — the opposite of coexisting. Pruning of dead contacts falls to
registration expiry instead. A call to the extension rings every registered contact; that is
how PJSIP dials an endpoint, so no dialplan change is involved.

Default 1 keeps every existing endpoint byte-identical (golden pjsip files unchanged), and
the value is validated in the model, the repository and the renderer like every other field
that reaches a conf file.

### D111. Max contacts is one global setting, not per extension (2026-09-19)
User decision the same day, superseding D110's per-extension column. `Sip.MaxContacts`
(default 1, capped 5) is a Settings key read by `AsteriskSettings.Transport` into
`PjsipTransport.MaxContacts`, and every aor in pjsip.conf carries it. Same rule for
`remove_existing`: on with one contact, off with several, so an office phone and a laptop
softphone coexist instead of displacing each other; a call rings every registered contact.

Everything per-extension about D110 is gone: `Extensions.MaxContacts` (added by schema 015
only minutes before, and already run on the lab VM) is dropped by schema `016`, which a fresh
install runs 015+016 and ends with neither. Per-extension max contacts is a later piece if a
site ever genuinely needs it.

### D112. Secrets show in the edit form; the table keeps the dots (2026-09-19)
User request after copying an extension password out of a sweetalert box proved painful.
Supersedes D68's "a secret's value never reaches the page" for these two screens:

- Extensions: the SIP password is a normal editable field in the edit modal, for new and
  existing extensions alike. Blank on save keeps the current password; Regenerate still makes
  a new one and drops it into the open form. The "show password" button and its API endpoint
  are gone.
- Settings: the edit form shows the stored value in the clear; the table still shows
  SettingRow.Mask dots. Blank now clears to the default like every other setting — the value
  is in the box, so clearing it is deliberate. The "show stored value" button, settings.js and
  the settings secret API are gone.

Tables still never carry a secret (SettingRow masks), phone config files still never carry one,
and conf-file secrets are untouched. The threat model shifts from "never in the browser" to
"behind the admin login" for these two admin-only forms, which is where it belongs for a PBX
whose passwords have to be readable to be typed into a phone.

### D113. Dark mode is chosen in the browser and nowhere else (2026-09-19)
User request. Bootstrap 5.3 themes itself from `data-bs-theme` on `<html>`, so that is the whole
mechanism: a sun/moon button in the navbar writes `light` or `dark` into `localStorage` under
`pbx-theme`, and a short inline script in the head of `_Layout` applies it before the first paint.
With nothing stored the operating system decides, and goes on deciding while the page is open.

No setting, no column, no round trip. The choice belongs to the person at the keyboard rather than
to the PBX, and two admins sharing one box should be free to disagree about it.

The markup that changed is colour only. `navbar-light bg-white` and the `text-dark` nav links
pinned the bar to the light palette, the log view was `bg-light`, the unknown registration badge
was `text-bg-light` (a fixed near white in *both* themes, so on a dark page the one badge meaning
"no answer" was the brightest thing in the table), and the layout CSS still carried the ASP.NET
template's `#0077cc` links and `#e5e5e5` borders. All of them follow Bootstrap's variables now.
D112's sibling, the placeholder rule from the day before, keeps its idea and inverts its
direction: gray-400 on light, gray-600 on dark, so a hint never reads as a typed value either way.

Two libraries needed help. sweetalert2 has themes of its own and follows the operating system by
default, which is not the same answer as the button, so it is re-mixed with `theme` on each
toggle; bootstrap-table 1.24 paints its loading overlay white, which one rule in site.css covers.

### D114. Email alert template as an embedded resource (2026-09-19)
A single generic HTML template, Templates/AlertEmail.html in Techie.Pbx.Core, embedded as
`templates/AlertEmail.html` alongside the schema scripts' `schema/NNN_name.sql` — so it
publishes with the build and needs nothing from disk at runtime.

Email-client HTML rules it follows: inline styles only (head CSS is stripped by clients),
table-based layout, fixed 600px card with max-width for phones, no external images, and a
plain wrapper background so it renders sanely in dark-mode clients too.

Placeholders are `{{Subject}}`, `{{Timestamp}}`, `{{Body}}`, `{{Hostname}}`,
`{{AccentColor}}` (severity color for the title bar and button), plus two optional
drop-blocks: `{{DetailBlockStart}}..{{DetailBlockEnd}}` (key/value table rows via
`{{DetailRows}}`) and `{{ButtonBlockStart}}..{{ButtonBlockEnd}}` (CTA via `{{ButtonUrl}}` /
`{{ButtonText}}`). The mail sender removes whole blocks when empty. Tests pin the resource
and its placeholders.

### D115. Mail settings, and the transports behind them (2026-09-19)
User request, and the answer to F4's open question: **both**, chosen per site by a `Mail.Transport`
setting, and sent **by this app** rather than by Asterisk.

- `graph` — Microsoft Graph, using this application's own Entra app registration. Clients are
  already on Microsoft 365 via Entra, so there is no second credential to create: the client
  credentials flow gets an app-only token and one POST goes to
  `/v1.0/users/{Mail.FromAddress}/sendMail`. Two HTTP calls with `HttpClient`, no SDK — half of
  MSAL for two requests is surface area this project is trying not to have.
- `smtp` — submission to a relay with `System.Net.Mail.SmtpClient`, STARTTLS always. No new
  package: it sends one small message to a relay that does the real work, which is the job it is
  still good at.
- Blank is a third state meaning "decide for me": Graph where this server has an Entra client
  secret, and **no mail at all** where it has not. It never falls back to SMTP, because a relay
  nobody configured is not a fallback.

Seven keys, all `SettingScope.App` — no generated conf file carries any of them, so none of them
is an apply. `Mail.Smtp.Password` joins the secret list (the table masks it, the edit form shows
it, D112).

**Graph needs two things no code here can arrange**, and says so plainly when either is missing:
the app registration needs the `Mail.Send` **application** permission with admin consent, and
`AzureAd:ClientSecret` has to be configured on the server. That secret is not and will not be in
this repository, so an unconfigured machine gets `GraphCredential.IsComplete == false` and Graph
reports itself unconfigured rather than failing somewhere out on the network.

**Voicemail to email is deliberately not wired to any of this.** `app_voicemail` sends its own mail
through a local MTA, and `VoicemailConfRenderer` still writes no `serveremail`, `fromstring` or
template — so voicemail email remains exactly as F2a left it. Routing it through these settings
means either running an MTA configured to relay through them, or taking delivery away from Asterisk
entirely (`externnotify`, or watching the mailbox directories). That is a piece of its own, and it
is still open.

The only thing that sends mail today is the **Send test mail** button on the new Email tab. It
sends one message over whatever the settings resolve to, rendered with the D114 template, so the
test shows what a real alert will look like when it lands. Nothing else sends anything yet, and the
transports were deliberately not built out further than that button can honestly exercise.

**Menu and pages.** The Settings dropdown becomes System / SIP settings / *All Settings*, the flat
list being the way to reach a key no grouped page shows yet. `/Settings/System` has two Bootstrap
tabs over the shared `_Sections` partial (renamed from `_SipSections`; the SIP page uses it too).
Rows still open the general settings page's edit form in the shared modal, so a setting is saved,
validated and logged in exactly one place however an admin got to it.

Main tab: hostname and timezone — both already existed, as `System.Hostname` and `System.Timezone`,
so nothing was duplicated. The **listening ports are shown read-only**, because they are not a
setting and never were: `WebBindings` derives them from whether there is a usable certificate, 80
has to stay open for ACME and 8080 is the way back in (D99). An editable "listening port" box would
be a lie, because nothing reads one. Changing them stays a code change with a decision behind it.

### D116. A W3C web request log, for debugging phone provisioning (2026-09-19)
When a desk phone does not come up, the first question is not an Asterisk question: **did the phone
even reach us, on what path, and what did we answer?** The app log answers that only for requests
that got far enough into a controller to log something — a phone that never arrived, or that was
turned away by the HTTPS redirect, or that asked for a file name we do not generate, leaves either
nothing or one line with no context around it. So Kestrel keeps a request log of its own.

**W3CLogger, from `Microsoft.AspNetCore.HttpLogging`** (`AddW3CLogging` / `UseW3CLogging`). It is in
the shared framework, so there is no new package — which is most of the reason to prefer it to
writing our own middleware. Fields chosen to be as near an Apache combined log as it goes: `date`,
`time`, `c-ip`, `cs-username`, `s-port`, `cs-method`, `cs-uri-stem`, `cs-uri-query`, `sc-status`,
`time-taken`, `cs-version`, `cs-host`, `cs(User-Agent)`, `cs(Referer)`.

Two deliberate differences from combined:

- **No `sc-bytes`.** ASP.NET Core's W3C logger has no bytes-sent field at all. It is the one column
  that cannot be had without writing the middleware ourselves, and response size is not a question
  this log exists to answer.
- **No `cs(Cookie)`**, which the logger does offer. Our cookie *is* the Entra session, so a log of
  cookies is a log of credentials (security.md: never log secrets). `s-port` is logged instead, and
  earns its place here: it says whether a phone arrived on 80, 443 or 8080.

**Where it goes: `logs/requests/` inside the install**, beside log4net's own `logs/tnpbx-web.log`.
Not `/var/log` — the hardened unit runs `ProtectSystem=strict` with
`ReadWritePaths=/opt/tnpbx /etc/asterisk …`, so `/var/log` is read-only to this process and is not
ours anyway. **No change to `app-deploy.sh` is needed**: `/opt/tnpbx` is already writable, and the
logs are wiped by a re-deploy exactly as the application log is (only `appsettings.json` and `Data/`
survive, D95). Ten files of ten megabytes is the ceiling; the logger starts a new file each day and
whenever the current one fills.

**It is first in the pipeline**, before the exception handler and the HTTPS redirect, so one line is
written for every request whatever becomes of it — a 307 to HTTPS, a 401 from Entra, a 500 the error
page produced. That also means it **wraps authentication rather than sitting behind it**, which is
the whole point for provisioning: `/polycom` and `/yealink` are `[AllowAnonymous]` and a phone has no
session, so a log that lived behind the cookie would be blind to exactly the traffic this is for.

**`cs-username` for phones.** Provisioning Basic credentials are checked inside the controllers, not
by an authentication handler, so nothing would otherwise name those requests.
`RequestLogUserMiddleware` reads the username half of the Basic header — never the password — and
puts it on `HttpContext.User` for the two provisioning paths only. The identity is built **with no
authentication type**, so `IsAuthenticated` stays false: it is a label on a log line and can never
stand in for signing in, and the controllers still do the real check themselves. It runs after
`UseAuthentication` (which replaces the user whenever a scheme returns one) and before
`UseAuthorization` (which ignores an unauthenticated identity).

**On or off: the `Web.RequestLog` setting**, `SettingScope.App`, on by default. On by default because
a request log is only worth having if it was already running when the thing you are trying to explain
happened — a phone that failed at 3am cannot be asked to fail again. It is read **once, at startup**:
the logger is middleware, so off means it is never added to the pipeline rather than added and asked
to do nothing. **Changing it needs `systemctl restart tnpbx-web`**, and the setting's own description,
the System page's section and the Logs page all say so. The scope stays `App` rather than `Asterisk`
because no generated conf file carries it and an apply would write nothing.

This is the first **toggle** setting, so `Toggles` (`on` / `off`) joins `MailTransports` and
`AcmeServers` as a fixed list of values a key may hold — which means the settings form already knows
how to render it, as a dropdown rather than a new kind of control (D75). Blank still means "not set",
which for this key means the default: on.

**Reading it: the Logs page**, as a fourth source in the same fixed allowlist (D106, D107) — the
browser still posts a name, never a path, and `LogSources` is still the only place a name turns into
a file. The one new wrinkle is that W3CLogger names its own files (`tnpbx-requests-<date>.<n>.txt`),
so the source resolves to the most recently written file with that prefix rather than to a fixed
name. Only the newest one, the same bargain the application log makes: the page tails what is being
written now, and anything older is on the box for whoever wants to go and look.

### D117. g722 and slin join the offered codecs (2026-09-19)
codec_g722.so and codec_slin.so are loaded, and the Sip.Codecs setting accepts g722 and slin.
Yealink provisioning now writes G722 (payload 9) when it is chosen; Yealink has no slin, so
that stays a server-side codec only. Defaults are unchanged — an existing install keeps offering
ulaw,alaw until the administrator edits the setting.

### D117 follow-up: prompts and voicemail stay wideband (2026-09-19)
`make install` ships the core prompts in GSM only, so G.722 calls heard prompts transcoded up
from GSM. The installer (and the lab build script) now also extract the official
asterisk-core-sounds-en-g722 tarball into /var/lib/asterisk/sounds/en, and voicemail.conf
records `format = g722|wav49` so messages keep their width for wideband phones while the wav49
copy remains the universally playable one. Verified live: a G.722 INVITE to *97 answered
payload 9 and played vm-youhave.g722 natively.

### D118. Phone push stays same-LAN only, by design (2026-09-20)
Reboot/config push goes to the phone's web UI at its LastIP, which for remote phones is the
NAT public address (stored in IPv6-mapped form, `::ffff:1.2.3.4`). Accepted as-is: deployments
are expected to be cloud PBXes where phones are never IP-reachable from the server anyway.
Remote phones pick up changes at their next poll; the reboot's SIP NOTIFY path remains the
LAN-capable alternative. The 500 on a mapped-form address is a known cosmetic edge, not fixed.

> **Amended by D123**: rebooting no longer goes this way at all — it is the SIP NOTIFY named here
> as the alternative, which rides the registration and so reaches a remote phone. Only the config
> reload on save is still an HTTP push, only for Polycom, and it is still same-LAN only.

### D119. Call parking, and music on hold with it (2026-09-19)

Call parking is one lot, off by default, and five settings — all `SettingScope.Asterisk`, because
every one of them lands in a generated conf file.

- **Park with a DTMF feature code**, `Parking.DtmfCode`, default `*3`, shape `*` + 1–2 digits. It
  is the `parkcall` entry of a new generated `features.conf` — Asterisk's own name for it; `park`
  is the application, `parkcall` is the feature — and its built-in default is empty, so a file
  without that line is a system where no DTMF parks anything.
- **Every generated `Dial()` now carries `tTkK`.** The featuremap is opt-in per Dial and per side:
  `k`/`K` are what let the called/calling party park, and without them the feature code is a line
  in a file nothing consults. `t`/`T` come with it for transfers, which also turns on Asterisk's
  default blind-transfer digit `#` — accepted, and written down here because nothing in the
  generated config says it.
- **Slots 1..N**, `Parking.Slots`, 1–9, default 9. Retrieval is dialling the slot number from any
  phone. Safe because extensions are three digits or more and feature codes are star-prefixed;
  the cap of 9 is the whole reason it is safe, so it is a validated bound rather than a guideline.
- **The slot entries are generated, not Asterisk's.** The slot retrieval routes live in
  `extensions.conf` as one `exten => <n>,1,ParkedCall(default,<n>)` per slot, per the same rule
  as everywhere else here: only numbers we wrote can be dialled (D12, D46, D60), and an
  `include` of a context we do not control is not that. The lot does carry a `parkext` (700),
  which exists only inside the lot's private `parkedcalls` context so no phone can dial it: the
  DTMF park feature parks by blind-transferring the peer into that extension, so a lot without
  one has a feature code that fires and parks nothing — found by E2E on the lab with a real
  client (baresip) pressing *3 during a live call.
- **The PBX speaks the slot number to the parker.** No dialplan work: `res_parking` does it with
  `ast_say_digits` on the parker's channel, and the core digit sounds are already installed in
  GSM and G.722 (D117).
- **Timeout is `Parking.Timeout`, 30–600 seconds, default 60**, written as `parkingtime`, with
  `comebacktoorigin = yes` and `comebackdialtime = 30`. The call goes back to the phone that
  parked it — the one answer that needs no destination picker. `res_parking` creates the
  `park-dial` context for that itself.
- **`findslot => first`**, so parking twice in a row gives slot 1 then slot 2.
- **The file is `res_parking.conf`, not `parking.conf`.** Parking moved out of `features.conf`
  into its own module in Asterisk 12 and the file moved with it.
- **Parked audio is `Parking.Audio`: `silence` (default) or `moh`.** Asterisk has no "silence"
  option for a parked call — the parkee joins a holding bridge whose idle mode is always music on
  hold — so silence is implemented as *having no class to start*: no `parkedmusicclass`, and a
  generated `musiconhold.conf` that never defines a class called `default`. `bridge_holding` falls
  back to a silence generator when `ast_moh_start` fails, which is the whole mechanism.
- **The music on hold class is called `parking`, deliberately not `default`.** Asterisk falls back
  to a class named `default` whenever music is asked for and none was named, so a class by that
  name would be played to a parked caller whose setting says silence. `preferchannelclass = no`
  in `[general]` for the mirror-image reason: a PJSIP endpoint suggests `default` without being
  asked, and the lot's class has to win.
- **One class, files uploaded by the admin, no per-class UI.** `mode = files` on
  `/var/lib/asterisk/moh` with `sort = alpha`. Asterisk plays the directory rather than a list, so
  the rows are written into the file as comments — there to be read next to an `ls`, not obeyed.
  New table `MohFiles` (017), the announcements upload/convert pattern (D55) with one flat
  directory: the stored name is `<MohFileID>-<slug>.g722`, ID first because two names that slug the
  same way would otherwise be one file.
- **Three modules join the allowlist**: `res_parking.so`, `res_musiconhold.so` and
  `bridge_holding.so` — the last being the one that is easy to forget and impossible to work
  without, since a parked call lives in a holding bridge. Unconditional, so turning parking on and
  off is a reload rather than a restart; the modules.conf change itself needs the usual restart
  (D33).
- **A dedicated `/Parking` page** carries the settings and the music on hold table, rather than
  five more rows on the flat settings list. `_Sections.cshtml` moved to `Pages/Shared` so a page
  that is not one of the settings pages can use it.

### D119 additions: attended transfer code, and starter music (2026-09-20)
`features.conf` now maps `atxfer => *2` (always; blind transfer `#` and the phone transfer
buttons were already covered by `tT` and `allow_transfer`). The installer also drops the
free-licensed asterisk-moh-opsound tracks into `/var/lib/asterisk/moh`, so Parking.Audio = moh
has something to play before the admin uploads anything — the class scans the directory, so the
files need no database row.

### D120. "Connectivity" in the navbar, and a printable cheat sheet under it (2026-09-20)

**The top level of the navbar is now four menus: Connectivity, Call Handling, Settings, Status.**
Extensions, Phones and Trunks used to sit flat beside the two dropdowns, which made the bar a
mixture of things you click and things that open — and gave no answer to where a new page of that
kind would go. The word is the user's: **Connectivity** is what a call arrives on or leaves by —
the extension, the handset it rings, the trunk to the outside — as against **Call Handling**,
which is what happens to a call once it is here. The two dropdowns now divide on a line an admin
can state, and nothing had to move on disk to do it.

**Certificates moved into Settings**, as a menu item only; the page is still `/Certificates`. A
certificate is part of how the box presents itself, not part of a call, and it is touched roughly
as often as the other settings pages are.

**Status is last.** It is still the home page and still what the logo leads to (D106); it is where
you go when something is wrong, which is not where a menu bar should start.

**The cheat sheet is `/Connectivity/CheatSheet`**, at the bottom of that menu under a divider. It
is the one page here written for somebody who will never log in: the extension list and the codes
a handset can dial, printed and pinned up next to the phones. It sits under Connectivity rather
than in Status or a menu of its own because it is the printout of the three pages above it — the
divider says it is not another thing to administer.

- **No new schema, no new settings, no new permission.** The page is read-only and any signed-in
  user can open it. Everything on it is either a row that already exists or a code derived from
  what the renderers generate. No secrets: the extension list is projected into a two-field row
  type rather than handed the `Extension` model, which carries SIP passwords and voicemail PINs.
- **The codes come from `FeatureCodes.All(parking, voicemailInUse)`**, a pure static function in
  `Techie.Pbx.Asterisk.Config`, spelled from the same constants the renderers write: `*2` and `#`
  from `FeaturesConfRenderer`, `*43` and `*97` from `ExtensionsConfRenderer`, the park code and
  the slot range from `ParkingSettings`. A conditional code is printed only when it is really
  generated — no park code and no retrieval slots with parking off, no `*97` when nobody has a
  mailbox — and a test renders both files and asserts that every code on the sheet is in them, so
  the printout cannot drift away from the dialplan. Unlike a renderer it does not validate what it
  is given: nothing here is written to a conf file, and one bad settings row must not be able to
  take a read-only page down.
- **`#` is printed even though we never write it.** It is Asterisk's own default for `blindxfer`
  and the `t`/`T` in every generated `Dial` turns it on (D119). It is real, and it is easy to
  press by accident, which is its own reason for saying so on the wall.
- **The slots are one row, not nine.** They are consecutive single digits by construction, and the
  row carries the thing that is otherwise a support call: a slot number is one digit, so press
  dial or `#` straight after it rather than waiting for the phone to decide you have finished.
- **Printing is Bootstrap's `d-print-none` plus one `@media print` block in `site.css`.** The
  layout's navbar and footer carry the class, so nothing but the sheet prints, on this page and on
  every other. The block is not page-specific either: it sets a 15mm `@page` margin and — the part
  that matters — pushes the handful of Bootstrap variables that decide text and border colour back
  to their light values, because browsers drop background colours when printing but keep
  foreground ones, so a dark-mode page would otherwise print near-white text on white paper.
- **One column, `table-sm`, no page-splitting cleverness.** Two columns would hold more, but the
  grid's breakpoints are not reliable in print; a single compact column fits a small office on one
  sheet and is the same on paper as it is on screen.
- **Ring group, announcement, IVR and time-condition numbers are deliberately not on it.** They
  are dialable, but they are not what a wall sheet is for, and the page has to stay one page. Ask
  before adding them.

### D120 addition: directed call pickup, *8 (2026-09-20)
Dialling *8 followed by the ringing extension (*8100) takes the call, the FreePBX convention.
The pattern `_*8.` demands a digit after the code, so bare *8 matches nothing — this system
has no pickup groups, only the directed form, and a code that does nothing is worse than none.
The `pickupexten` default in features.conf is not used: Pickup() is reached through a
generated pattern, like every other dialable number here. On the cheat sheet under
"From your phone".

### D35 amendment: an inbound route can also go to an IVR or a time condition (2026-09-20)
The destinations an inbound route offers are now **Extension, Voicemail, Hangup, RingGroup, Ivr
and TimeCondition** — the whole of `DestinationCatalog`, with nothing held back.

Why: the two things a caller from outside meets first are the clock and the menu. "Ring the front
desk at 3am" and "put every caller through to one phone" are both the wrong default, and
business-hours routing (D63) and an auto attendant (D59) are exactly what fix them. A trunk
landing straight on a single extension was only ever the simplest case, not the common one.

Nothing in the model, the database or the dialplan had to change for it, which is the point of
D35 and D36: `InboundRoutes.DestinationType` is a plain `TEXT` column with no `CHECK` on it, so
there is **no schema script** in this change; `Destination.Validate` has known `Ivr` and
`TimeCondition` since D59 and D63; `InboundRouteRepository` already validated a saved route
against the full catalog; and `DestinationDialplan` already renders both as
`Goto(internal,<play extension>,1)` — the same door an internal caller uses, so a menu or an
open/closed check behaves identically whether the call came from a desk phone or a trunk.

What was actually behind was one page. `Pages/Inbound/Index.cshtml.cs` still called the one- and
four-argument `DestinationCatalog` overloads, so the picker offered extensions and mailboxes
only, and a route pointed at an IVR another way would have read "(gone)" in the table. Both calls
now pass every source. The golden `extensions-inbound.conf` gains a route to IVR 500 and one to
time condition 600, and a test renders those routes together with the IVR and time-condition rows
to prove the `Goto` lands on the entry those renderers really write.

### D121. Phone buttons: eight keys, and the hints their lamps watch (2026-09-20)
A phone's edit modal now has two tabs — **Buttons** first, because it is what an admin comes back
to, and **Details** second with everything the form had before. The Buttons tab is eight
dropdowns, "Key 1" to "Key 8", each offering nothing, any extension, or a parking slot.

> **Amended 2026-09-21 (schema 020): a phone's registration is one of its keys.** The user
> configured all eight keys on a Poly Edge 450 and the handset showed **nine** lines: the Details
> tab's Extension dropdown took line key 1 on top of whatever the Buttons tab said. Two places
> decided what was on the handset and neither knew about the other. So:
>
> - **`Phones.ExtensionID` is gone.** What a phone registers as is its **line key**, and
>   `PhoneButton.LineNumber` is what asks. `PhoneButtonRepository.GetLines` answers it for every
>   phone at once, which is what the phones table and the status page use.
> - **The kinds are now `Line`, `Blf` and `ParkingSlot`**, with `CallFlowControl` still the
>   reserved fourth. `Line` is this phone's own registration; `Blf` is what `Extension` was, a lamp
>   on somebody else's extension — the Buttons tab calls that group **User list**. The migration
>   renames every `Extension` row to `Blf`, inserts the phone's old extension as a `Line` at key 1
>   and shifts the rest down one. **A phone with all eight keys full loses its last key**: eight
>   keys with a line on one of them is seven lamps, which is the fact about the handset this whole
>   change is about.
> - **A phone must have a line, and lines lead.** `PhoneButton.ValidateSet` refuses a set with no
>   line, a set whose first key is not one, and a line sitting after a lamp — a handset puts its
>   own lines on its leading keys whatever we store, so a set that disagrees would not be the set
>   the admin saved. Several lines are allowed and become `reg.2`, `account.2` and so on. There is
>   no "unassign" any more: a phone nobody needs is disabled or deleted.
> - **Two phones still cannot register as one extension.** The old rule about `ExtensionID`
>   followed the registration onto the key. A *lamp* on an extension another phone registers as is
>   fine, and common — that is what a BLF is for.
> - **No line, no keys.** `PhoneButton.Usable` returns nothing at all once the line has been
>   switched off or deleted: the lamps subscribe and dial on the registration, so without one there
>   is nothing for them to be on.
> - **Polycom** writes one `reg.N` per line key in key order, each with `lineKeys="1"`, and the
>   remaining keys as the attendant resource list exactly as below. **Yealink** writes one
>   `account.N` per line key and a `linekey.N.type = 15` (Line) for each, with the lamps as
>   `linekey.N.type = 16` (BLF) on account 1. **This changes the BLF type from the 15 recorded
>   below**: Yealink's own line-key table has 15 = Line and 16 = BLF, and the two must differ now
>   that both are written, or a phone would show a row of lines where it was meant to show one line
>   and some lamps. Unverified on a real handset (D92) — if a Yealink says otherwise it is one
>   constant in `YealinkConfigRenderer`.

> **Amended again 2026-09-21: a key may be left blank, and key 6 is key 6.** The user set up eight
> keys on the Poly Edge 450 with key 5 deliberately blank — two groups of lamps with a gap between
> them, which FreePBX allowed — and the handset showed the keys below the gap one place higher than
> the form did. So:
>
> - **Blanks are allowed anywhere but the first key.** `PhoneButton.ValidateSet` now says: every key
>   valid, no two in the same place, and **the lines are keys 1..n with no gap in them** — a phone
>   signs in on its leading keys whatever we store, so a registration further down would sit
>   somewhere else on the handset than on the form. Every other key may be left blank, wherever the
>   admin wants the gap. Nothing about this was ever a database rule: a blank key has always been
>   stored as no row at all.
> - **Polycom's resource list is written by position, not packed.** This replaces the numbering
>   recorded below. A resource's index is its key's position less the number of registrations —
>   the registrations take the leading line keys, so with one line, key 4 is
>   `attendant.resourceList.3` — and **every index from 1 to the last assigned key is written**. A
>   blank key becomes `attendant.resourceList.N.address=""` with no label and no type, which the
>   phone leaves unassigned. It cannot simply be skipped: a phone reads the list until the first
>   index it does not find, so a missing index would hide every key after it. That is the whole
>   reason the list was packed 1..n before, and an empty resource is what keeps both facts true at
>   once.
> - **Yealink needed no change**, and the test that says so is the point: a Yealink line key is
>   addressed by its own number, so key 6 is `linekey.6` and a blank key is simply not written,
>   which leaves the handset's default on it. Sparse was already right there.
> - **The consequence recorded below — "clearing one moves the rest up a key" — is gone.** Clearing
>   a key now clears that key and nothing else.

- **Eight keys, and eight is ours rather than the handset's.** A VVX 310 has six line keys and a
  VVX 410 has twelve; the form offers eight because it is a number an admin can fill in without
  scrolling, not because any phone has exactly that many. A key beyond what the handset has is
  simply not shown on it. Asking the phone how many keys it has means knowing every model, which
  is a feature to justify on its own.
- **Two kinds today, and a third that is not built.** `PhoneButtons.TargetType` is `'Extension'`
  or `'ParkingSlot'`, stored as plain `TEXT` with **no `CHECK`** on it — the same shape
  `InboundRoutes.DestinationType` has (D35). Call flow control is the kind this was designed
  around: when it is built it is a constant in `PhoneButtonTarget`, a case in
  `PhoneButton.Validate` and a label, and **no schema script**.
- **A key is a reference, never a copy.** It stores the extension number or the slot number, so a
  renamed extension relabels every key that watches it at the next poll, and a deleted one leaves
  a key that is dropped rather than a key that dials a stranger.
- **The dialplan grows hints, whether or not anything watches them.** `[internal]` now carries
  `exten => <number>,hint,PJSIP/<number>` for every enabled extension and
  `exten => <slot>,hint,park:<slot>@parkedcalls` for every parking slot. The park device name was
  read off the Asterisk 22 sources (`res/res_parking.c` builds it as `park:%d@%s` from the lot's
  own context, which our `res_parking.conf` sets to `parkedcalls`), not guessed. They are
  unconditional because a hint costs one dialplan line and nothing else, and because assigning a
  key must not need an apply before the lamp works — nothing about a phone is written to
  /etc/asterisk (D79), and that stays true.
- **Two more modules are loaded, and the allowlist says why.** A hint is half a lamp; the other
  half is answering the phone's SUBSCRIBE, so `res_pjsip_exten_state.so` and
  `res_pjsip_dialog_info_body_generator.so` join the allowlist (D31) — the dialog-info+xml body is
  what a BLF key asks for. The PIDF generators are deliberately **not** loaded: that is presence,
  which nothing here does.
- **Polycom renders them as an attendant resource list**, one resource per assigned key:
  `attendant.reg`, then `address`, `label` and `type` per resource. There is no `attendant.uri` —
  that names a *server-side* resource list, and ours is the one the file carries. Resources are
  numbered 1..n in key order rather than by the key's own position, because a phone reads the
  list until the first index it cannot find: a gap left by an unassigned key would hide every key
  after it. The consequence worth knowing is that clearing key 1 moves the rest up a key.
- **Yealink renders the same keys as line keys of type 15**, the BLF: `linekey.N.line="1"`,
  `linekey.N.value`, `linekey.N.type="15"` and `linekey.N.label` per assigned key. Type 15 is the
  direct counterpart of Polycom's `automata` — the phone SUBSCRIBEs for the value so the lamp
  follows its hint, and pressing the key dials it — and both kinds of key get it, for the same
  reason Polycom gives both `automata`: retrieving a parked call is dialling the slot. All eight
  keys are rendered; a handset with fewer line keys than that silently ignores the rest. Unlike
  Polycom's resource list, the keys keep their own numbers — key 4 is `linekey.4` — because a
  Yealink line key is addressed by the key itself rather than read until the first missing index,
  so an unassigned key is written as nothing and keeps its default behaviour. The consequence
  worth knowing here is the other one: key 1 is a phone's default line appearance, so assigning
  key 1 replaces it. Keys are written only for a phone that has a registration, because
  `linekey.N.line="1"` names account 1 and there is nothing to subscribe on without it.
- **A key that cannot work is dropped rather than written.** `PhoneButton.Usable` is what the
  provisioning endpoint filters through: an extension that has been deleted or switched off has
  no endpoint to watch, and a slot outside the configured lot — parking off, or fewer slots than
  it once had — has no hint to subscribe to.
- **Modals are no longer centred.** `modal-dialog-centered` is gone from `_FormModal.cshtml`, the
  only place it was. A form that changes height — tabs, eight rows of keys — moved the header up
  and down under the mouse as it grew; sitting near the top of the window keeps it still.

### D122. Music on hold is several classes, and one of them ships (2026-09-21)

> **Amended 2026-09-21: an inbound route picks the class its callers hear.** Parking was the only
> customer a class had, so a caller a receptionist put on hold heard whatever Asterisk fell back
> to. FreePBX lets each inbound route choose, and the difference between the main line and the
> support line is exactly what a site wants to hear. New column
> **`InboundRoutes.MohClassID`** (schema 021, nullable), a "Music on hold" dropdown on the inbound
> route form, and one dialplan line in the trunk's context before the call is handed on:
>
> - **`Set(CHANNEL(musicclass)=<name>)`**, not `CHANNEL(mohclass)`. The field Asterisk's `CHANNEL`
>   function has is `musicclass` (`func_channel.c`, `ast_channel_musicclass`); the other spelling
>   is what everyone calls it in conversation, and Asterisk answers it with a warning and no music.
>   Set on the channel rather than played by us, because holding is the far end's doing — what
>   Asterisk needs from us is the name to reach for when it happens. It goes in before the
>   destination, since a `Goto` never comes back.
> - **Null means no class named**, and no line is written, which is exactly what every route did
>   before the column existed. **Worth knowing, and the user's call to change:** the class that
>   ships is called `Standard`, and Asterisk's own fallback is a class called `default` that this
>   system deliberately never writes (D119) — so a route left on "Default" gets *silence* on hold,
>   not the shipped music. Making null render the `IsDefault` class instead is a one-line change in
>   `ExtensionsConfRenderer.MusicOnHoldLine`; it was not taken here because it would turn hold
>   music on for every existing route without anyone asking.
> - **`ON DELETE SET NULL`.** Deleting a class must not delete the route that played it — that
>   would take a site's advertised number off the air because somebody tidied up the hold music —
>   and a route pointing at a class that has gone is a name the renderer refuses to write. The
>   repository refuses a class that is not there on the way in; the renderer refuses one it was not
>   given, and one called `default`, on the way out.
> - **`preferchannelclass = no` stays** and does not get in the way: with it off, Asterisk consults
>   the channel's own class after the hold payload and the endpoint's `moh_interpret`, and we set
>   neither of those, so the route's class is what plays. Not yet verified on the lab VM.
>
> **Amended again 2026-09-21: an internal call backfills the class that ships.** The amendment
> above only reached calls that came in over a trunk. A call that started on a phone here never
> passes a trunk context, so nobody had ever set a class on that caller's channel, and Asterisk's
> fallback is a class called `default` that this system deliberately never defines (D119) — so a
> receptionist putting a colleague on hold gave them *silence*. Every extension in `[internal]`
> now carries one line ahead of its `Dial`, which makes the `Dial` priority 2:
>
> ```
> exten => 1001,1,ExecIf($["${CHANNEL(musicclass)}" = "" | "${CHANNEL(musicclass)}" = "default"]?Set(CHANNEL(musicclass)=Standard))
>  same => n,Dial(PJSIP/1001,30,tTkK)
> ```
>
> - **The guard is the point, not the `Set`.** An inbound route's class is the *caller's* — the
>   site chose what people who rang the support line hear — and the call then `Goto`s into
>   `[internal]` on the extension it was routed to, where this line runs on the same channel. So
>   internal only fills in where nobody chose: it sets the class when, and only when, Asterisk
>   still has its own default sitting there. The route's choice is never clobbered.
> - **Two values count as "still the default", and the literal `default` is the one that fires.**
>   The channel is not empty on the calls this actually runs on: `chan_pjsip` puts the endpoint's
>   `moh_suggest` on every channel it creates and res_pjsip's default for that is `default`.
>   Testing for empty as well costs nothing and covers a channel that arrived any other way.
>   Neither value names a class this system ever writes, so both mean silence and both are ours.
> - **The class is the one flagged `IsDefault`** — `MohClassRepository.Default()`, read off the
>   list `ExtensionsConfRenderer` is already handed for the routes. **No default class, no line**:
>   the old silence, not a reference to a class that does not exist. Which is why most of the
>   renderer's golden files are unchanged — they are rendered with no classes at all.
> - **Two modules join the allowlist** (D31), and they are the cost of this line: `app_exec.so`
>   for `ExecIf` and `func_channel.so` for `CHANNEL`. D57 turned down a `${CHANNEL(state)}` guard
>   for exactly this price when it bought nothing; here it buys the difference between hold music
>   and silence. **`func_channel` was already needed and was not there**: the trunk-context
>   `Set(CHANNEL(musicclass)=...)` of the amendment above cannot have worked on the lab VM —
>   `autoload = no` means an unlisted function is not registered, and Asterisk answers a `Set` on
>   one with a warning and no music. That is the first thing to check when this is verified.
>   The alternative that would have cost one module instead of two is a labelled `GotoIf` chain,
>   which is three lines and a label per extension; not taken, because the file is read by people.
> - **Two consequences worth knowing, both the user's call to change.** *Inbound routes left on
>   "Default"* no longer get silence when they lead to an extension — the caller lands in
>   `[internal]` with `default` on the channel and is backfilled with `Standard`. That is the
>   thing the amendment above deliberately did not do; it is done here for calls that reach a
>   phone, and not for one that reaches an announcement or a menu, because only extensions carry
>   the line. And *a parked caller whose lot is set to silence* now hears the shipped class if
>   their call started on a phone here: `res_parking` writes no `parkedmusicclass` for silence and
>   Asterisk then falls through to the channel's own class, which is no longer empty. Silence on
>   hold is not a thing this system can offer a channel *and* music on hold at the same time.
> - **Ring groups are not covered.** A caller who dialled a group rather than an extension still
>   has nothing on their channel, because the line is written per extension. It is the same one
>   line in `AppendRingGroup` whenever that is wanted; it was not taken here because it was not
>   asked for.
> - **Neither is the other direction, and that is worth a decision of its own.** This sets the
>   class on the channel that is *executing the dialplan*, which is the caller's. The extension
>   they dialled answers on a channel chan_pjsip made, carrying `default` from its own
>   `moh_suggest` — so when the caller holds *them*, they are the ones in silence. The tidy fix
>   is probably not another dialplan line: `moh_suggest =` (empty) on every endpoint would leave
>   the class blank on every channel, and app_dial copies the caller's onto an outgoing channel
>   that has none — read off `app_dial.c` and **not confirmed on the lab**, which is what would
>   have to happen first. This line's empty test already covers that case if it is ever taken.
>   Writing `moh_suggest = Standard` instead would be the wrong
>   way round — with `preferchannelclass = no` the hold payload is consulted *before* the
>   channel's own class, and that payload is the holder's `moh_suggest`, so every route's class
>   would be overridden by the extension that held the call. Neither was changed here.
>
> **Amended a third time 2026-09-21: the called channel gets the class from a Gosub.** The
> amendment above was half the fix, and it says so in its own last bullet: it sets the class on the
> channel *executing dialplan*, which is the caller's. The phone they dialled answers on a channel
> `chan_pjsip` created, which never runs dialplan at all — so when the **caller** is the one who
> presses hold, the channel Asterisk asks for music on is that one, it asks for the class named on
> it, and that is the literal `default` its `moh_suggest` put there. This system deliberately
> defines no `default` class (D119, and parked-caller silence depends on it), so what the lab got
> was one line of log per hold and no music:
>
> ```
> WARNING res_musiconhold.c _get_mohbyname: Music on Hold class 'default' not found in memory.
> ```
>
> `Dial`'s **`U(<context>)`** option is Asterisk's own hook for this: it Gosubs into `<context>,s,1`
> on the channel the Dial created, as that channel answers. Every internal Dial the renderer writes
> now carries `U(sub-setmoh)`, and one small context is written once:
>
> ```
> exten => 1001,1,ExecIf($["${CHANNEL(musicclass)}" = "" | "${CHANNEL(musicclass)}" = "default"]?Set(CHANNEL(musicclass)=Standard))
>  same => n,Dial(PJSIP/1001,30,tTkKU(sub-setmoh))
>
> [sub-setmoh]
> exten => s,1,ExecIf($["${CHANNEL(musicclass)}" = "" | "${CHANNEL(musicclass)}" = "default"]?Set(CHANNEL(musicclass)=Standard))
>  same => n,Return()
> ```
>
> - **A `[default]` section was the fix that was not taken**, and it is worth writing down why it
>   never will be. Defining one would silence the warning and give every channel music — including
>   a parked caller whose lot is set to silence, because "silence" here *is* the absence of a class
>   for Asterisk to fall back to (D119, `ParkingConfRenderer`, `MohConfRenderer`). `default`
>   staying undefined is load-bearing; the fix has to name a real class on the channel instead.
> - **The same guard, from the same code.** The subroutine's `ExecIf` is
>   `ExtensionsConfRenderer.InternalMusicOnHoldLine` — the identical string the caller's own line is
>   built from, not a second copy of it — so the two sides of a call cannot end up guarded
>   differently. It fills in only where Asterisk still has its own default sitting there, which on
>   a freshly created channel is always, and leaves a class anybody chose alone.
> - **The option and the context stand or fall together.** No class flagged as the default means no
>   `[sub-setmoh]` and no `U()` naming it — the old silence, as before. This is not tidiness: a
>   `U()` pointing at a context Asterisk cannot find fails the Gosub, and a failed Gosub in a Dial
>   ends the call. One value decides both, in `InternalDialOptions`.
> - **Ring groups are covered this time**, which the caller-side line still is not. A member who
>   answers a group call is a Dial-created channel like any other, so `AppendRingGroup` writes the
>   same options an extension does. What is still missing is the *caller* who dialled a group
>   rather than an extension; that remains the one line in `AppendRingGroup` it always was.
> - **Outbound is deliberately left alone.** A Dial out over a trunk keeps the bare `tTkK`. The
>   channel it creates belongs to the provider, and what the far end of an outbound call hears
>   while we hold them is their carrier's business, not ours to name a class for. **Worth knowing,
>   and the user's call:** that means an internal caller who holds an *outbound* call is still the
>   silent case, and still logs the warning.
> - **One module joins the allowlist** (D31): `app_stack.so`, for the `Gosub` the `U()` performs
>   and the `Return` that ends it. It was not there — nothing in this dialplan had used a
>   subroutine before.
> - **Not verified on the lab.** What has to be seen: a colleague put on hold by the person who
>   called them hearing the shipped tracks, the warning gone from the Asterisk log, and a ring
>   group member answering behaving the same way.

D119 built one music on hold class, called `parking`, on one flat directory, because the only
thing in this system that played hold music was a parked call. A class is a *directory* as far as
`res_musiconhold` is concerned, so more than one costs a section each — and the ask was exactly
that: a different tune or message for parked callers than for anywhere else music is wanted.
Music on hold is now its own feature with its own page, and parking is one of its customers.

- **New table `MohClasses` (019): `Name`, `Directory`, `IsDefault`.** `MohFiles` gains
  `MohClassID` (`NOT NULL`, `ON DELETE CASCADE`) and its `File` is unique *per class* rather than
  globally, because each class plays its own directory. SQLite cannot add a `NOT NULL` column
  with a `REFERENCES` clause, so the script rebuilds the table; everything D119 uploaded is
  pointed at the class that ships, which is the directory those files are already in.
- **`Name` is unique `COLLATE NOCASE`, and no class may be called `default`.** Asterisk matches
  class names with `strcasecmp` (`res/res_musiconhold.c`, `moh_class_cmp` and `ast_str_case_hash`),
  so "Jazz" and "jazz" would be one class to it and two to an admin. `default` is refused outright
  for the reason D119 refused it: it is the class Asterisk falls back to whenever music is asked
  for and none was named, so a class by that name would be played to a parked caller whose setting
  says `silence`. That is checked in the model, in the database and again in the renderer.
- **The class that ships is called `Standard`, not `Default`.** The word would have been the
  reserved name under another capital. The table says which class is the default one with a badge
  instead, and its *directory* is still `default` — a path Asterisk has no opinion about.
- **`Directory` is separate from `Name`**: lower-case letters, digits and dashes, 24 characters,
  unique. Renaming a class must not have to move files, and a name an admin would write ("Front
  desk") is not a path. Changing the directory does move the music, in one `Directory.Move`.
- **The renderer writes one section per class**, `[<name>]` with `mode = files`,
  `directory = /var/lib/asterisk/moh/<class directory>` and `sort = alpha`. A class with no rows
  is still written out: `files` mode scans the directory, so the tracks the installer put there
  play with no database row at all, and an empty directory is simply nothing to play.
  `preferchannelclass = no` stays, for D119's reason.
- **Parking picks a class by name: new setting `Parking.MusicClass`** (Asterisk scope, default
  `Standard`), written as `parkedmusicclass`. `Parking.Audio` still decides *whether* there is
  music at all, and silence is still implemented as writing no `parkedmusicclass`. The setting is
  validated for shape only — a name Asterisk could match, and not `default` — because a class an
  admin is about to create must not be unstorable first; a name that matches no class is silence,
  since Asterisk finds nothing to start. The Music on hold page will not delete the class the
  parking settings point at, or the class that ships.
- **A new `/Moh` page, "Music on hold", under Call Handling after Parking**, and the upload
  section is gone from the Parking page. Two tables: the classes, and every track with the class
  it plays in. Rows open their edit form in the shared modal (D42, D48) with Delete in the footer.
  The upload is D119's spool-and-convert, placed only if the conversion worked (D55) — but
  into the class's directory, and the format changed after the user asked for better than
  telephone-quality music: hold music now converts to 16 kHz mono **G.722**, the same wideband
  the voice path carries (D117), so a G.722 call plays it with no transcoding at all.
  `.g722` files are read by `format_pcm.so` — already loaded for the announcement WAVs, because
  in Asterisk 22 one module covers raw pcm/alaw/ulaw/g722 files and there is no separate
  format_g722 to add. Announcements stay 8 kHz PCM WAV (D55): they play through the narrowband
  voicemail path, and one format per feature is the bargain both make. 
- **A track does not move between classes.** The class is chosen when the track is added and is
  read-only afterwards: moving one is moving a file on disk to make a dropdown true, and deleting
  it and uploading it again says the same thing with no new failure mode to explain.
- **The three tracks that ship are rows *and* files, put there by different things.** The schema
  script inserts `default-1.g722`, `default-2.g722` and `default-3.g722` into the class that ships —
  but only on a system that had no music on hold of its own, so an existing install's tracks stay
  its own business. The installer puts the audio there: `install.sh` and `build-asterisk-vm.sh`
  transcode the three royalty-free Audiodollar MP3s kept in the repo at `media/musiconhold` with
  `ffmpeg -ar 8000 -ac 1 -sample_fmt s16`, never overwriting a file that is already there. They
  are owned `tnpbx:asterisk` 0640 rather than `asterisk:asterisk`, because renaming a track ends
  in a `chmod` on that file and `chmod` needs ownership — and `MohStore` now logs a refused
  `chmod` rather than throwing, since a move keeps the file's mode and there was nothing to fix.
- **`MohFile.IsValidFile` no longer insists on the `<id>-` prefix.** Uploads are still stored as
  `<MohFileID>-<slug>.g722` — that is what keeps two tracks with the same name apart — but the
  rule is now lower-case letters, digits and dashes then `.g722`, because `default-1.g722` was
  named by a shell script that has never seen the database. Nothing about the safety changes: no
  dot but the extension and no separator of any kind, checked again before a path is built.
- **The opsound tarball is gone from both installers.** It was D119's starter music, downloaded
  into the flat directory that is no longer a class. The tracks that ship replace it, and one
  fewer thing is fetched over the network at install time. An upgrade moves any `*.g722` still
  loose in `/var/lib/asterisk/moh` into the class that ships, which is where its rows now point —
  in `app-deploy.sh` as well as `install.sh`, because a deploy is what brings the schema script
  that repoints them.

### D123. A phone is rebooted by SIP NOTIFY, not by an HTTP push (2026-09-21)

D118 accepted that the reboot push reaches a phone's web UI only on the server's own network, and
that deployments are hosted PBXes where no phone is ever IP-reachable from the server. That made
the "Reboot phone" button a button that worked in the lab and nowhere else. It now sends a SIP
NOTIFY instead, which **rides the registration**: the phone told us where it is when it registered,
so the NOTIFY reaches it behind a home router, a hotspot or a corporate NAT — anywhere it can make
a call from.

- **`pjsip_notify.conf` is rendered with two types**, `polycom-reboot` (`Event: check-sync`) and
  `yealink-reboot` (`Event: check-sync;reboot=false`), each with `Content-Length: 0` because the
  NOTIFY has no body and a phone sent one without that header may sit waiting for a body that never
  comes. The two differ because `check-sync` does: a Polycom phone reboots on a bare one, and a
  Yealink phone reads the `reboot=` parameter. **No `[general]` section** — Asterisk 22's
  res_pjsip_notify refuses the file outright with one, found on the lab VM, and that is what the
  test for the absence of something is there for.
- **The app sends it over AMI with the headers spelled out**, `Action: PJSIPNotify`, `Endpoint:
  <extension>`, `Variable: Event=...`, `Variable: Content-Length=0`, rather than naming a type from
  the file. A typed action, so the AMI account still needs no `command` permission (D32), and no
  round trip through a file Asterisk only reads at module load. The file carries the same two
  messages for the equivalent `pjsip send notify polycom-reboot endpoint 1001` at the CLI. The old
  `PJSIPSendNotify` action this code sent is not an action Asterisk has.
- **pjsip_notify.conf joins the startup set** (D33): res_pjsip_notify reads it when the module
  loads and no reload re-reads it, so an apply that changes it reports a restart. In practice it is
  written once and never again — its contents are fixed.
- **The button is disabled with the reason on it** when the phone has no line key, or when its
  extension has no registered contact: there is nothing to send a NOTIFY to, and a disabled button
  saying why beats a button that is not there. An AMI we could not ask answers "unknown" for every
  extension, and not knowing is not a reason to take the button away — the toast says so if the
  send then fails.
- **Yealink's reboot button is not built.** The Yealink NOTIFY in the file is the config re-read,
  which is what saving a Yealink phone sends; whether its reboot button should send
  `reboot=true` is the user's call when that piece comes. **Decided in D134**: it does, the
  button is built, and `[yealink-reboot]` in the file is now `check-sync;reboot=true`.
- **The HTTP push stays for the config reload only** (D86). The only NOTIFY a Polycom phone
  understands reboots it, and rebooting a handset because somebody renamed it would be worse than
  waiting for the poll. `PolycomPusher.PushReboot` is gone with the button that called it.

### D124. A digit map may only be eager where it cannot be wrong (2026-09-21)

The Polycom digit map D79 shipped was `xxxx|*xx.T|[2-9]11|0T`, with `dialplan.digitmap.timeOut=3`.
On a real Poly Edge 450 an attended transfer to a ten-digit mobile went out as its **first four
digits**: `xxxx` full-matches at exactly four, and a Polycom phone dials the moment the digits it
has match a pattern that does not end in `T` — a longer pattern still being partially matched does
not stop it. The map is now built per system by `PolycomDigitMap.For(extensions)`.

- **The rule, and it is the whole design: a pattern may be eager only when nothing dialable is
  longer than it and starts with it.** Everything else ends in `T`, so the phone sends after the
  inter-digit timeout — or at once when the user presses `#`, which Polycom takes as "send now".
- **Eager:** `[2-9]xxxxxxxxx` (ten digits), `1xxxxxxxxxx` (eleven), and `[2-9]11`. N11 is safe
  because the NANP reserves those codes: no area code is N11, so `211` cannot be the start of a
  ten-digit number.
- **Timed:** the extensions, seven-digit local dialing (`[2-9]xxxxxxT` — it is the first seven
  digits of a ten-digit number, and the outbound routes allow it), the feature codes (`*xx.T`,
  because `*8` takes a whole extension after it) and the operator (`0T`).
- **The extension patterns come from the extensions this system has**, one per length, with the
  first digit narrowed to the digits really in use: the lab's 100–104 render as `1xxT`, not
  `xxxT`. Every extension is 2–6 digits and so is always shorter than a real number, which is why
  an extension pattern always takes the `T`. Disabled extensions count — what is being described
  is which lengths mean "extension", and a phone must not need a re-poll to dial one that was
  switched back on this morning.
- **The cost, and it is the honest one:** overlapping lengths still overlap. A site on four-digit
  extensions that dials `1800`, pauses three seconds and then types the rest has sent `1800`. That
  is inherent in a map that lets an extension be dialled without pressing Send, and it is what
  FreePBX's generated maps do too. Four digits *of a ten-digit number typed at speed* is the bug;
  a three-second pause mid-number is a user changing their mind.
- **The tests do not only pin the string.** `PolycomDigitMapTests` compiles the map the way the
  phone reads it and asks whether any prefix of a number would be sent on its own, for a ten- and
  an eleven-digit number — and asserts that the map this replaced *does* send four digits and
  stop, so the test can be seen to see the bug.
- **Yealink gets no dial plan at all**, and now has a test saying so. It dials on its own Send key
  and its own timers; `dialnow` rules for a Yealink are a piece to justify on their own.
  **Amended by D135**: Yealink now gets the eager patterns above as `dialplan.dialnow.rule.1..3`,
  and nothing else.

### D125. Outbound caller ID: trunk, then route, then extension — and the route's hold music (2026-09-21)

An outbound route can name **what a call that matched it calls out as** (FreePBX's rarely used
"option CID"), an extension can name **its own outbound caller ID** so a user with a direct DID
calls out as that DID, and a route can also name **the music on hold class its caller hears**.
Schema 022: `OutboundRoutes.CallerID`, `OutboundRoutes.MohClassID`, `Extensions.OutboundCallerID`.

**The precedence, decided by the user and the whole point of the design: trunk < route <
extension.** The trunk's `callerid` in pjsip.conf is untouched and stays the fallback it always
was — nothing in this decision writes to it.

- **The extension claims at origination, with `set_var`.** Its endpoint gains
  `set_var = TNPBX_CID="Jane Smith" <17141234567>`, and chan_pjsip puts that variable on every
  channel the phone creates. So the claim exists before any dialplan runs, and **no route has to
  know which extensions have DIDs** — which is the reason for doing it here rather than with a
  per-extension line in `[internal]` that every route would then have to read around.
- **The route applies it, and only the route.** `set_var` sets a plain channel variable —
  `ast_set_variables` does not evaluate dialplan functions — so something has to turn it into
  `CALLERID(all)`, and the outbound route contexts are the only place that does:

  ```
  exten => _NXXXXXX,1,ExecIf($["${TNPBX_CID}" != ""]?Set(CALLERID(all)=${TNPBX_CID}))
   same => n,ExecIf($["${TNPBX_CID}" = ""]?Set(CALLERID(all)="Acme Sales" <17141234567>))
   same => n,Set(CHANNEL(musicclass)=Front Desk)
   same => n,Dial(PJSIP/callcentric/sip:1714${EXTEN}@callcentric.com,60,tTkK)
   same => n,Hangup()
  ```

  **Applying it nowhere else is what makes a blunt `set_var` safe.** An internal call never enters
  an outbound context, so a colleague still sees the endpoint's own `callerid` — the name and the
  extension number. A call that arrived on a trunk cannot enter one either: a trunk's context
  includes nothing (D50). The claim reaches exactly the calls it is about.
- **The route's own caller ID is written guarded, the extension's is not.** The two `ExecIf`s are
  opposite halves of one test, so the order they are written in does not matter and neither can
  clobber the other. The route's line is guarded **even where no extension has a caller ID at
  all** — the variable is simply empty and the `ExecIf` always fires — because the alternative is
  a line whose meaning changes silently the day somebody gives an extension a DID.
- **Nothing is written where nothing was named.** A route with no caller ID and no class, on a
  system where no extension claims one, renders the single `Dial` and one `Hangup` it rendered
  before any of this existed, which is why every existing golden file is unchanged. The line that
  applies a claim is left out entirely until some enabled extension has one.
- **The route's music is `Set(CHANNEL(musicclass)=...)` and deliberately unguarded**, unlike the
  internal backfill (D122 amended). It is the class the *caller* hears while **the far side holds
  them** — a receptionist at the other end pressing hold on us. An outbound caller's channel has
  passed no trunk context and no extension entry, so nothing can have named a class on it and
  there is nothing here to avoid clobbering. Null means no class named, as inbound, and the same
  caveat applies: Asterisk's own fallback is a class called `default` that this system never
  writes (D119), so a route left on "Default" is silence, not the shipped music.
- **`ON DELETE SET NULL`** on the route's class, for the reason the inbound one has it: deleting a
  hold-music class must not delete the route that named it. The repository refuses a class that is
  not there on the way in; the renderer refuses one it was not given, and one called `default`, on
  the way out — `MohClassName` is now shared by both directions so the two cannot drift.
- **Both caller ID fields are read by one validator**, `CallerIDFormat`, because both end up in the
  same `CALLERID(all)`. FreePBX's two forms are accepted — `"Acme Sales" <17141234567>` or a bare
  number — with the quotes optional on the way in and **ours to add on the way out**: `ConfText`
  refuses a value containing a quote, so the name and the number are checked separately and then
  wrapped, exactly as the endpoint `callerid` lines are.
- **The rules are deliberately narrower than Asterisk's.** A number is digits only, up to 15: no
  `+`, no spaces, no punctuation, like every other number this system stores (a DID, a prepend).
  A name is up to 32 letters, digits, spaces and `. ' - _ &` — **no brackets, commas, colons or
  quotes**, because what is generated is `Set(CALLERID(all)="Name" <number>)` *inside an `ExecIf`*:
  Asterisk reads an application's arguments to the matching bracket, and `ExecIf` reads a colon as
  the start of its else branch. A site that needs `+E.164` presentation is a decision to take on
  its own, for every number field at once.
- **No new modules.** `set_var` is core res_pjsip (an endpoint option, not an application),
  `func_callerid.so` was already on the allowlist for `${CALLERID(num)}` in the `*97` entry, and
  `app_exec.so` + `func_channel.so` arrived with D122's amendments. The allowlist is unchanged,
  which was checked rather than assumed (D31).
- **What the toll-fraud story does not change.** A caller ID is presentation, not permission: the
  route's pattern still decides what may be dialled, and international dialling is still refused
  by validation (D47). Nothing here lets a call out that could not go out before.
- **Not verified on the lab VM.** Three things need a real call: that the provider accepts and
  displays the caller ID we set (Callcentric may override it to an account-owned number, which is
  the provider's right and not something this system can fix), that a held outbound caller hears
  the route's class, and that an extension with a DID beats a route with a caller ID on the same
  call.

### D126. Voicemail to email: a fixed mailcmd script, not a mail server (2026-09-21)
The last piece of F4, and the one F4 left deliberately open: **how does the recording actually
leave the box?** The answer is that Asterisk keeps composing the email and we take over the
delivery, with **no MTA installed at all**.

`app_voicemail` is good at making the message — it renders the template, encodes the recording as
a MIME attachment and hands the finished thing to whatever `mailcmd` names, on stdin. What it is
not good at is delivery: `mailcmd` defaults to `/usr/sbin/sendmail -t`, which means a system mail
server, which means Postfix or Exim on a PBX. So the generated `voicemail.conf` now carries

```
mailcmd = /opt/tnpbx/bin/voicemail-mail
```

and that is a **python3 script in this repository** that reads the message on stdin and relays it
through the SMTP settings the Email tab already holds.

- **No system MTA, and this is the whole reason.** Postfix is a large, network-listening,
  separately-configured service with its own attack surface, its own queue, its own security
  updates and its own idea of where mail goes — on a box whose entire premise is a small surface
  area (D115 made the same argument for not having one). What the job actually needs is "open a
  TCP connection, authenticate, send one message", which is a few lines of `smtplib`.
- **One sender and one credential set.** The test button proves `Mail.Smtp.*` (D115); voicemail
  uses those same settings. There is no `serveremail` and no second relay to configure, and an
  admin who has made the test button work has made voicemail email work. **The script owns the
  `From` header**, replacing app_voicemail's `asterisk@<hostname>` — an address no hosted relay
  accepts and no SPF record covers — with `Mail.FromAddress` / `Mail.FromName`, which is the
  address the relay authenticated as. That is the *only* header it touches: subject, body, the
  other headers and the attachment are relayed exactly as composed.
- **SMTP only, deliberately.** `Mail.Transport = graph` still sends alerts through Graph, but
  voicemail goes through the SMTP relay or not at all: the script would otherwise need an Entra
  client credential of its own, outside the web app's process and its `appsettings.json`, which is
  a secret in a second place for one feature. A site on Graph that wants voicemail email fills in
  the SMTP relay as well. **This is a known limitation, and the Email tab does not yet say so.**
- **`mail.json`, not the database.** The script runs as `asterisk` and has no business opening the
  application's SQLite file, so the web app writes `/opt/tnpbx/Config/mail.json` whenever a setting
  is saved and at every start. It carries the SMTP password, so it is **0640, owner `tnpbx`, group
  `asterisk`**, in a **setgid 2750** directory the installer creates — the same trick `/etc/asterisk`
  uses (D18), and the **only** privilege move in this piece. The Helper is not involved: nothing
  here needs root at run time.
- **Settings that leave the relay unusable remove the file** rather than leaving a stale credential
  behind, and the script fails closed on a missing, unreadable, malformed or incomplete one — exit
  1 with a sentence on stderr, which Asterisk logs. It never falls back to "send it somehow".
- **The script is root-owned, mode 0755, and takes no arguments.** app_voicemail runs it as
  `( mailcmd < tmpfile ; rm -f tmpfile ) &`, so there is no command line to inject into; it reads
  two fixed paths (its config, and stdin), never reads the environment and never executes
  anything. Root-owned because neither the web user nor the asterisk user may rewrite a program
  Asterisk is about to run. `app-deploy.sh` reinstalls it from the publish on every deploy and
  re-asserts `root:root` **after** its `chown -R tnpbx`, and `bin/` and `Config/` join
  `appsettings.json` and `Data/` as state that survives a re-deploy (D95).
- **It refuses to put the relay password on the wire.** Port 465 is implicit TLS; every other port
  is `STARTTLS` where the relay offers it, and where it does not *and a username is configured* the
  script hangs up rather than authenticating in plain text. An unauthenticated hop to an internal
  relay is still allowed, because that is the admin's own choice to make.
- **`format = wav49|g722`, reordered.** D117 put g722 first for wideband storage; app_voicemail
  attaches **the first format in the list**, which made the attachment a raw `.g722` no mail client
  will play. wav49 (GSM-in-WAV) leads now — every desktop mail client plays it and the storage cost
  is similar — and **playback is unaffected**: Asterisk picks the best format on disk for the
  listening channel whatever order they are written in, so a wideband phone still hears g722.
- **The template is a constant, not a setting.** FreePBX's wording — "New message N in mailbox X
  from Y", and a body with the date, the length and a reminder that `*97` plays it — with
  `emaildateformat = %A, %B %d, %Y at %r`. A per-site template is a text box that renders into a
  file Asterisk parses, for a line of wording; it can become a setting the day somebody asks. Only
  variables app_voicemail actually substitutes appear in it (`VM_NAME`, `VM_DUR`, `VM_MSGNUM`,
  `VM_MAILBOX`, `VM_CIDNUM`, `VM_DATE`), and a test asserts that.
- **Not an apply.** The `Mail.*` keys stay App-scoped (D103): the generated `voicemail.conf` names
  a fixed path and nothing else, so no mail setting changes a conf file. `mail.json` is written the
  moment the setting is saved, which is also why the apply button stays quiet for it.
- **`python3` joins the package list.** A Debian standard install has it; a minimal cloud image may
  not, so `install.sh` asks for it explicitly and `app-deploy.sh` warns when it is missing.
- **Not verified on the lab VM.** Everything below needs a real message: that app_voicemail runs
  the script at all and what it logs when the script exits non-zero, that the attachment arrives
  playable, that the rewritten `From` gets past the relay, and — the one thing that could not be
  checked from here — **that app_voicemail attaches only the first format**, which is what the
  `format` reorder rests on. `grep -n 'fmt' /usr/src/asterisk-22/apps/app_voicemail.c` around
  `sendmail()` settles that, and the substitution list in
  `configs/samples/voicemail.conf.sample` settles the template.

### D127. `r` on internal Dials: an answered caller hears ringback from the server (2026-09-19)

A caller an IVR or a ring group has already been answered cannot be sent 180 Ringing — the call is up, so Asterisk would have nothing to signal. Without help that caller hears silence while the extension rings. `InternalDialOptions` therefore adds `r` to every internal `Dial`: the server generates ringback toward the caller for the whole dial, whether or not the caller was answered. This was found incomplete by D132 — `r` inband ringback also needs a tone zone to exist.

### D128. Voicemail transcription: whisper.cpp on the box, and a generated file to ask for it (2026-09-21)
Voicemail to email (D126) delivers a recording somebody still has to listen to. Transcription puts
the words in the email, and the interesting question was never the engine — it was **where the
audio goes** and **how a per-extension choice reaches a script that takes no arguments**.

- **On the box, not in an API.** A voicemail is a customer's private message, often the one with
  the account number in it. Sending it to a transcription service would make every TNPBX install a
  site that ships its callers' voices to a third party, and would add an API key to protect, a
  bill, a network dependency and a privacy question to answer. whisper.cpp runs on the server with
  no network at all. The engine is **whisper.cpp with the `small.en` model** (~488 MB, English
  only): roughly real time on the hardware this targets, and noticeably better than `base.en` on
  8 kHz telephone audio, which is the worst input a speech model gets.
- **Optional, and the install may fail at it.** There is no Debian package, so `install.sh`
  clones and builds it — which is exactly the kind of step that fails on a strange network or a
  small VM. So **every step of it only warns**: a box where the clone, the build or the 488 MB
  download failed is a fully working PBX that emails voicemail without a transcript, and the
  script checks for `whisper-cli` and the model before it tries to use either. This is the one
  section of `install.sh` that never calls `die()`. `app-deploy.sh` preserves `whisper/` across
  re-deploys for the obvious reason: a deploy has no business re-downloading half a gigabyte.
- **Per mailbox, not per system.** Transcribing costs roughly the length of the message in CPU
  time on a machine that is also switching calls. The receptionist who wants readable voicemail
  should not make every other extension pay for it, so it is a checkbox on the extension —
  `Extensions.VoicemailTranscribe`, off by default, and ignored without an address to email.
- **A generated JSON file is how the choice reaches the script, because nothing else could.**
  `app_voicemail` has no transcription option, and `mailcmd` is a fixed string it runs as
  `( mailcmd < tmpfile ; rm -f tmpfile ) &` — there is no argument to pass and no variable to set.
  Inventing a per-mailbox option in `voicemail.conf` was the obvious idea and is the wrong one:
  `app_voicemail` answers an unknown option with a warning **per mailbox on every reload**, is
  under no obligation to keep ignoring it, and the script would then have to parse
  `voicemail.conf` to read it back. So a renderer writes
  **`/etc/asterisk/tnpbx-voicemail-options.json`**, mailbox → `{ "Transcribe": bool }`, next to
  the file it belongs with. It is rendered output from the extension rows, so it is written by an
  apply through the same atomic writer as everything else (D4), and the asterisk user already
  reads that directory through the group (D18). It carries **no PIN, no address and no name** —
  a mailbox number and a boolean — so a group-readable conf directory is a fine place for it.
- **It is listed against `app_voicemail` even though Asterisk never reads it.** `GeneratedFile`
  has two states, "this module reloads it" and "no module, so this needs an Asterisk restart"
  (D33). The second would have an apply telling the operator to restart Asterisk over a file
  Asterisk does not read, which is worse than a pointless reload of the module that owns
  voicemail — and that module is being reloaded on those applies anyway, because `voicemail.conf`
  changed with it.
- **The script finds the mailbox in the email, and the audio in the MIME.** `app_voicemail` stamps
  `X-Asterisk-VM-Extension` on what it composes; the subject is the fallback, and it is safe to
  parse because *we* write that template ("in mailbox `<number>`", D126). No mailbox found means no
  transcription: a guess would transcribe against somebody else's settings. The recording is read
  out of the MIME part rather than off disk — the script is never told a path, and the one it could
  work out would be a path derived from a message. wav49 is GSM-in-WAV at 8 kHz and whisper wants
  16 kHz mono PCM, so **ffmpeg** (already a dependency, D55, D122) converts it in a temporary
  directory that is deleted however the function returns.
- **Synchronous, with a 300 second timeout.** `app_voicemail` already forked this into the
  background, so the only thing waiting is the message itself, and a transcript that arrives in a
  second email later is worse than one that arrives in the first. `maxsecs` caps a message at 300
  seconds and the model is roughly real time, so the timeout is where something has gone wrong,
  not where a long message gives up.
- **Transcription never blocks the email.** Not installed, not asked for, no attachment, ffmpeg
  missing, whisper crashed, whisper timed out, an exception nobody predicted — every one of them
  relays the message exactly as D126 does today, with a line in syslog saying which. The relay
  still fails closed; only the transcript fails open. A mailbox that asked for one gets
  **`X-TNPBX-Transcribed: yes` or `no`** on the email, so "why is there no transcript" is answered
  by looking at the message rather than by guessing.
- **The transcript is a new `text/plain` part, before the attachment**, rather than an edit to
  app_voicemail's body: that body is composed in whatever charset app_voicemail was configured
  for, and appending UTF-8 speech to it would be guessing. The new part declares its own.
- **Not verified on the lab VM.** The renderer, the schema and the script's structure are tested
  here; what a real message proves is that `X-Asterisk-VM-Extension` is present and spelled that
  way, that the attachment's content type arrives as `audio/*`, that ffmpeg decodes wav49 from
  app_voicemail's own writer, and what `small.en` actually does to a 30 second voicemail recorded
  over G.722.

### D129. The voicemail email is ours: the script calls the application back (2026-09-21)
D126 put delivery in our hands and left composition in app_voicemail's. That was the right first
step and it shows: the email that arrives is a plain-text template with a transcript bolted on as
a second MIME part and a `.WAV` attachment some phones will not play. This piece takes composition
too — **the application builds the email**, with its own template, the transcript in the body and
the recording attached as an MP3 — **without giving up the guarantee that a voicemail always
arrives**.

- **A callback, not a replacement.** `mailcmd` still points at `/opt/tnpbx/bin/voicemail-mail`, and
  app_voicemail still composes its message. The script now tries one thing first: it POSTs what the
  email says about the message — mailbox, the message's path on disk, caller ID, duration, arrival
  time — to `http://127.0.0.1:8080/api/voicemail/notify`. **200 and it exits**; app_voicemail's
  `delete=yes` then tidies up as usual. **Anything else** — the app down, mid-deploy, no token,
  slow, or answering badly — **and it relays the message exactly as D126 and D128 do today**,
  transcript and all. The fallback is the whole reason the design is shaped like this: an
  application that is not running must never mean a voicemail nobody hears.
- **Why not have the app do it all?** Because then the app is on the critical path of every
  message. The script is what Asterisk executes and the script always works; the nicer email is an
  improvement it attempts, not a dependency it acquires.
- **The token is the only thing protecting the endpoint, and it is the first thing checked.** The
  endpoint is outside the Entra cookie for the same reason provisioning is (D77): the caller is a
  script running as `asterisk`, with no browser and no session. It carries
  `Authorization: Bearer <Mail.VoicemailCallbackToken>`, compared in fixed time, and **a blank
  configured token matches nothing** — a system without one has a closed endpoint, not an open one.
  Behind that, and only behind it, the request must come from the loopback address. It is
  `[AllowAnonymous]` and `[IgnoreAntiforgeryToken]`, which are the two things that would otherwise
  refuse it, and nothing else about the pipeline changes.
- **The token is generated, not typed.** A machine credential between two processes on one box;
  40 characters from `SecretGenerator`, created on the first start that finds it missing, exactly
  as the ACME account key is created on the first order (D98). A feature that needs an admin to
  invent a secret before it works is a feature that does not work. It is a **secret key** — masked
  in the settings table, never logged — and it travels to the script in `Config/mail.json`, which
  already carries the SMTP password at 0640 `tnpbx:asterisk`. One file is the whole interface
  between these two processes, and it stays one file. Clearing it turns the callback off; the next
  restart makes a new one.
- **Nothing the script sends is trusted.** The mailbox has to be an extension with voicemail
  enabled and an address. The path has to match `VoicemailSpool`'s pattern exactly — the spool
  root, a context, **the mailbox from the request**, `INBOX`, `msgNNNN`, no extension — anchored
  with `\A`/`\z` rather than `^`/`$` so a trailing newline cannot sneak past, and not trimmed, so
  "nearly the right path" is not the right path. That closes traversal and closes one mailbox
  asking for another's messages. The caller's name and number are text a stranger chose: they are
  flattened by `MailText.Plain` before they reach a subject line (a line break in a header is an
  injected header) and HTML-encoded by the renderer before they reach a body.
- **MP3, with the original as the fallback.** ffmpeg — already a dependency (D55, D122) — converts
  the wav49 recording to 48 kbit/s mono MP3, which every phone and mail client plays and which
  keeps a five minute message under two megabytes. If ffmpeg is missing or refuses the file, **the
  original is attached instead**: a conversion is not worth an email. A mailbox set to
  `attach=no` still gets no audio, because that is what it asked for.
- **Transcription moves into the app, and gains something.** Same engine, same model, same 300
  second timeout (D128), but it reads the message **from disk** rather than out of the MIME — so a
  mailbox that emails without the recording can still have a transcript, which the script could
  never do. Every failure is still a null transcript and never an error.
- **The message number is tried both ways.** app_voicemail numbers messages from 1 in the email and
  from 0 on disk, so the script tries `msgNNNN` for both and takes the one whose `.txt` is really
  there, rather than encoding a belief about which end of that off-by-one it is on. Both candidates
  are inside that mailbox's own INBOX whatever happens.
- **Reading the spool needs no new privilege.** `/var/spool/asterisk` is 0755 `asterisk:asterisk`,
  the mailbox directories are group-readable, and the web user is already in the `asterisk` group
  (D18) with `/var/spool/asterisk` already in the service's `ReadWritePaths`. The Helper is not
  involved and nothing here runs as root.
- **SMTP only, still.** Graph sends a JSON message with a body and no parts, so an attachment has
  nowhere to go; voicemail email remains the SMTP relay's job, as in D126. This is now the second
  reason for that limitation rather than the first.
- **Tested where it can be tested.** The renderer, the path gate, the token, the text flattening
  and the ffmpeg/whisper fallbacks all have tests; the converter and the engine are stand-in shell
  scripts, so they run on a machine that has neither. The controller itself is glue and is not
  tested, because the test project deliberately does not reference the web project — so every
  decision it makes was put somewhere that could be.
- **Not verified on the lab VM.** What a real message proves: that the `X-Asterisk-VM-*` headers
  are spelled as assumed (`Caller-ID-Num`, `Caller-ID-Name`, `Duration`, `Message-Num`, `Context`,
  `Orig-time`), which end of the message-number off-by-one is real, that the web user can read the
  spool, that ffmpeg makes a playable MP3 out of app_voicemail's wav49, and that the whole callback
  finishes inside a caller's patience.

### D130. Forwarding: a list of places that replaces the extension's own phone (2026-09-21)
F2b, "follow me", built as **one field on the extension** rather than as a feature of its own.
`Extensions.Forwarding` (schema `024`) is a space-separated list of places to ring; when it is not
empty it **replaces** the extension's own phone in the ring, and when it is empty the dialplan is
byte for byte what it was before this existed.

- **The field is the whole ring, not an addition to it.** Someone who wants their handset to keep
  ringing puts their own number in the list — `1001 7146085242` rings the desk and the mobile
  together. This is the one thing the UI hint says in bold, because "my desk phone stopped ringing"
  is what a user notices otherwise. The alternative, an implicit self-ring plus a checkbox to turn
  it off, is a second control to explain and a second state to render; one field that means exactly
  what it lists is smaller in every direction.
- **Ring all, one `Dial`.** The targets are joined with `&` into the extension's existing `Dial`,
  so they ring simultaneously and the first to answer takes the call. **Everything around that line
  is untouched**: the same 30 second ring time, the same `tTkKr` / `U(sub-setmoh)` options (D119,
  D122, D127), the same hint on the extension (D121), and the same no-answer fallthrough to
  voicemail or hangup behind it (D29). Forwarding changes what rings and nothing else.
- **Sequential ringing, per-step timers and "press 1 to accept" are deliberately not here.** F2b
  lists them; each one is a chain of `Dial`s, a set of timers to explain and — for the confirmation
  — `app_dial`'s `M()` macro plus a subroutine and a prompt to record. Ring-all with one timer
  covers the case that was actually asked for ("ring my cell as well"), and the rest can be added
  later against a real request rather than guessed at now.
- **An external target is a `Local` channel into `[internal]`, not a second dial-out
  implementation.** `Local/7146085242@internal/n` enters exactly the context a phone dials from, so
  the number meets the outbound routes in the same order (D46), the caller ID lines those contexts
  carry (D125), and the same `[outbound-blocked]` refusal when nothing matched (D45). Dialling a
  trunk from here would have been a second copy of all of that, drifting from the first the day
  anyone edits a route. The `/n` stops Asterisk optimising the Local pair out of the call and
  taking the channel that ran the route's dialplan with it.
  - The consequence worth knowing: `TNPBX_CID` is a plain channel variable, and plain variables are
    not inherited across a `Local` channel, so a forwarded call goes out as the **route's** caller
    ID, or the trunk's — not as the original caller's claim. That is the safer of the two
    behaviours and it is one of the things the lab should confirm.
- **An extension target is that extension's endpoint**, `PJSIP/<number>`, matched against **every**
  extension row rather than the enabled ones: a target that has since been switched off is then a
  phone that does not ring, rather than a number that falls through to the `Local` form and gets
  offered to the outbound routes.
- **The toll-fraud rule is the outbound routes', reused.** A target is digits only, 2 to 15 of
  them, and **may not start with 0** — 00 and 011 are international dialling (D47, D109) — and
  `OutboundRoute.InternationalPrefix` is now the one place that digit is written down. At most four
  targets, no duplicates, and **spaces are the only separator**: `103,7146085242` is one token, so
  it fails and is named in the error rather than being quietly read as two numbers. One
  consequence: an extension whose own number begins with 0 cannot be a forwarding target, which is
  a number nobody should be handing out anyway.
- **"Is that really an extension?" belongs to the repository**, as it does for a ring group's
  members (D53): a target 2 to 6 digits long is an extension number, and if no enabled extension
  has it the save is refused. That is the rule dialling from a phone already follows — `[internal]`
  matches the extensions before it tries a route — so a mistyped `104` is a mistake to report, not
  a number to hand to a provider. An extension's **own** number is explicitly allowed.
- **A ring group still dials its members directly**, so a member's forwarding does not apply to
  group calls. Anything that reaches an extension through the internal context — a direct dial, an
  inbound route, an IVR key, a time condition — does get the forwarding, because they all arrive by
  the one door the extension's entry provides (D12, D36).
- **Not verified on the lab VM.** What a real call proves: that a `Local` leg in a ring-all `Dial`
  really does reach the outbound route and ring a mobile, which caller ID the provider is shown,
  that the mobile's own voicemail answering does not steal the call in a way that surprises anyone
  (it will — that is what the "press 1 to accept" option above exists for, if it turns out to
  matter), and that answering on the desk phone cleanly cancels the mobile leg.

### D131. Call reports: the app collects Cdrs over AMI, keeps them forever, and reports per leg (2026-09-22)

F5's open question is settled: **our app collects `Cdr` events over AMI into its own SQLite
database**, not one of Asterisk's own CDR backends. One database, one backup; the report is
plain SQL; retention is ours. `cdr_manager` needs `cdr_manager.conf` with `enabled = yes` to
send anything, and that file also maps `linkedid` and `sequence` onto the event — which is why
`func_cdr.so` joins the module allowlist (D31's `cdr_*` reservation turned out to be
`cdr_manager` only; the CDR engine itself is in the core and `cdr_core.so` does not exist).
The AMI account's `read` permission becomes `system,cdr` (D32 anticipated this).

The `Cdrs` table (schema 025) keeps **one row per Asterisk CDR record, not per call**: a
ring-all to three phones is three rows, deliberately, so the history can show who was on a
ring path and who wasn't. The unique key is `(UniqueID, Sequence)` — UniqueID alone repeats
across the legs of one call. Retention: **forever**, nothing deletes. Direction (inbound,
outbound, internal) is derived at read time from which channels are trunks, so it follows the
trunks as they are. "Missed" means not answered at a called extension, or an unanswered
inbound trunk call; an outbound call nobody picked up is not the caller's missed. The
extension filter matches both numbers and channels, so ring-group and DID calls show up. The
page opens on today in the site's timezone, with Today / Yesterday / This week / Last week
(Sun–Sat) quick buttons; the list caps at 2,000 rows while totals and CSV include everything.

**Added 2026-09-22 (readable endpoints, same piece):** the reports and the status page show
people, not channels: extensions as `number (name)`, and a Line column with the number a call
went out as (outbound) or was dialled on (inbound). The DID reaches the record because the
trunk context's first priority sets `CDR(userfield) = ${DID}`, which `cdr_manager.conf` maps
onto the event as `Did` and schema 026 stores; older rows fall back to `Dst`. A call's
originating extension is recovered from the first leg via `LinkedID` when the record itself
only shows a Local channel, as on the outgoing half of a forwarded call.

### D132. indications.conf is generated: one fixed `us` tone zone, so Dial's `r` has a ring to play (2026-09-22)

D127 added `r` to every internal `Dial` so a caller an IVR or ring group has already answered
hears ringing rather than silence. That was necessary but not sufficient: on an answered
channel `r` plays the ring tone **inband**, from the channel's tone zone, and the lab had no
`indications.conf` at all — `indication show` answered "No countries matched your criteria",
so there was no tone to play and the caller still heard silence when the IVR sent them to an
extension.

`indications.conf` is now generated like every other file in `/etc/asterisk`
(`IndicationsConfRenderer`): `[general]` with `country = us`, and a single `[us]` zone copied
line for line from Asterisk 22's `indications.conf.sample` (description, `ringcadence`, `dial`,
`busy`, `ring`, `congestion`, `callwaiting`, `dialrecall`, `record`, `info`, `stutter`). No
other countries. Tone handling is built into the core as the `indications` module, so nothing
joins the modules.conf allowlist (D31); an apply that changes the file reloads `indications`
the way it reloads `logger` and `features` — a module reload, not an Asterisk restart.

**No country setting, for now.** The zone is fixed at `us` because every site so far is in
North America and a setting is surface area nobody has asked for. If a site elsewhere needs
its own ringing and busy tones, a country setting (one value choosing among the stock zones)
is the obvious future knob; it would be decided then, not now.

### D133. Yealink phones get the web UI passwords too: `security.user_password`, from the same two settings (2026-09-22)

The user has a brand-new Yealink handset on their desk sitting on the factory "please set a new
admin password" prompt, and asked for Yealink to be set up like the Polycom phones. That prompt
is what this removes: a Yealink phone now gets its web UI passwords from provisioning, the same
way a Polycom phone has since D85.

- **The parameter is colon-form, one line per web account** (Yealink V86 admin guide,
  T2/T3/T4/T5/CP92X): `security.user_password = admin:<password>` and
  `security.user_password = user:<password>`. Yealink names its two built-in accounts `admin` and
  `user`; as with Polycom, only the password is ours to set.
- **The same two settings, no new keys.** `Provisioning.AdminPassword` and
  `Provisioning.UserPassword` already describe "the phone's web UI admin and user passwords";
  a second pair per brand would be two more secrets to keep in step for no benefit.
- **The same rules as D85.** Either one unset is left out rather than written blank, the two
  are independent, and both go through `ConfText.Safe`. They are phone-level, so a phone with no
  line key gets them too — the factory-fresh handset is exactly the one that has none yet.
- **They apply at boot**, not when the phone reads the file, which is why the Yealink reboot
  button (D134) sends a real reboot.
- The admin password is **not** a push credential for Yealink the way it is for Polycom (D85):
  Yealink has no HTTP push here (D90), so it is only ever the password on the phone's own web UI.

> **Amended same day.** The bare `security.user_password` form was silently not applied by the
> user's handset (T33G, firmware 124.86.0.118): the phone fetched the config and still sat on its
> password prompt. Yealink's own current password-recovery document (support.yealink.com, 2026)
> uses the `static.` prefix — `static.security.user_password = admin:<password>` — and a known
> FusionPBX-thread firmware quirk matches. The renderer now writes the static form, which also
> locks the value against the phone's own web UI.

### D134. Yealink gets a reboot button, and `yealink-reboot` really reboots (2026-09-22)

Amends D90 and closes the question D123 left open ("whether its reboot button should send
`reboot=true` is the user's call"). The user asked for Yealink to match the Polycom phones, and
the web passwords of D133 only apply at boot, so a Yealink phone needs a reboot button that
reboots.

- **The button is the Polycom one, unchanged**: the same `OnPostReboot` handler, the same
  confirm, the same best-effort toast, and the same disabled-with-a-reason when the phone has no
  line key or its extension has no registered contact (D123). All that changed is that
  `RebootHint` no longer refuses every Yealink phone. `PhoneNotifier.NotifyReboot` already sent
  `Event: check-sync;reboot=true` to a Yealink endpoint; nothing on the page called it for one.
- **`pjsip_notify.conf`'s `[yealink-reboot]` is now `Event = check-sync;reboot=true`.** It was
  `reboot=false` — a config re-read under a name that said reboot. The file is only for the CLI
  (`pjsip send notify yealink-reboot endpoint 1001`); the app sends its NOTIFYs over AMI with the
  headers spelled out (D123). So both types in the file now do what their names say.
- **Saving a Yealink phone still sends the re-read, not a reboot** (`reboot=false`, over AMI
  only, D90). Rebooting a handset because somebody relabelled a key would be worse than waiting,
  the same reasoning D123 gives for Polycom. A changed web password therefore reaches a Yealink
  phone at its next boot — the button, or a power cycle.
- `pjsip_notify.conf` changing means one Asterisk restart on the next apply (D123: it is in the
  startup set); after that it is fixed again.

### D135. Yealink gets dial-now rules: the eager-safe subset of D124, and nothing else (2026-09-22)

Amends D124's "Yealink gets no dial plan at all". The user asked for Yealink to be set up like
the Polycom phones, and a Yealink that waits for Send or its timer on a ten-digit number is the
most visible difference left.

- **Yealink's mechanism is eager-send only** (support.yealink.com, Dial Plan): each
  `dialplan.dialnow.rule.N` is a full-match pattern, up to 20, and when the digits typed match
  one the phone sends at once. There is no Yealink equivalent of Polycom's `T` — a rule is
  either eager or not written.
- **So only D124's eager patterns are ported**, in this order: `[2-9]11`, `[2-9]xxxxxxxxx`,
  `1xxxxxxxxxx`. The rule is D124's, unchanged: a pattern may be eager only when nothing dialable
  is longer than it and starts with it. N11 is safe because no area code is N11; ten digits is as
  long as a number without the 1 gets; eleven is as long as anything gets.
- **Not ported:** the extensions, seven-digit local and the feature codes, which are all the
  start of something longer, and the operator `0` — it is also how `011` international starts,
  and Polycom only has it timed (`0T`). All of those stay on the phone's own Send key and
  inter-digit timer, exactly as before.
- **No `dialplan.dialnow.line_id.N`**, so the rules apply to every account, and **no
  `phone_setting.dialnow_delay`**: the phone's one-second default is fine.
- **Fixed, not built per system.** Polycom's map is built from the extension list only because
  its timed extension patterns need to be (D124); every Yealink rule is independent of the
  extensions, so the list is a constant in the renderer. Phone-level, so an unassigned phone
  gets them too.
- **The test asks the question, not only the string**: it reads the rules back out of the
  rendered file, matches them the way the phone does, and asserts that no strict prefix of a
  ten-, eleven- or international number, an extension, a seven-digit number or a feature code is
  ever sent on its own.
- **Same caveat as D124:** a four- to six-digit extension that starts with an N11 — `2110`, say —
  would be sent as its first three digits. The Polycom map has shipped with the same eager
  `[2-9]11` since D124; a site numbered that way needs the rule revisited for both brands.

### D136. The destination picker is universal: everything except the entity being edited (2026-09-23)

The user asked for the ring group failover picker to be finished and, beyond that, for every
destination picker to be the same: offer the full catalog, exclude only the thing being edited.

- **Every picker offers every destination type** — extensions, their voicemail boxes, ring
  groups, announcements, IVRs, time conditions, and the always-available Hangup. Before this,
  the ring group failover offered extensions and groups only, and the IVR pickers left out
  time conditions.
- **Self-exclusion is the only filtering.** `DestinationCatalog.Except(choices, self)` removes
  the one entity being edited: a ring group's own failover, an IVR's own final destination,
  a time condition's own three cases (D63). Everything else stays, including others of the
  same kind — ring group → ring group chains (D54) and IVR → IVR ("press 9 to hear this
  again", D59) are features, not loops. An IVR's *keys* still offer their own menu (D59).
- **Ring group save validation was already catalog-wide**; what was missing was tests and the
  row labels, both of which now cover announcement / IVR / time condition failovers.
- **Amends D52** in passing: a group's failover may now name anything the picker offers, not
  only "voicemail, another group, or an extension".
- **Known gap, resolved 2026-09-23 by the user: leave loops to the admin.** Each repository's
  loop check still follows only its own kind (ring group → ring group, IVR → IVR, D59).
  Cross-feature loops — a failover to a time condition whose closed case hands back to the
  same group — are buildable from the UI and always were at save; the user decided the
  system will not police them, so no shared cross-feature walk will be built.

### D137. Call flow control: state in astdb, the phone is the switch, and a routed call can never flip it (2026-09-23)

F9, piece 36. The "closing early, flip to night mode" switch, FreePBX's Call Flow Control
reduced to what the user asked for (their answers on who toggles, the lamp and multiple
switches are in features.md).

- **One row per switch** (`027_call_flow_controls.sql`): name, feature code, and two
  destinations — normal when off, override when on — each the two fields D35 settled on.
  There is deliberately **no State column**: the live state is Asterisk's own astdb, key
  `TNPBX/CFC/<CallFlowControlID>`, written by the dialplan when a phone dials the code. A
  flip therefore takes effect on the very next call with no apply and no involvement from
  the app; the rendered dialplan reads the key at call time and is the same file whichever
  way the switch is set.
- **`*<code>` in the internal context is the toggle**, and **`cfc-entry` is where a
  destination enters a switch** — two different doors into `cfc-<ID>`, so a call routed
  through a switch can never flip it, only a phone dialling the code can. The toggle plays
  `activated` / `de-activated` (installed core sounds) so the person at the phone knows
  which way it went.
- **The lamp is a hint on the code** — `Custom:tnpbx-cfc-<ID>`, the pattern the parking
  slots use — set by the toggle with `DEVICE_STATE()`; two modules join the allowlist,
  `func_db.so` (DB()) and `func_devstate.so`, both verified to exist in the Asterisk 22
  build before listing. A phone key may target a switch: `PhoneButtons.TargetType` gains
  the kind it was left CHECK-free for.
- **`CallFlowControl` is a destination type**, so every picker offers every switch
  automatically (D136), excluding itself on its own form; chaining one switch to another
  is an ordinary destination and self-reference and CFC→CFC loops are refused at save.
- **The page badge reads astdb over AMI `DBGet`** — a read-only action inside the existing
  `write = system` permission, so `manager.conf` is unchanged — and shows Unknown without
  AMI, the RegistrationStatus pattern.
- **Found by the tests, fixed here:** every `GeneratedRegex` validator ended in `$`, which
  in .NET also matches just before a trailing newline — `"*28\n"` passed the feature-code
  check. All 51 validators now end in `\z`. Harmless before (the page trims what is posted
  and `ConfText.Safe` refuses the control character), but the uniqueness check treated the
  two as different codes.

### D138. An IVR's announcements can come back, and renumbering carries every reference with it (2026-09-23)

Piece 37, both halves user-requested and approved.

**Announcements return to the menu, per IVR.** `Ivrs.ReturnAfterAnnouncement` (schema `028`,
default 0) — one checkbox on the IVR form, applying to that menu's announcement destinations
only. When on, such a key plays the announcement's audio in the menu's own context and
`Goto(s,1)`: the caller re-enters the menu exactly like a fresh dial, so the retry count
resets and no Gosub stack builds. The final destination never returns — a caller pressing
nothing would loop forever. Off is byte-identical to before; a key whose announcement has
become unplayable falls back to what was written before the flag existed.

**Renumbering sweeps every reference, in one transaction.** Destinations are stored by
number (D35, no FKs), so a changed number used to orphan them. `Renumbering` is one helper
the entity repositories call from their `Update`: the sweep rewrites references first, the
entity's own row second, so a refusal on the row's UNIQUE constraint (the mid-way failure)
rolls everything back. A reference is rewritten only on an exact type+value match — never a
substring. What follows a renumber:

| Renumber | What is rewritten |
|---|---|
| IVR play extension | every stored Ivr destination, plus its own "press 9 to hear this again" keys |
| Ring group number | every stored RingGroup destination |
| Extension number | Extension and Voicemail destinations, `Forwarding` lists on every extension (D130), ring group members, phone Line and BLF keys |
| Time condition play extension | every stored TimeCondition destination |
| CFC feature code | every stored CallFlowControl destination, phone CFC keys |

Ten stored destination column pairs are covered (inbound routes, IVR keys and final, the
three time-condition cases, time-condition holiday rules, ring group failover, both CFC
destinations). The apply marker rises, so nothing goes live until Apply. **Deletes never
cascade** (D35: references to a deleted thing show as gone and hang up), and clearing a
number counts as a delete. **Not cascaded, on purpose:** voicemail messages on disk stay in
the folder named by the old extension number — moving them is a file operation that belongs
to the Helper (piece 19), not the web app. **Open, for the user:** whether an extension
renumber also pushes a config re-read (check-sync) at phones holding that line.

### D138 amended. Extensions cannot be renumbered at all (2026-09-23, user decision)

The user: "we dont allow extensions to be renumbered... that usually never happens." The
extension sweep built this morning is removed. `ExtensionRepository.Update` refuses a
changed number outright — "delete it and create a new extension instead" — because the
number is the extension's identity: it names the phone, the mailbox on disk and every
stored reference, and the honest path is delete-and-recreate (references then show as
"gone" and hang up, D35). The IVR, ring group, time condition and CFC renumber cascades
stand unchanged.

### D139. Admin access is gated by Entra app assignment, not an app role (2026-09-23, user decision)
The enterprise app's **User assignment required** is set to Yes in Entra, with self-signup
disabled; the Entra admin assigns users or groups explicitly. Entra refuses sign-in for anyone
not assigned, so no unassigned tenant user can ever reach a session, and TNPBX adds no
`Pbx.Admin` role check of its own — every assignee is an admin, which is the current model.
An app role is only worth adding if the UI ever needs access *levels*; until then it is
surface area. Closes the "any user in the Entra tenant can sign in as an admin" known gap in
[security.md](security.md#known-gaps). Operational note: assigning/removing admins is now an
Entra-side workflow, outside this app.

### D140. Bypass is always on; shipping default is loopback only (2026-09-23, user decision)
`LocalAuthenticationBypass` ships `Enabled` with `AllowedNetworks = [ "127.0.0.1/32" ]` — a
fresh install is safe by default while still giving an on-box break-glass path (Entra down,
admin on the console). Deployments widen the list to their trusted networks (the lab carries
the VM subnet and Jaysam's VPN ranges) and leave it on permanently; that is the accepted
posture, not an outage-time toggle. Per-request IP logging and the startup WARNING stay the
audit trail.

### D141. fail2ban: tnpbx jail on Asterisk security events, whole-IP nftables bans (2026-09-23)
Piece 20's fail2ban half (D7's second half — the own Helper/AMI blocker — stays deferred).
Config lives in the repo at `scripts/fail2ban/` and `install.sh` copies it to `/etc/fail2ban`;
the server never carries a hand-edit. The filter matches only the failure SecurityEvents
(`InvalidPassword`, `InvalidAccountID`, `ChallengeResponseFailed`, `FailedACL`), capturing the
address from `RemoteAddress="IPV4/UDP/<ip>/<port>"` — the `SuccessfulAuth`/`ChallengeSent`
handshake noise of every healthy REGISTER never matches. Jail: polling backend, `maxretry 5`
in `findtime 10m`, `bantime 24h`, `action = nftables-allports` (the box's only public services
are SIP/RTP, provisioning and the admin UI — a source attacking one gets dropped from all),
and `ignoreip` = loopback, VM subnet, and the admin VPN ranges so a misconfigured phone or a
NATed admin path can never lock the office out. Lab-verified 2026-09-23: the filter matched 14
real lines from an actual credential-guessing scanner (208.100.60.35, Sep 22) in the lab's
existing security.log and nothing else in 46k lines; five planted failures banned 203.0.113.77
into the `f2b-table` nft set within seconds, and five from an ignoreip range banned nothing.

### D142. The Helper: one Unix socket, SO_PEERCRED, typed messages, root-owned binary (2026-09-23)
Piece 19. The privileged helper of D3 is built, and this is the shape it settled into.

**The socket.** `/run/tnpbx/helper.sock`, never TCP. Newline-delimited JSON: one request object,
one reply object, connection closed. Request is `{"type":"ping"}`, `{"type":"firewall.status"}` or
`{"type":"firewall.apply","rules":[…]}`; reply is `{"ok":true,"result":…}` or
`{"ok":false,"error":"<one human sentence>"}`. The messages live in `Techie.Pbx.Contracts`, which
stays dependency-free. **There is no message that carries a command, a path or a shell string, and
there will not be one** — that is the whole reason this process exists instead of a sudo rule.
Unknown fields deserialize to nothing; an unknown protocol name or a numeric enum value is a
deserialization failure, not a value anything downstream has to decide about.

**The authentication model, in full.** The kernel reports the connecting process's UID
(`SO_PEERCRED`) and it must equal the UID of the `tnpbx` system user, resolved once at startup with
`getpwnam` — the Helper refuses to start if that user does not exist, because there would be nobody
the socket could be for. Any other UID is logged and closed before a byte is read off it. Nothing
the caller *says* about itself is an input. Two things back that up rather than replace it: the
socket directory is 0750 `root:tnpbx` and the socket 0660 `root:tnpbx`. The directory's mode is set
*before* the bind, which closes the moment between `bind()` creating the socket with the umask's
permissions and the `chmod` that follows. `tnpbx-web.service` gains `SupplementaryGroups=tnpbx`,
because its `Group=asterisk` (D18) means systemd would not otherwise give it that group.

**Validation happens at both ends.** Every rule is validated on the web side so a bad one never
leaves, and again in the Helper so a bad one never arrives, and a third time in the renderer before
anything is written — the same "never trust the row" rule the conf renderers follow. A rule is a
protocol (`tcp`/`udp`), a port range inside 1–65535 with start ≤ end, and a label of 1–40
characters from letters, digits, space, dash and slash. Thirty-two rules per message at most. The
label's character set is that narrow because it is written into the generated ruleset as the nft
comment, so `nft list ruleset` on the box says what each open port is for; anything that could end
that quoted string is refused rather than escaped.

**The safety rules are by construction, not by review.** The Helper always writes loopback,
established/related, ICMP and TCP 22 itself, before anything a message carried, from data no
message can reach. A `firewall.apply` can only ever widen what follows them. That is what makes it
safe to put a red Apply button in a web UI: there is no message content, malicious or mistaken,
that locks the machine out.

**nft is never a command line.** `ProcessStartInfo` with `FileName = /usr/sbin/nft` (a fixed
absolute path, so nothing searches `PATH` for a program to run as root) and `ArgumentList`, no
shell, ever. Every argument is a constant or a path the Helper chose. A ruleset is written to
`/var/lib/tnpbx-helper/pending.nft` (0700 root directory), checked with `nft -c -f`, then loaded
with `nft -f`; a failure at either step deletes the temp file and leaves the running ruleset
exactly as it was, and the reply carries nft's own words. On success the text is kept as
`last.nft` (0600) and re-applied at startup, so a reboot comes up protected. The rules and the
timestamp are kept beside it in `last.json`, because the Helper is not in the business of parsing
nft syntax back and a status page on a rebooted box must not say "never applied".

**`firewall.status` reports what the Helper applied, not what the kernel has.** The comparison
against what the system *expects* happens in the web app, which is the only end that knows that
`Sip.Port` means a UDP hole: `FirewallRulesBuilder` derives the expected list from the settings, the
`RtpConfRenderer` port constants and `WebBindings`, so the firewall and what is actually listening
cannot drift. "Somebody ran nft by hand" is deliberately not something this page pretends to detect.

**`/opt/tnpbx-helper`, root:root 0755 — not `/opt/tnpbx`.** `app-deploy.sh` chowns the app's tree
to the `tnpbx` user, so a helper binary in there would be a root binary the web user could rewrite,
which is precisely the escalation this design exists to prevent. Same reasoning as
`bin/voicemail-mail` (D126), one step further. The unit and the tmpfiles entry live in the repo
once and travel in the web publish beside that script; `install.sh` installs them from the repo and
enables the service, `app-deploy.sh` installs the binary and starts it.

**The unit is the hardening set a root service can actually take:** `NoNewPrivileges`,
`PrivateTmp`, `ProtectHome`, `ProtectSystem=strict` with `ReadWritePaths=/var/lib/tnpbx-helper`,
`RuntimeDirectory=tnpbx` (which is what lets it own its socket directory at all under
`ProtectSystem=strict`), `ProtectClock`, `ProtectHostname`, `ProtectKernelLogs`,
`ProtectKernelTunables`, `RestrictSUIDSGID`, `LockPersonality`, and
`RestrictAddressFamilies=AF_UNIX AF_NETLINK` — which turns "listens only on a Unix socket, never
TCP" from a rule somebody has to keep into one the kernel keeps. Deliberately not set:
`PrivateNetwork` (the job is this host's packet filter), `ProtectKernelModules` (nft loads
`nf_tables` on first use) and `PrivateUsers`.

Connections are served one at a time, single threaded: an apply is two nft commands against one
kernel ruleset, so concurrency buys nothing and costs readability. Both socket directions carry the
same five second timeout the client does.

### D143. Firewall policy: one `inet tnpbx-input` table, policy drop, fail2ban's untouched (2026-09-23)
What the Helper generates, decided once so it is not re-argued per rule.

One table, `table inet tnpbx-input` — `inet`, so a single ruleset covers IPv4 and IPv6 and there is
no second copy to keep in step. One base chain, `input`, `type filter hook input priority 0`, and
**`policy drop`**: a default-accept firewall with accept rules in it is a list of opinions, not a
filter.

The rules, in this order and no other:

1. `iif "lo" accept` — the box talking to itself, AMI included.
2. `ct state established,related accept` — every reply to something this box started.
3. `meta l4proto { icmp, ipv6-icmp } accept` — ping and, more importantly, the ICMP errors that
   path MTU discovery needs. Silently dropping those is how "SIP works but big packets vanish".
4. `tcp dport 22 accept` — SSH, so the way back in is never a thing a firewall change can take away.
5. Then one `<proto> dport <range> accept comment "<label>"` per rule the message carried.

The first four are written by the renderer from nothing a message can influence (D142).

**fail2ban's `f2b-table` is a separate table at priority -1 and is neither in our file nor flushed
by it.** Lower priority means it runs first, which is the right way round: a banned source is
dropped before our accepts are reached. Our file adds the table, deletes it, and builds it again —
`nft -f` is one transaction, so that replaces exactly our table with no moment where the ruleset is
half loaded, and nothing else on the box is touched. There is no `flush ruleset` anywhere in it.

Not done, on purpose: no per-address or per-CIDR rules (source restriction is the customer's cloud
firewall or a later piece), no rate limiting on 5060 (fail2ban owns that, D141), and no outbound
filtering at all — an `output` chain on a PBX that has to reach trunks, ACME, Graph and NTP is a
support burden with very little to show for it.

### D144. Polycom on-call soft keys: a fixed Park + Blind Xfer pair (2026-09-23)
Every provisioned Polycom gets two soft keys appended to the active-call set, the same for
all of them — no per-phone soft key editor, because a fixed pair is surface-area-free and the
per-phone case has not been asked for (user decision 2026-09-23).

- **Blind Xfer**: Polycom's built-in `$Fblindxfer$` action, always present.
- **Park**: an EFK that **sends DTMF `*3` mid-call** (the configurable `Parking.DtmfCode`),
  not a blind transfer. `*3` is the features.conf `parkcall` featuremap entry (D119), not a
  dialable dialplan extension — there is no `exten => *3` to REFER a call to, and adding one
  would be new dialplan surface for no gain. The user chose "blind-transfer to the *3 park
  code" by its behavior, and DTMF delivers the same behavior: next free slot, spoken slot
  announcement, lamp keys showing where it landed. Park is written only when parking is
  enabled; a Park key that 404s is worse than no key.

### D145. Polycom background logo: one site-wide image through the provisioning gate (2026-09-23)
One uploaded image, PNG or JPEG, set as the idle background on every provisioned Polycom —
site-wide, not per-phone (user decision 2026-09-23; per-phone is a later piece only if asked for).

- **Served through the existing `/polycom` provisioning endpoint**, behind the same basic auth
  the phones already send for their config — not an anonymously readable wwwroot directory.
  A world-readable file dir is new attack surface for zero benefit: every phone that wants the
  logo already holds credentials. No new firewall exposure, no new path to guess.
- **Stored in the app's data directory** like announcements and MOH tracks, not in wwwroot at
  all — wwwroot ships with the app and would put uploads inside the deploy tree.
- **Validate, don't convert**: magic-byte check that the upload really is a PNG or JPEG, a size
  cap, and that is all. No server-side resizing per model — the phone scales whatever it gets,
  and per-model image generation is surface area for a marginal payoff.
- Config written into the generated `exten<mac>.cfg`: `bg.background.enabled` plus
  `bg.color.bm.1.name` pointing at the gated URL. No image (never uploaded, or removed) means
  the `bg` block is not written — the factory background stays.

**Amended 2026-09-23, after the desk test.** Park worked on a real Edge E450; Blind Xfer via
`$Fblindxfer$` did nothing — that macro is not acted on by the Poly Edge's firmware. The EFK + named
macro + softkey mechanism itself is proven (Park), so the fix is Poly's own FAQ recipe (example 6):
Blind Xfer becomes a second EFK, `$P1N{n}$$Trefer$` — a prompt that collects the destination digits
and issues a SIP REFER, which is what a blind transfer is. `n` is the site's extension length,
derived from the extension set at render time (configs are generated per fetch, so the prompt
always matches the numbering with no new setting). The prompt (`efk.efkprompt.1.*`, label
"Transfer to:", numeric, visible, digitmatching none) rides in the same `<efk>` element.

### D146. The line display name is "Ext - Name" (2026-09-23)
Every provisioned phone shows its main line as the extension number, a space-dash space, and the
extension's name — "100 - Jaysam" — rather than the number alone or the name alone. The desk test
showed both failure modes: an Edge 450 showing bare "100" and a VVX 311 showing bare "Michael",
and a user glancing at a phone wants both. Polycom `reg.1.displayName` and the Yealink equivalent
are rendered from the same composition, so both brands show the identical shape. An extension with
no name shows the number alone — a dangling dash is worse than the plain number.

### D147. `res_pjsip_refer` joins the module allowlist: transfers are REFER (2026-09-23)
Every transfer a Polycom or Yealink makes — blind or consultative — is a SIP REFER, and Asterisk
only answers a REFER when `res_pjsip_refer.so` is loaded. It was not on our strict allowlist, so the
desk test saw every transfer do nothing: the native consultative Transfer rang the far side fine
(the INVITE path) but completing it was a REFER that Asterisk refused, and the soft keys were the
same refusal. Park worked throughout because it is DTMF into the featuremap, not a REFER (D144).
Not a phone-config bug at all — the provisioning from pieces 38/40 was right and is unchanged.

### D148. Parking-slot keys are attendant type `normal`, extension lamps stay `automata` (2026-09-23)
A slot's lamp is lit exactly when pressing the key must dial the slot and retrieve the call — but
`automata` is attendant-console semantics: a busy (lit) resource makes the key attempt a directed
call pickup instead of dialling, and with no pickup code in our dialplan that is a silent nothing.
That was the desk symptom: slot busy → press → nothing; slot empty → press → dials and reaches the
"nobody parked" playback. Type `normal` keeps the lamp and makes the key dial unconditionally, which
is what retrieval is. The FreePBX module's slot keys carry no type for the same reason — its
generated configs are the production-proven shape.

**Amended the same day, on the user's call: every key is `normal`, extensions and call flow
controls included.** A day/night key set on a Poly Edge showed it: the control switched on — the
lamp lit — and the lit key's pickup attempt meant it could never be switched back off. A key whose
action changes with its lamp is not a key anyone can trust, whatever the target is.

### D149. Phone clocks: the phone's own daylight-saving rule is off and both time values override DHCP (2026-09-23)

The user's Edge E450 showed 6pm at 5pm — exactly one hour fast. The served config was fetched from
the lab VM before theorizing: `tcpIpApp.sntp.gmtOffset="-25200"` (correct PDT, computed at
generation time as D82 records) but no `daylightSaving` setting, so the phone applied its own
factory DST rule **on top** of a DST-correct offset. Double daylight saving, one hour fast.

D82's design already assumed the phone adds nothing on its own; the missing half was saying so.
The renderer now writes, into the same `tcpIpApp` element:

- `tcpIpApp.sntp.daylightSaving.enable="0"` — D82's generation-time offset is the whole truth;
  the phone's DST rule is off so it cannot add an hour a second time.
- `tcpIpApp.sntp.address.overrideDHCP="1"` and `tcpIpApp.sntp.gmtOffset.overrideDHCP="1"` —
  pin both values over whatever the site's DHCP server may be offering for the time, so the
  config and the clock cannot disagree about who is in charge of either.

The D82 caveat stands and is now the whole trade: a phone crosses a DST boundary at its next
config poll (86400s), not the moment the clocks change. That is accepted rather than teaching
every phone brand each zone's DST rule — one rule per phone family is more surface than one
number the server already knows. Yealink's `local_time` block is untouched: its defaults leave
the phone's DST off already (D92's not-verified note still applies).

Verified live: deployed to the lab VM, the served config carries all three lines, and a
`check-sync` NOTIFY pushed it to the registered Edge (contact (remote, NAT-mapped)) — desk confirmation
is the user's.

### D150. Phone clocks, the fix that held: the STANDARD offset and the phone's own DST rule (2026-09-23, amends D149 and D82)

D149's fix did not hold: the desk Edge E450 rebooted onto the new config — offset -25200 with
`tcpIpApp.sntp.daylightSaving.enable="0"` and both `overrideDHCP` flags — and still showed 6pm at
5pm. The phone applies its own daylight-saving adjustment whatever the config says about it.

The production reference had the answer all along: the user's FreePBX module offers only
**standard** offsets (`-28800` labelled "GMT -8:00 Pacific Time") and writes no daylightSaving
parameter at all. The phone's DST rule (on by default — which is why it added an hour even when
unset) supplies the summer hour on top. Production phones stayed correct year-round that way.

- `GmtOffsetFor` now returns the zone's **standard** offset: the smaller of its fixed January-15
  and July-15 UTC offsets, because daylight saving always adds, in either hemisphere; the two are
  equal for a zone with no DST. Deterministic, no generation-time drift.
- The `daylightSaving.enable` line is **gone** — unset, exactly like the module: the phone's own
  rule owns the summer hour.
- Both `overrideDHCP` flags stay (D149): provisioned NTP address and offset win over DHCP.

This retires D82's caveat entirely: a phone now crosses DST boundaries at the moment the clocks
change, on its own rule — not at its next config poll. The trade it takes on instead: the phone's
rule is a built-in US/EU calendar, so a site whose zone keeps different dates (or none —
Arizona-style) needs a setting someday; none of this user's sites do.

The Yealink renderer keeps the current-offset shape (D92): a Yealink's `local_time` block carries
its own DST switches and its default leaves them off, so its offset is applied as-is and it
self-heals at the next config poll. Not desk-verified yet, unchanged.

Verified live: deployed to the lab VM, the served config carries `gmtOffset="-28800"`, and a
`check-sync;reboot=true` NOTIFY rebooted the registered Edge onto it — desk confirmation is the
user's.

### D151. Polycom background: the parameter shape, where the image lives, and how a phone gets it (2026-09-23, piece 39, implements D145)
- **Three parameters, not two.** The user's FreePBX module (`~/polycomphones`) has no background
  handling at all, so it could not supply the shape. Poly's own documents do — the Lens FAQ
  "custom background" recipe and the Edge E admin guide (chapter 14) — and both give the same
  three lines: `bg.background.enabled="1"`, `bg.color.selection="2,1"` ("custom background,
  index 1"), and `bg.color.bm.1.name="<absolute URL>"`. D145 named only the first and the last;
  the selection is the guides' step that makes the phone use the image, so it is written too.
  No `bg.color.bm.1.em.name`: expansion modules are not something this system knows about.
- **Written into `exten<mac>.cfg` in a `<bg>` element after `<da>`**, for every phone including
  an unassigned one — the background is phone-level, like the time. No image means no element
  and no comment: the file is byte-for-byte what it was before (the existing golden files are
  unchanged; `polycom-phone-background.cfg` pins the with-image state). The renderer re-checks the
  URL is absolute http/https before the usual `ConfText.Safe` + XML escaping.
- **Stored in the data folder** beside the database (D25) as `Data/polycom-background.png` or
  `Data/polycom-background.jpg` — one fixed name per format, never a browser-supplied name, and
  saving one format removes the other. **No schema change and no settings key**: the file existing
  is the whole state, and `app-deploy.sh` already preserves `Data/`. The upload is spooled in the
  same folder and renamed into place, so a phone fetching mid-save gets the old image or the new.
- **Served as `/polycom/background.png` or `/polycom/background.jpg`** by `PolycomController`,
  after the same basic-auth and Polycom User-Agent gates as the config files. Only the name
  matching the stored format answers; anything else is a 404. The URL in the config is built the
  way the Yealink provisioning URL is: request scheme + phone-facing host name (`System.Hostname`
  or the request host, D105) + `/polycom/` + the file name, no port (80 always binds and
  `/polycom` is exempt from the HTTPS redirect, D77/D99). The name ends in the real extension
  because the guides name the image file in the URL.
- **No credentials in the URL.** The phone is expected to answer the 401 challenge with the
  provisioning credentials it already used for the config. **Not verified on a desk phone** — if
  a Poly does not, the fallback is to embed `user:pass@` the way DHCP option 160 does, which
  would need a decision of its own because it writes the provisioning password into every config.
- **UI:** one status line and a "Background image…" button above the tabs on the Phones page,
  opening an upload/remove form in the shared modal (204 + `HX-Trigger` of
  `phoneBackgroundChanged` and `pbxToast`). Upload and remove change nothing else — no apply, no
  push to the phones; each phone picks it up at its next config fetch or a reboot.

### D152. Background image validation: PNG or JPEG by magic bytes, capped at 2 MB (2026-09-23, piece 39)
- **Magic bytes decide, never the name or content type**: the full eight-byte PNG signature
  (`89 50 4E 47 0D 0A 1A 0A` — all eight, since the line-ending bytes exist to catch a file mangled
  as text) or a JPEG's `FF D8 FF`. Anything else — GIF, BMP, SVG, a text file called `logo.png` —
  is refused with the current image untouched. No decoding, converting or resizing (D145): the
  phone scales what it gets.
- **Cap: 2 MB**, enforced while the upload is spooled, not after, and the Phones page's request
  limit is the cap plus 1 MB of multipart overhead, as MOH and announcements do theirs. In the same
  spirit as those caps (20 MB / 40 MB for audio that is minutes long): the largest Poly screen is
  about 1024x600 and an image that size is a few hundred kilobytes, so 2 MB allows an unoptimised
  export without handing every phone something large to fetch at each boot.
- **Not checked: progressive JPEG**, which Poly says the phones do not display. Telling it apart
  means walking the JPEG's markers, which is parsing rather than a signature check; the upload
  form says "not a progressive JPEG" instead.

### D153. Polycom logo, and both images must be exactly one of Poly's sizes (2026-09-24, piece 41, amends D145 and D152)
- **A site-wide logo beside the background.** One uploaded PNG or JPEG, kept exactly as the
  background is (D151): `Data/polycom-logo.png` or `Data/polycom-logo.jpg`, one fixed name per
  format, same magic-byte check and 2 MB cap (D152), spooled and renamed into place, no schema
  change and no settings key. Served as `/polycom/logo.png` or `/polycom/logo.jpg` behind the same
  basic-auth + Polycom User-Agent gate; only the name matching the stored format answers.
- **Rendered as `bg.logo="<absolute URL>"`** inside the same `<bg>` element as the background,
  after the background's three lines when both exist. The parameter name is the user's, from Poly's
  parameter reference (not yet checked against a desk phone). No logo means no line and the phone
  keeps Poly's own; neither image means no `<bg>` element, so every existing golden file is
  unchanged. The URL is built and re-checked (absolute http/https, then `ConfText.Safe` + XML
  escaping) exactly as the background's is.
- **Exact pixel sizes, amending D145/D152's "validate, don't convert — the phone scales it".**
  Poly's Edge E admin guide gives optimal sizes per screen class: background **320x240** (E100,
  E220, E300, E400 series) or **800x480** (E500 series); logo **60x26** (E100–E400) or **182x78**
  (E500). An upload must be exactly one of the two for its kind, or it is refused with a message
  naming the accepted sizes and the file's own (`The background image must be exactly 320x240 or
  800x480 pixels; this file is WxH.`). Strict on purpose (user decision 2026-09-24): these are
  site-wide images for one mixed-model, mostly-E450 fleet, and an image of any other size is one
  the phone rescales or crops — the admin should hear that at upload, not see it on a desk.
  Still no converting or resizing on our side.
- **Width and height are read from the header, with no image package**: PNG's IHDR chunk (fixed
  offset, big-endian), and for JPEG a walk of the segment markers by their length fields to the
  first start-of-frame (C0–CF less C4/C8/CC). A file whose size cannot be read — truncated, or a
  JPEG that reaches its scan data first — is refused. Still not checked: progressive JPEG (D152),
  though the marker walk now sees the frame type and could.
- **Shared, not duplicated:** `BackgroundStore` and `LogoStore` are thin subclasses of
  `PolycomImageStore`, which holds the spool/validate/rename logic and differs per image only in
  file stem, name and accepted sizes. No interface.
- **No UI yet.** The logo has no upload form; that is a later run. Until then a logo can only be
  put in place by hand on the server, and the background form's hint now states the exact sizes.

### D154. Polycom background and logo: any size is accepted and resized, not refused (2026-09-24, piece 41, amends D153)
- **Resize, don't reject (user decision 2026-09-24).** D153's exact-size rule refused anything
  that was not one of Poly's sizes. Now any size of PNG or JPEG is accepted and the server makes
  it into the one size the site serves. D153's refusal, its multi-size list and its message are
  gone.
- **One target per image, the E400-series size**: background **320x240**, logo **60x26**. Poly
  still publishes a size per screen class (D153), but a site has one background and one logo for
  a mixed, mostly-E450 fleet, so both are served at the E100–E400 size. An 800x480 or 182x78
  upload (the E500 sizes) is no longer special — it is resized like anything else.
- **How it is fitted.** The background is scaled to **cover** 320x240 and centre-cropped: the
  screen is always filled and the edges are what is lost, as with any wallpaper. The logo is
  scaled to **fit inside** 60x26 and centred on transparent padding, so a wordmark is never
  cropped or distorted. The crop is taken in the source's own pixels before scaling, so an
  extreme shape (1x3000) never makes the resampler produce anything bigger than the target. A
  photo's Exif rotation is applied first.
- **Output is always PNG after a resize** — the logo needs the alpha channel, and one format
  keeps it simple — with no metadata carried over. **An upload that is already exactly the
  target size is stored byte for byte, in its own format, never decoded or re-encoded**: what
  the admin made is what the phone gets. So a 320x240 JPEG is still stored and served as
  `.jpg`, and both file names and both golden files stay valid.
- **Still D152/D153 at the front door.** The 2 MB cap is on the uploaded file, enforced while
  it is spooled and before anything decodes it; the magic-byte check still decides PNG or JPEG;
  the header-read size (`ImageDimensions`, no decode) is what decides "already the right size".
  Only then does anything decode it. Storage names, the `LogoStore`/`BackgroundStore` split,
  serving paths and the `bg`/`bg.logo` rendering are unchanged.
- **A pixel cap before decoding: 25 megapixels.** The byte cap does not bound what a file
  decodes to — a 50 KB PNG can declare 6000x5000 — so the header is identified first and
  anything over 25 megapixels (100 MB decoded at four bytes a pixel) is refused with a message
  naming its size. That is past a 24-megapixel camera photo. A file that cannot be decoded is
  refused with a message, and a refused upload never touches the stored image.
- **First image dependency: SixLabors.ImageSharp 3.1.12, in `Techie.Pbx.Asterisk` only** (and
  the test project, to build real fixtures of any size). Decoding PNG and JPEG and resampling
  well is a great deal more code, and more risk, than a maintained library; the header reads
  D153 hand-rolled were a few lines, a decoder is not. Fully managed (no native libraries, so
  nothing new for the installer's package list or the single-file publish). It is handed a
  configuration that registers the PNG and JPEG codecs only, so its other decoders (GIF, BMP,
  TIFF, WebP, …) are unreachable from an upload. **Licence:** the Six Labors Split License, which
  grants Apache 2.0 to software consuming it under an open-source licence — TNPBX is GPL-3.0,
  and Apache 2.0 is GPLv3-compatible. A closed-source fork by a company over US$1M revenue would
  need Six Labors' commercial licence.
- **The form says so.** The background upload hint now says any size is accepted and that the
  server resizes to 320x240 (keeping a 320x240 upload as it is). The logo still has no upload
  UI (D153); when it gets one its hint says the same for 60x26.
