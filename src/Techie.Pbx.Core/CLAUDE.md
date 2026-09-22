# Techie.Pbx.Core

Database conventions (see also the root CLAUDE.md).

**Database**
- SQLite via **Dapper** + `Microsoft.Data.Sqlite`. No EF Core.
- **Primary keys are `<Entity>ID`**, e.g. `ExtensionID`, never `Id`, in both the column and the
  C# property. Foreign keys use the same name as the key they point at.
- Tables and columns are PascalCase (`Extensions.Number`). Tables are plural.
- Schema changes: add a new `Data/Schema/NNN_description.sql`. **Never edit a script that has
  shipped**; `PRAGMA user_version` tracks which have run. See [docs/database.md](docs/database.md).
- Repositories validate before writing and throw `ValidationFailedException` for user errors.
