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

See [docs/roadmap.md](docs/roadmap.md).
