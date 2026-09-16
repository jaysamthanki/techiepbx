# TNPBX

A deliberately small FreePBX alternative. A .NET 10 web app that installs onto a fresh Debian
server and manages Asterisk. FreePBX's problem is surface area: most sites use about 5% of
its features, and the other 95% is attack surface and confusion. **Every feature added here
has to justify its surface area.** When in doubt, leave it out and ask.

The agreed scope is [docs/features.md](docs/features.md). Don't build features that aren't on
it, and where it lists an open question or implementation choice, ask rather than pick.

Start with [docs/README.md](docs/README.md). Read [docs/architecture.md](docs/architecture.md)
and [docs/security.md](docs/security.md) before changing anything structural.

## Working model

- A supervising agent (Hermes) manages the project, deploys to the lab VM and runs tests there.
  Claude writes code under that supervision.
- Work in small, reviewable pieces. Each piece should build, pass tests, and leave the docs accurate.
- When a decision is made (by the user or during review), record it in
  [docs/decisions.md](docs/decisions.md). Update [docs/roadmap.md](docs/roadmap.md) when a piece lands.

## Layout

```
Techie.Pbx.slnx
Directory.Build.props          shared: net10.0, nullable, implicit usings
src/
  Techie.Pbx.Web/              Razor Pages UI + API controllers, Entra ID (cookie) auth. Runs unprivileged.
  Techie.Pbx.Core/             Models, validation, SQLite data access (Dapper), secrets
    Data/Schema/NNN_name.sql   numbered schema scripts (embedded resources)
    scripts/                   shell scripts (lab VM build, later the installer)
  Techie.Pbx.Asterisk/         Config renderers, atomic conf writer, (next) AMI client
  Techie.Pbx.Contracts/        Typed messages between Web and Helper. No dependencies.
  Techie.Pbx.Helper/           Root helper on a Unix socket, fixed command allowlist (stub)
tests/
  Techie.Pbx.Tests/            xUnit. Expected/ holds the known-good generated conf files.
docs/                          architecture, decisions, security, database, lab, roadmap
```

References: Web → Core, Asterisk, Contracts. Asterisk → Core. Helper → Core, Contracts.

## Commands

```bash
dotnet build Techie.Pbx.slnx
dotnet test Techie.Pbx.slnx
dotnet run --project src/Techie.Pbx.Web
```

## Conventions (follow these, the user cares about them)

**C#**
- **Code style (the user cares about these; apply to all new code, and to existing code when you touch it):**
  - Private members are camelCase with **no underscore prefix** (`confDirectory`, not `_confDirectory`). Underscores are a waste of time.
  - Properties and methods are PascalCase, always.
  - Group all properties together **above the constructors**, in alphabetical order.
  - Group methods together after the constructors, in alphabetical order (test classes exempt).
  - Use `this.` where possible (`this.confDirectory`, `this.Apply()`) — it helps the reader see where the member comes from.
- **Minimal dependency injection.** Construct things with `new`, use static classes for pure
  functions (e.g. renderers). Only use DI where ASP.NET Core forces it (auth, Razor Pages).
  No interfaces that exist only for DI or mocking.
- **Logging is log4net.** `private static readonly ILog Log = LogManager.GetLogger(typeof(X));`
  Framework logs are bridged into log4net in `Program.cs`. Don't inject `ILogger<T>`.
- Namespaces match folders: `Techie.Pbx.<Project>.<Folder>`.
- Block-scoped namespaces (`namespace X { ... }`), as in the existing code.

**Database**
- SQLite via **Dapper** + `Microsoft.Data.Sqlite`. No EF Core.
- **Primary keys are `<Entity>ID`**, e.g. `ExtensionID`, never `Id`, in both the column and the
  C# property. Foreign keys use the same name as the key they point at.
- Tables and columns are PascalCase (`Extensions.Number`). Tables are plural.
- Schema changes: add a new `Data/Schema/NNN_description.sql`. **Never edit a script that has
  shipped**; `PRAGMA user_version` tracks which have run. See [docs/database.md](docs/database.md).
- Repositories validate before writing and throw `ValidationFailedException` for user errors.

**UI**
- **Allowed client libraries (and nothing else without asking):** Bootstrap, **bootstrap-table**,
  **sweetalert2**, and **htmx**. All vendored locally under wwwroot/lib — no CDN, no npm build step.
- **Modals:** create/edit/delete **forms live in Bootstrap modals** — a server-rendered partial
  (htmx `hx-get` loads it into the modal body, the form posts via htmx and swaps back validation
  errors or a 204 + `HX-Trigger`). **sweetalert2 is only for alerts, confirms and toasts** —
  never for forms.
- **htmx** is the workhorse: Razor Pages returns HTML partials, and htmx handles table refresh,
  modal submit, and polling (`hx-trigger="every 5s"`) for live status like registration state.
  JavaScript we write ourselves stays minimal — a few lines of glue, not a framework.
- Lists are **tables** (bootstrap-table); create/edit/delete happen in **modals**.
- API controllers use the same Entra ID cookie as the pages. Only our own pages call the API.

**Asterisk config**
- The database is the source of truth. Conf files are generated, never hand-edited.
- Renderers are pure static functions (data in, text out) with golden-file tests in
  `tests/Techie.Pbx.Tests/Expected/`. If you change output, update the expected file and
  explain why in the PR.
- Every value written to a conf file goes through `ConfText.Safe` and renderers re-validate
  models. Never write user input into a conf file any other way.
- Write files only with `ConfFileWriter.WriteAtomic`.

## Security rules (non-negotiable)

- The web process never runs as root and never uses sudo.
- The Helper never accepts a free-form command, path or shell string. Only typed messages
  from `Techie.Pbx.Contracts` with validated arguments. No `bash -c`, no string-built commands.
- Don't widen what Asterisk loads or listens on without a decision recorded in docs/decisions.md.
- No secrets in the repo, docs or logs. Lab credentials live on the VM only.

## Current state

See [docs/roadmap.md](docs/roadmap.md). As of 2026-09-13: solution scaffolded, lab VM running
Asterisk 22 (echo test works), extension model + schema + repository + pjsip/extensions
renderers + atomic writer done with tests. Next: AMI client and "apply config".
