# TicTacToang.NET

TicTacToang.NET is a .NET/C# web game platform for five-in-a-row matches with player rooms, match history, admin management, and PostgreSQL/SQLite persistence.

## Tech Stack

- ASP.NET Core
- Blazor Server
- EF Core with PostgreSQL deployment support and SQLite local fallback
- SignalR
- Clean Architecture style project layout
- Domain-focused game rules and tests
- Suspicious match detection and AI performance analytics

## Run

```powershell
dotnet run --project src\TicTacToang.Web --urls http://localhost:5098
```

Open:

```text
http://localhost:5098
```

## Test Accounts

| Type | Username | Password |
| --- | --- | --- |
| Admin | `admin` | `Admin@1234` |
| Premium player | `PlayerA` | `PlayerA@123` |
| Normal player | `PlayerB` | `PlayerB@123` |

Additional seeded demo users use `Demo@1234`.

MongoDB accounts imported from the React project retain their existing password. On their first successful .NET login, the legacy bcrypt hash is automatically upgraded to PBKDF2.

## Import Data from the React Project

The original React/Node project uses MongoDB. Export it to JSON first, then import that data into the .NET PostgreSQL or SQLite database.

From `group_7_assignment_3\Group7\backend`:

```powershell
npm run export:dotnet -- ..\..\..\TicTacToang.NET\Data\mongo-export.json
```

The exporter reads `MONGODB_URI` and `MONGODB_DB_NAME` from the React backend `.env` file.

Import into the local SQLite database from `TicTacToang.NET`:

```powershell
dotnet run --project tools\TicTacToang.DataImporter -- `
  --input Data\mongo-export.json `
  --provider Sqlite `
  --connection "Data Source=Data/tictactoang.db" `
  --replace
```

Import into PostgreSQL:

```powershell
dotnet run --project tools\TicTacToang.DataImporter -- `
  --input Data\mongo-export.json `
  --provider Postgres `
  --connection "$env:POSTGRES_CONNECTION_STRING" `
  --replace
```

`--replace` deletes the target application's current records before importing. Omit it to merge records using stable IDs.

## Test

```powershell
dotnet build TicTacToang.sln -m:1
dotnet run --project tests\TicTacToang.Domain.Specs
```

## Documentation

See [USER_GUIDE.md](USER_GUIDE.md) for full run, stop, testing, admin, and database instructions.

See [DEPLOYMENT.md](DEPLOYMENT.md) for Docker and hosting instructions.
