# Migration And Design Record

## Scope

The source application was a React/Vite frontend and Node/Express/MongoDB backend using Socket.IO. It is a Gomoku platform, not a basic tic-tac-toe board: players seek five connected markers on a 10 x 10 or 15 x 15 board.

## Workflow Mapping

| Original workflow | .NET workflow |
| --- | --- |
| Login and register React screens calling Express auth routes | Blazor login/register screens using `PlayerService`; equivalent REST endpoints in `TicTacToang.Api` |
| Main menu and recent session sidebar | Blazor player hub with new match actions and recent match results |
| Create/join room, AI slots and room chat | Blazor lobby powered by `RoomService`; SignalR hub supports realtime client events |
| Browser/backend move logic | `Match` aggregate validates every move and resolves win/draw state centrally |
| Easy/medium/hard AI logic | `GomokuAi` application service with adjacent easy play and tactical medium/hard play |
| Match history and premium replay | History and move-by-move replay pages; replay is gated by premium membership |
| Profile, settings and password update | Profile and settings screens with PBKDF2 password hashing |
| PayPal-backed premium UI | Sandbox activation use case behind the player subscription boundary; a production gateway can replace it |
| Admin users/games panel | Blazor operations console and `/api/admin` endpoints |
| Socket.IO namespace | SignalR `GameRoomHub` at `/hubs/gameroom` |

## Architecture

Dependencies point inward:

```text
Web / Api -> Application -> Domain
Web / Api -> Infrastructure -> Application -> Domain
```

`Match` is the primary aggregate root. It protects turn order, board occupancy, board bounds, match completion, winning lines, draw detection and resignation. UI pages do not determine results.

`GameRoom` controls membership, host-only starting rules and chat ownership. `Player` controls account status and premium validity.

## Persistence Decision

The first migrated version uses a JSON-backed implementation of `IApplicationStore` so the capstone is runnable without carrying forward exposed MongoDB credentials or requiring database setup. Moving to SQL Server or PostgreSQL with EF Core requires an additional infrastructure adapter and migration scripts; domain/application code is intentionally independent of that choice.

## Security And Configuration

- Passwords are stored with PBKDF2 and a unique salt.
- Inactive accounts cannot perform active player operations.
- The legacy README exposed a MongoDB connection credential. Rotate it; it is intentionally absent here.
- The REST API currently supplies equivalent application workflows; production deployment should add ASP.NET Core Identity or JWT authorization policies to enforce endpoint access, especially admin operations.

## Visual Design

The Blazor UI retains a competitive dark game atmosphere while using a compact management-style navigation and admin console appropriate for a capstone. Teal communicates interaction, coral communicates danger/opponent markers, green communicates success, and the board uses a readable wood tone rather than a one-color interface.

## Remaining Production Integrations

The migrated application is runnable and implements its core workflows. Production deployment work is deliberately isolated behind boundaries:

- replace local JSON persistence with EF Core persistence;
- connect `ActivateSubscriptionAsync` to PayPal capture verification;
- connect notification workflows to an SMTP/email provider;
- enforce API bearer/cookie policies and SignalR authenticated groups.
