# aaas-app-template

Template repository for AaaS-generated applications. FastAPI + Postgres, deployable as-is.

**Mark this repository as a template** in Settings → General → Template repository, so `gh repo create --template` works.

## What you get

- `GET /health` — liveness/readiness, never touches the database
- `GET /ready` — reports database connectivity
- `GET /items`, `POST /items` — a trivial Postgres-backed resource, there to prove the connection works end to end
- Multi-stage Dockerfile, non-root, health-checked
- CI: ruff, pytest, docker build, container smoke test
- Release: image push to GHCR + automatic deployment PR

## Local development

```bash
python -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt
pytest -q
uvicorn app.main:app --reload
```

No database needed for tests. To run against one:

```bash
docker run -d --name pg -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:16
export PGHOST=localhost PGDATABASE=postgres PGUSER=postgres PGPASSWORD=dev
uvicorn app.main:app --reload
```

`PGPASSWORD` is a local-development fallback only. In Azure there is no
password: the container's managed identity fetches an Entra token at connect
time. See `app/db.py`.

## Creating an app from this template

```bash
gh repo create main0034/aaas-app-<name> --template main0034/aaas-app-template --private --clone
```

Then:

1. Edit `.aaas/deployment` to point at the deployment directory this app feeds.
2. Add repository secrets `AAAS_APP_ID` and `AAAS_APP_PRIVATE_KEY`.
3. Create the matching deployment in `aaas-deployments` with `container_image` set to `:bootstrap`.

## Agents

Read `AGENT.md` before writing code. It defines what you may not edit and the health-endpoint contract that deployment depends on.
