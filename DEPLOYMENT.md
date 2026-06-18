# Deployment Guide

This project can be deployed as a Docker web service. Docker is the recommended path because the app uses Blazor Server and needs predictable runtime settings.

## Recommended Host

The simplest GitHub-connected option is Render using the included `render.yaml` blueprint.

Render web services provide a `PORT` environment variable. The Dockerfile binds ASP.NET Core to `${PORT}` automatically, falling back to port `8080` for local Docker runs.

## Files Used For Deployment

| File | Purpose |
| --- | --- |
| `Dockerfile` | Builds and runs the Blazor web app |
| `.dockerignore` | Keeps build output, local database files, logs, and local notes out of the image |
| `render.yaml` | Render blueprint for a Docker web service and PostgreSQL database |
| `USER_GUIDE.md` | User/admin operating instructions |

## Local Docker Test

Start Docker Desktop first. The Docker CLI must be able to connect to the Linux engine.

From the project root:

```powershell
docker build -t tictactoang .
docker run --rm -p 8080:8080 -v ${PWD}\Data:/app/Data tictactoang
```

Open:

```text
http://localhost:8080
```

Stop the container with `Ctrl + C`.

## Deploy To Render

1. Push the repository to GitHub.
2. Open Render.
3. Choose `New` -> `Blueprint`.
4. Select the GitHub repository.
5. Render will detect `render.yaml`.
6. Create the service.
7. Wait for the Docker build and deployment to finish.
8. Open the generated Render URL.

The included `render.yaml` provisions a Render PostgreSQL database named:

```text
tictactoang-db
```

The service receives its PostgreSQL connection string through:

```text
ConnectionStrings__DefaultConnection
```

The deployed service also sets:

```text
Database__Provider=Postgres
```

## Environment Variables

These values are set in the Dockerfile for local container testing:

```text
ASPNETCORE_ENVIRONMENT=Production
Database__Provider=Sqlite
ConnectionStrings__DefaultConnection=Data Source=/app/Data/tictactoang.db
```

For Render cloud deployment, `render.yaml` overrides the database provider to `Postgres`, injects the PostgreSQL connection string, and generates a JWT signing key.

For another host using PostgreSQL, set:

```text
Database__Provider=Postgres
ConnectionStrings__DefaultConnection=<your PostgreSQL connection string>
Jwt__Key=<a long random secret>
Jwt__Issuer=TicTacToang
Jwt__Audience=TicTacToang.Client
```

## Deploy To Another Docker Host

Build and run:

```powershell
docker build -t tictactoang .
docker run -d --name tictactoang -p 80:8080 -v tictactoang-data:/app/Data tictactoang
```

Then open the server IP or domain.

## Test Accounts

| Type | Username | Password |
| --- | --- | --- |
| Admin | `admin` | `Admin@1234` |
| Premium player | `playera` | `PlayerA@123` |
| Normal player | `playerb` | `PlayerB@123` |

Additional demo accounts use:

```text
Demo@1234
```

## Notes

- Do not commit `Data/tictactoang.db`; it is local runtime data.
- Demo data is seeded automatically when the database is empty.
- Render deployment uses PostgreSQL to satisfy production-style database requirements.
- The admin console includes suspicious match detection and AI analytics for a more distinctive management workflow.
