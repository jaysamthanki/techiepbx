# Database

SQLite, accessed with Dapper over `Microsoft.Data.Sqlite`. One file, owned by the web
service user, mode 0600.

## Conventions

- **Primary key:** `<Entity>ID INTEGER PRIMARY KEY`, e.g. `ExtensionID`. Never `Id`.
- **Foreign keys:** same name as the key they reference (`Voicemail.ExtensionID` → `Extensions.ExtensionID`).
- **Names:** PascalCase. Tables are plural (`Extensions`), columns singular (`Number`).
- **Booleans:** `INTEGER NOT NULL` with 0/1.
- **C# models** use the same names as the columns, so Dapper maps them with no configuration.
- Foreign key enforcement is on (`ForeignKeys = true` in the connection string).

## Schema scripts

- Location: `src/Techie.Pbx.Core/Data/Schema/NNN_description.sql`, embedded into the assembly.
- `Database.Migrate()` reads `PRAGMA user_version`, runs every script with a higher number in
  order, each in its own transaction, then sets `user_version` to that number.
- **Add a new script for every change. Never edit one that has been deployed.** Until the
  first real install, `001_initial.sql` may still be edited; this note gets removed when that
  changes.
- Scripts can contain several statements.

## Data access

- One repository class per aggregate (`ExtensionRepository`, `SettingsRepository`), constructed
  with `new Repository(database)`.
- Repositories validate models before writing and turn constraint violations into
  `ValidationFailedException` with a message fit to show a user.
- Write explicit column lists, no `SELECT *`.

## Tables

### Extensions (001)

| Column | Type | Notes |
|---|---|---|
| `ExtensionID` | INTEGER PK | |
| `Number` | TEXT, unique | 2–6 digits |
| `Name` | TEXT | Up to 64 chars: letters, digits, space, `. , ' - _ ( ) &`. Used as caller ID name. |
| `Secret` | TEXT | SIP password, 16–64 letters/digits |
| `Enabled` | INTEGER | 0/1, default 1. Disabled extensions are left out of the generated config. |

### Settings (002)

Key/value rather than a column per setting, so adding one needs no schema script (D15).

| Column | Type | Notes |
|---|---|---|
| `SettingID` | INTEGER PK | |
| `Key` | TEXT, unique | Must be one of the constants in `SettingsKeys`; the repository rejects anything else. Quoted in SQL, since `KEY` is a SQLite keyword. |
| `Value` | TEXT | Up to 1024 characters. Empty or blank counts as "not set", so the default applies. |

Current keys: `Asterisk.ConfDirectory`, `Ami.Host`, `Ami.Port`, `Ami.Username`, `Ami.Secret`,
`Ami.TimeoutSeconds`, `Sip.BindAddress`, `Sip.Port`, `Sip.LocalNets` (comma separated CIDRs),
`Sip.ExternalAddress`.

`Ami.Secret` is a credential (D14): `SettingsKeys.IsSecret` marks it, it is never logged and
never shown. Defaults are not seeded as rows — they live on `AmiSettings` and `PjsipTransport`,
and `AsteriskSettings` applies a row on top only when there is one.
