# TicTacToang.NET

TicTacToang.NET is a .NET/C# web game platform for five-in-a-row matches with player rooms, match history, admin management, and SQLite persistence.

## Tech Stack

- ASP.NET Core
- Blazor Server
- EF Core with PostgreSQL deployment support and SQLite local fallback
- SignalR
- Clean Architecture style project layout
- Domain-focused game rules and tests

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
| Premium player | `playera` | `PlayerA@123` |
| Normal player | `playerb` | `PlayerB@123` |

Additional seeded demo users use `Demo@1234`.

## Test

```powershell
dotnet build TicTacToang.sln -m:1
dotnet run --project tests\TicTacToang.Domain.Specs
```

## Documentation

See [USER_GUIDE.md](USER_GUIDE.md) for full run, stop, testing, admin, and database instructions.

See [DEPLOYMENT.md](DEPLOYMENT.md) for Docker and hosting instructions.
