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


