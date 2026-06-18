# Deployment Guide

This project can be deployed as a Docker web service. Docker is the recommended path because the app uses Blazor Server and SQLite, and the container keeps runtime settings predictable.

## Recommended Host

The simplest GitHub-connected option is Render using the included `render.yaml` blueprint.

Render web services provide a `PORT` environment variable. The Dockerfile binds ASP.NET Core to `${PORT}` automatically, falling back to port `8080` for local Docker runs.

## Files Used For Deployment

| File | Purpose |
| --- | --- |
| `Dockerfile` | Builds and runs the Blazor web app |
| `.dockerignore` | Keeps build output, local database files, logs, and local notes out of the image |
| `render.yaml` | Render blueprint for a Docker web service |
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

The included `render.yaml` mounts a disk at:

```text
/app/Data
```

That is where SQLite stores:

```text
tictactoang.db
```

If your hosting plan does not support persistent disks, the app will still run, but the SQLite database can reset when the container is rebuilt or restarted.

## Environment Variables

These values are already set in the Dockerfile/render blueprint:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Data Source=/app/Data/tictactoang.db
```

For another host, set the same connection string environment variable if you want SQLite stored in a specific mounted folder.

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
- For production with multiple app instances, replace SQLite with a hosted database such as PostgreSQL or SQL Server.
