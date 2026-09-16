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
