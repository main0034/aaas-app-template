"""
Database access using Entra ID authentication.

There is no password. The container's user-assigned managed identity requests
a short-lived token for Postgres and passes it where a password would go.

Why it is done this way: any stored credential has to live somewhere
Terraform can see, which means it ends up in Terraform state and has to be
readable by whatever identity runs `terraform plan`. Removing the credential
entirely was the only thing that actually solved that - see the note in
modules/app-stack/main.tf.

Consequences to be aware of:
  - tokens expire (roughly an hour), so the password is a CALLABLE that
    asyncpg invokes per connection rather than a fixed string
  - locally there is no managed identity, so set PGPASSWORD to fall back to
    ordinary password auth against a local Postgres
"""

from __future__ import annotations

import os

import asyncpg

# Scope for Azure Database for PostgreSQL. Not the ARM scope - using
# https://management.azure.com/.default here yields a token Postgres rejects
# with a confusing authentication failure.
POSTGRES_SCOPE = "https://ossrdbms-aad.database.windows.net/.default"

# PGHOST has no default on purpose. An unset PGHOST means "no database
# configured", which lets CI run without one and keeps is_configured() from
# triggering an Azure token request that would just time out.
PGHOST = os.environ.get("PGHOST", "")
PGDATABASE = os.environ.get("PGDATABASE", "postgres")
PGUSER = os.environ.get("PGUSER", "postgres")
PGPASSWORD = os.environ.get("PGPASSWORD", "")

_credential = None


def _get_credential():
    """Created lazily so importing this module never requires Azure."""
    global _credential
    if _credential is None:
        from azure.identity import DefaultAzureCredential

        # AZURE_CLIENT_ID is set by Terraform to the user-assigned identity's
        # client id. Without it DefaultAzureCredential looks for a
        # system-assigned identity and fails.
        _credential = DefaultAzureCredential(
            managed_identity_client_id=os.environ.get("AZURE_CLIENT_ID"),
            exclude_interactive_browser_credential=True,
        )
    return _credential


def _password() -> str:
    """Return a token, or the local development password.

    asyncpg accepts a callable here and invokes it for each new connection,
    so expiring tokens are refreshed without any explicit rotation logic.
    """
    if PGPASSWORD:
        return PGPASSWORD
    return _get_credential().get_token(POSTGRES_SCOPE).token


async def create_pool(min_size: int = 1, max_size: int = 5) -> asyncpg.Pool:
    return await asyncpg.create_pool(
        host=PGHOST,
        database=PGDATABASE,
        user=PGUSER,
        password=_password,
        ssl="require" if PGHOST != "localhost" else "prefer",
        min_size=min_size,
        max_size=max_size,
        timeout=15,
        # Tokens are per-connection; don't hold connections long enough for
        # one to expire mid-use.
        max_inactive_connection_lifetime=300,
    )


def is_configured() -> bool:
    """True when a database host has been provided."""
    return bool(PGHOST)
