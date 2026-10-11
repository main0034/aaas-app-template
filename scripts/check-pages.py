#!/usr/bin/env python3
"""Screens follow AGENT.md, "Screens". Checked deterministically (D-17), on the whole tree.

  1. Pages never touch the database. Nothing under src/App/Components/ names
     AppDbContext, DbSet or EF Core: a page reaches data through AppApi, which
     calls the app's own routes, so the page gets the routes' tested behaviour.
  2. Static server-side rendering only: no @rendermode, no interactive render
     modes, no <script>. An interactive circuit does not survive scale-to-zero.
  3. Page paths are "/" or start with "/app/", so a page never shares a path with
     a route.
  4. Every page has a page test: its component name appears in tests/ as
     GetPageAsync<Name> (which renders it against Postgres).

Usage:
  scripts/check-pages.py
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
COMPONENTS = ROOT / "src" / "App" / "Components"
TESTS = ROOT / "tests"

DATABASE = re.compile(r"\bAppDbContext\b|\bDbSet\s*<|Microsoft\.EntityFrameworkCore")
INTERACTIVE = re.compile(
    r"@rendermode|InteractiveServer|InteractiveWebAssembly|InteractiveAuto|"
    r"AddInteractive\w+Components|AddInteractive\w+RenderMode|<script\b",
    re.IGNORECASE,
)
PAGE = re.compile(r'^\s*@page\s+"([^"]*)"', re.MULTILINE)


def main() -> int:
    errors: list[str] = []

    sources = [
        p for p in COMPONENTS.rglob("*") if p.suffix in {".razor", ".cs"} and p.is_file()
    ]
    for path in sources:
        rel = path.relative_to(ROOT)
        text = path.read_text(encoding="utf-8")
        for n, line in enumerate(text.splitlines(), 1):
            if line.strip().startswith(("//", "@*", "*")):
                continue  # comments may explain the rule
            if DATABASE.search(line):
                errors.append(
                    f"{rel}:{n}: a page touches the database. Call the route through "
                    f"AppApi instead; if no route does what the page needs, add the "
                    f"route (with its endpoint test) and call that."
                )
            if INTERACTIVE.search(line):
                errors.append(
                    f"{rel}:{n}: interactive rendering or script. Pages are static "
                    f"server-side rendered, with plain HTML forms."
                )

    for program in (ROOT / "src" / "App").glob("*.cs"):
        for n, line in enumerate(program.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip().startswith("//") and INTERACTIVE.search(line):
                errors.append(f"{program.relative_to(ROOT)}:{n}: interactive rendering.")

    tests = "\n".join(
        p.read_text(encoding="utf-8") for p in TESTS.rglob("*.cs") if "/bin/" not in str(p)
    )
    pages = 0
    for path in COMPONENTS.rglob("*.razor"):
        text = path.read_text(encoding="utf-8")
        routes = PAGE.findall(text)
        if not routes:
            continue
        pages += 1
        rel = path.relative_to(ROOT)
        for route in routes:
            if route != "/" and not route.startswith("/app/"):
                errors.append(
                    f'{rel}: page path "{route}" must be "/" or start with "/app/", so it '
                    f"never shares a path with a route."
                )
        name = path.stem
        if not re.search(rf"GetPageAsync<\s*{re.escape(name)}\s*>", tests):
            errors.append(
                f"{rel}: page {name} has no page test. Add one deriving from PageTest that "
                f"calls GetPageAsync<{name}>(...)."
            )

    if errors:
        for e in errors:
            print(f"::error::{e}")
        return 1
    print(f"pages ok: {pages} page(s), {len(sources)} component file(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
