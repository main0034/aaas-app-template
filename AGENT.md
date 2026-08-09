# AGENT.md — conventions for this repository

Read this before writing any code here. It is the contract that keeps generated applications consistent, and it is the reason a human can review an agent's PR quickly.

## Do not edit these

| Path | Why |
|---|---|
| `Dockerfile` | Defines the image contract the Terraform module depends on: port, non-root user, `/health`. |
| `.github/workflows/` | CI is a guardrail on your output. You must not be able to weaken it. |
| `.aaas/deployment` | Set once when the repo is created. Changing it retargets deployments silently. |
| `AGENT.md` | This file. |

If one of these genuinely needs to change, say so and stop. A human decides.

## The health contract

`GET /health` must return 200 **without touching the database**, and it must do so within a few seconds of process start.

This is not a stylistic preference. The container's liveness and readiness probes hit this endpoint. If `/health` depends on Postgres, then a database problem presents as a failed deployment and a rolled-back revision, and you will spend a long time debugging the wrong layer. Database connectivity belongs on `/ready`, which nothing depends on.

## Structure

```
app/
  main.py          # FastAPI app, routes, models
  __init__.py
tests/
  test_*.py
requirements.txt      # runtime dependencies, pinned
requirements-dev.txt  # test and lint dependencies
```

For anything beyond a handful of routes, split into `app/routes/` and `app/models.py` rather than growing `main.py` indefinitely. Keep `app.main:app` as the entry point — the Dockerfile references it.

## Rules

**Configuration comes from environment variables.** Read them at module level with a sensible default. `DATABASE_URL` and `PORT` are injected by the platform; do not redefine them.

**Never hardcode a credential**, and never read one from anywhere but the environment. The database connection string arrives as `DATABASE_URL` from Key Vault. There is no other secret store in this application.

**Database access is lazy.** Create the pool on first use, not at import or startup. Startup must not require the database — see the health contract above.

**Pin every dependency** to an exact version. An unpinned dependency means the image built from a given commit is not reproducible, which defeats the point of tagging images with the git SHA.

**Fail with a status code, not a stack trace.** Unreachable database is a 503. Bad input is a 422 (Pydantic gives you this). An unhandled exception in a route is a bug.

**Every new route gets a test**, and tests must pass with no database available. If a test needs Postgres, the design is probably wrong — the logic under test should be separable from the connection.

## Before you open a PR

```bash
pip install -r requirements-dev.txt
ruff check app tests
pytest -q
docker build -t app:local .
```

All four must pass. CI runs the same commands plus a container smoke test, so a failure locally is a failure in CI — save yourself the round trip.

## What happens after merge

Merging to `main` builds an image tagged with the commit SHA, pushes it to GHCR, and opens a PR against `aaas-deployments` bumping `container_image`. That second PR is what actually deploys. You do not need to do anything to trigger it, and you must not edit the deployments repo by hand to work around it.
