# aaas-app-template

Template repository for AaaS-generated applications. ASP.NET Core minimal API on .NET 10 + Postgres via EF Core, deployable as-is.

**Mark this repository as a template** in Settings → General → Template repository, so `gh repo create --template` works.

## What you get

- `GET /health` — liveness/readiness, never touches the database
- `GET /ready` — reports database connectivity and the latest applied migration
- `GET /items`, `POST /items` — a trivial Postgres-backed resource, there to prove the connection and the migration path work end to end
- EF Core migrations, applied by `App migrate` in an init container before each revision starts
- Chiseled, non-root runtime image
- CI: format, build (warnings as errors), tests without a database, model-vs-migration check, immutable and expand-only migration checks, docker build, smoke test, migrations applied twice to a real Postgres
- Release: image push to GHCR + automatic deployment PR

## Local development

Needs the .NET 10 SDK (`global.json` pins the band).

```bash
dotnet restore && dotnet tool restore
dotnet test
```

No database needed for tests. To run against one:

```bash
docker run -d --name pg -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:16
export PGHOST=localhost PGDATABASE=postgres PGUSER=postgres PGPASSWORD=dev
dotnet run --project src/App -- migrate
dotnet run --project src/App
```

`PGPASSWORD` is a local-development fallback only. In Azure there is no password: the container's managed identity fetches an Entra token for each connection. See `src/App/Data/Database.cs`.

## Creating an app from this template

```bash
gh repo create main0034/aaas-app-<name> --template main0034/aaas-app-template --private --clone
```

Then:

1. Edit `.aaas/deployment` to point at the deployment directory this app feeds.
2. Add repository secrets `AAAS_APP_ID` and `AAAS_APP_PRIVATE_KEY`.
3. Create the matching deployment in `aaas-deployments` with `container_image` set to `:bootstrap`.

## Agents

Read `AGENT.md` before writing code. It defines what you may not edit, the health-endpoint contract that deployment depends on, and the migration rules.
