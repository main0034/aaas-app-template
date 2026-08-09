"""
Template application.

Deliberately small, but complete: it starts, serves /health without a
database, and exercises Postgres on the endpoints that need it. That shape
matters - the container must become healthy even when the database is
unreachable, otherwise a DB problem looks like a deployment failure and you
debug the wrong thing.

Agents extending this file: read AGENT.md first.
"""

from __future__ import annotations

import os
from contextlib import asynccontextmanager
from typing import Any

import asyncpg
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

DATABASE_URL = os.environ.get("DATABASE_URL", "")
APP_NAME = os.environ.get("APP_NAME", "app")

_pool: asyncpg.Pool | None = None


async def get_pool() -> asyncpg.Pool:
    """Lazily create the connection pool.

    Lazy on purpose: startup must not depend on the database being reachable.
    """
    global _pool
    if _pool is None:
        if not DATABASE_URL:
            raise HTTPException(status_code=503, detail="DATABASE_URL is not configured")
        try:
            _pool = await asyncpg.create_pool(DATABASE_URL, min_size=1, max_size=5, timeout=10)
        except Exception as exc:  # noqa: BLE001 - surfaced to the caller as 503
            raise HTTPException(status_code=503, detail=f"database unavailable: {exc}") from exc
    return _pool


SCHEMA = """
CREATE TABLE IF NOT EXISTS items (
    id    SERIAL PRIMARY KEY,
    title TEXT NOT NULL,
    note  TEXT
);
"""


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Best-effort schema creation. A failure here must not stop the app from
    # starting, or the health check never passes and the revision is rolled back.
    if DATABASE_URL:
        try:
            pool = await asyncpg.create_pool(DATABASE_URL, min_size=1, max_size=2, timeout=10)
            async with pool.acquire() as conn:
                await conn.execute(SCHEMA)
            await pool.close()
        except Exception as exc:  # noqa: BLE001
            print(f"[startup] schema init skipped: {exc}", flush=True)
    yield
    if _pool is not None:
        await _pool.close()


app = FastAPI(title=APP_NAME, lifespan=lifespan)


class ItemIn(BaseModel):
    title: str = Field(min_length=1, max_length=200)
    note: str | None = Field(default=None, max_length=2000)


class Item(ItemIn):
    id: int


@app.get("/health")
async def health() -> dict[str, str]:
    """Liveness and readiness. Must never touch the database."""
    return {"status": "ok", "app": APP_NAME}


@app.get("/ready")
async def ready() -> dict[str, Any]:
    """Reports database connectivity. Not wired to the container probes."""
    if not DATABASE_URL:
        return {"database": "unconfigured"}
    try:
        pool = await get_pool()
        async with pool.acquire() as conn:
            await conn.fetchval("SELECT 1")
        return {"database": "ok"}
    except HTTPException as exc:
        return {"database": "unavailable", "detail": exc.detail}


@app.get("/items", response_model=list[Item])
async def list_items() -> list[dict[str, Any]]:
    pool = await get_pool()
    async with pool.acquire() as conn:
        rows = await conn.fetch("SELECT id, title, note FROM items ORDER BY id DESC LIMIT 100")
    return [dict(row) for row in rows]


@app.post("/items", response_model=Item, status_code=201)
async def create_item(item: ItemIn) -> dict[str, Any]:
    pool = await get_pool()
    async with pool.acquire() as conn:
        row = await conn.fetchrow(
            "INSERT INTO items (title, note) VALUES ($1, $2) RETURNING id, title, note",
            item.title,
            item.note,
        )
    return dict(row)
