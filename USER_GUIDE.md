# TicTacToang User Guide

TicTacToang is a web-based strategy game platform where players create rooms, invite or join players, play five-in-a-row matches, and manage accounts through an admin console.

## What You Need

- Windows, macOS, or Linux
- .NET SDK 9 or newer
- A web browser

Check that .NET is installed:

```powershell
dotnet --version
```

## Start The Application

Open a terminal in the project folder:

```powershell
cd path\to\TicTacToang.NET
```

Run the web app:

```powershell
dotnet run --project src\TicTacToang.Web --urls http://localhost:5098
```

Open the app:

```text
http://localhost:5098
```

## Stop The Application

If the app is running in the terminal, press:

```text
Ctrl + C
```

If the app was started in the background or the port is stuck, run:

```powershell
Get-NetTCPConnection -LocalPort 5098 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

## Test Accounts

| Account Type | Username | Password |
| --- | --- | --- |
| Admin | `admin` | `Admin@1234` |
| Premium player | `PlayerA` | `PlayerA@123` |
| Normal player | `PlayerB` | `PlayerB@123` |

Additional demo users are seeded for dashboard and management testing. They all use:

```text
Demo@1234
```

| Name | Username | Notes |
| --- | --- | --- |
| Mina Tran | `minat` | Premium player |
| Noah Lee | `noahl` | Normal player |
| Ava Chen | `avac` | Moderator and premium user |
| Liam Pham | `liamp` | Inactive/banned user |
| Sora Kim | `sorak` | Normal player |
| Kai Nguyen | `kaing` | Premium player |

## Player Features

### Create A Match

1. Log in as a player.
2. Open the main menu.
3. Select `Create Room`.
4. Choose room options such as player count, board size, marker, style, and timer.
5. Select `Create Room`.
6. Add an AI player or wait for another player.
7. Select `Start Game`.

### Join A Room

1. Log in as another player.
2. Select `Join Room`.
3. Enter the room code.
4. Join the lobby and start playing when the room is ready.

### Chat In A Room

1. Open a game room lobby.
2. Type a message in the chat input.
3. Select send.

### View Profile

1. Open the profile page from the main menu.
2. Review or update account details.

## Admin Features

Log in with:

```text
admin
Admin@1234
```

Open:

```text
http://localhost:5098/admin
```

The admin console supports:

- Dashboard statistics
- User search
- Filtering users by role, account status, and premium status
- Banning and unbanning users
- Reviewing recent matches
- Aborting active or waiting matches
- Suspicious match detection
- AI difficulty analytics and player win-rate tracking

The database includes demo users, rooms, matches, friendships, friend requests, and room invites so the admin dashboard does not appear empty during testing.

## Database

The application uses Entity Framework Core.

For local development, it uses SQLite by default. The local database file is created automatically when the app starts:

```text
Data\tictactoang.db
```

For cloud deployment, the project supports PostgreSQL by setting:

```text
Database__Provider=Postgres
ConnectionStrings__DefaultConnection=<PostgreSQL connection string>
```

The main database tables are:

- `Players`
- `Matches`
- `Rooms`
- `FriendRequests`
- `Friendships`
- `RoomInvites`

## Database Integration

The project separates database code from business rules:

- Domain rules are in `src\TicTacToang.Domain`
- Use cases are in `src\TicTacToang.Application`
- Database implementation is in `src\TicTacToang.Infrastructure`
- The web app is in `src\TicTacToang.Web`

The application layer depends on `IApplicationStore`.
The infrastructure layer implements that interface with `SqliteApplicationStore` and `ApplicationDbContext`.

Connection string location:

```text
src\TicTacToang.Web\appsettings.json
src\TicTacToang.Api\appsettings.json
```

Default connection string:

```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=Data/tictactoang.db"
}
```

To use a different SQLite file, change the `Data Source` value.

To use PostgreSQL, set `Database:Provider` to `Postgres` and provide a PostgreSQL connection string. Render uses this automatically from `render.yaml`.

## API Authentication

The REST API supports JWT authentication.

Login endpoint:

```text
POST /api/auth/login
```

The response includes a `token`. Send it to protected endpoints with:

```text
Authorization: Bearer <token>
```

Admin API endpoints require an admin JWT.

## EF Core Migrations

Migration files are stored here:

```text
src\TicTacToang.Infrastructure\Persistence\Migrations
```

Create a new migration after changing the database model:

```powershell
dotnet ef migrations add MigrationName --project src\TicTacToang.Infrastructure --startup-project src\TicTacToang.Web --context ApplicationDbContext --output-dir Persistence\Migrations
```

Apply migrations manually if needed:

```powershell
dotnet ef database update --project src\TicTacToang.Infrastructure --startup-project src\TicTacToang.Web --context ApplicationDbContext
```

The current app also creates missing tables automatically during startup for easier local testing.

## Run Tests

Build the solution:

```powershell
dotnet build TicTacToang.sln -m:1
```

Run domain tests:

```powershell
dotnet run --project tests\TicTacToang.Domain.Specs
```

Expected result:

```text
PASS five horizontal markers completes the match
PASS five diagonal markers records winning tiles
PASS occupied cells are rejected
PASS opponent cannot move out of turn
PASS resignation awards win to opponent
PASS waiting multiplayer match activates after join
```

## Troubleshooting

### Port 5098 Is Already In Use

```powershell
Get-NetTCPConnection -LocalPort 5098 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

Then start the app again.

### Database Looks Empty

The app seeds test accounts when the database is empty. If you delete `Data\tictactoang.db`, the app creates a fresh database the next time it starts.

### Build Fails Because A File Is Locked

Stop the running app first:

```powershell
Get-NetTCPConnection -LocalPort 5098 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

Then build again:

```powershell
dotnet build TicTacToang.sln -m:1
```
