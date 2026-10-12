# aaas-app-template

Template repository for AaaS-generated applications. ASP.NET Core minimal API on .NET 10 + Postgres via EF Core, deployable as-is.

**Mark this repository as a template** in Settings → General → Template repository, so `gh repo create --template` works.

## What you get

- `GET /health` — liveness/readiness, never touches the database
- `GET /ready` — reports database connectivity and the latest applied migration
- `GET /items`, `POST /items` — a trivial Postgres-backed resource, there to prove the connection and the migration path work end to end
- `/` — a screen for it: the item list and a form to add one. Blazor components rendered statically on the server, no JavaScript; pages call the routes through `AppApi` and never touch the database (AGENT.md, "Screens")
- EF Core migrations, applied by `App migrate` in an init container before each revision starts
- Chiseled, non-root runtime image
- CI: pages checked (routes only, static, tested), format, build (warnings as errors), unit tests without a database, endpoint tests against a real Postgres (one cloned database per test), model-vs-migration check, immutable and expand-only migration checks, docker build, smoke test, migrations applied twice to a real Postgres
- Release: image push to GHCR + automatic deployment PR

## Local development

Needs the .NET 10 SDK (`global.json` pins the band).

```bash
dotnet restore && dotnet tool restore
dotnet test
```

Without a database the endpoint tests are skipped. To run them, and the app, against one:

```bash
docker run -d --name pg -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:16
TEST_POSTGRES="Host=localhost;Username=postgres;Password=dev" dotnet test
export PGHOST=localhost PGDATABASE=postgres PGUSER=postgres PGPASSWORD=dev
dotnet run --project src/App -- migrate
dotnet run --project src/App
```

`PGPASSWORD` is a local-development fallback only. In Azure there is no password: the container's managed identity fetches an Entra token for each connection. See `src/App/Data/Database.cs`.

## The workbench

How the operator tries a change on his own machine before it ships (roadmap step 5a).
Needs Docker, and `aaas-agent` checked out next to this repository.

```
request ──► build ──► preview ──► (edit request ──► rebuild ──► preview)* ──► ship
```

In Rider, each step is a run configuration in the *Workbench* folder (`.run/`):

| Step | Rider | Shell | What happens |
|---|---|---|---|
| Write the request | | | `requests/<name>.md` from `requests/TEMPLATE.md`, on `master`, with any help you like: Rider's AI assistant, given `requests/TEMPLATE.md` and `AGENT.md`, drafts one well. No need to commit it |
| Check it | *0 Check request* | `../aaas-agent/workbench.sh check [name]` | What is missing before it can ship: the title, *What and why*, *Rules*, a route under *Interface*. No agent, no network. A build may start from an unfinished request; ship may not |
| Build | *1 Build* | `../aaas-agent/workbench.sh build [name]` | The request is committed on `change/<date>-<name>` and pushed. A builder writes the code there and the harness pushes it - no pull request, no spec-tester. The branch is then checked out here |
| Preview | *2 Preview* | `scripts/preview.sh` | Builds the image from this checkout and runs it on <http://localhost:8080> with a local Postgres, `migrate` first. An empty app gets a few rows (`scripts/preview-seed.sh`, through the routes) |
| Change your mind | | | Edit `requests/<name>.md` on the branch. No need to commit it |
| Rebuild | *3 Rebuild* | `../aaas-agent/workbench.sh rebuild` | Commits and pushes the request. A fresh builder gets the new request, what changed in it, and the branch so far |
| Ship | *4 Ship* | `../aaas-agent/workbench.sh ship` | Two spec-testers write hidden acceptance tests from the final request, on `master`, never seeing the branch. The harness moves the request into `changes/<id>/` with their tests, opens the pull request and runs up to two fix rounds. **The pull request waits for you to merge it.** `ship --merge` lets the harness merge it when D-26 allows (no migration, no dependency) |

- **The request's git history is the record** of how the requirement moved; the pull request lists it.
- **Ship refuses a request edited after the last build**: the spec-testers test the request, so the code must have been built from that one.
- **The preview keeps its data** between runs, as a deployment does. If a rebuild changed a migration the preview had already applied, *Preview from an empty database* (`scripts/preview.sh fresh`).
- **Debugging**: *Preview* first, then the `App (preview database)` launch profile runs `dotnet run` on <http://localhost:8001> against the preview's database, with breakpoints.

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
