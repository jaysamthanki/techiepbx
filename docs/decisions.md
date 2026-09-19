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

### D91. pjsip_notify.conf is a generated conf, and res_pjsip_notify joins the allowlist (2026-09-21)
The NOTIFY categories (tnpbx-check-cfg = Event: check-sync, tnpbx-reboot = check-sync;reboot=true)
are generated into **pjsip_notify.conf** — the file Asterisk 22's res_pjsip_notify actually reads
(`notify.conf` belongs to the dead chan_sip; discovered live on the lab VM when the module
declined to load) — and res_pjsip_notify.so joins the modules.conf allowlist to load it.
Per D33, pjsip_notify.conf is written but never live-reloaded: an apply that changes it reports
a restart, which is rare since the categories are fixed.

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
