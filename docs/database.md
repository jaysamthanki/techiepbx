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

- One repository class per aggregate (`ExtensionRepository`), constructed with `new Repository(database)`.
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
