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
