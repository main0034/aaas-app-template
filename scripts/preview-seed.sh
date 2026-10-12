#!/usr/bin/env bash
# A few rows for the preview, added through the app's own routes - never SQL, so
# they pass the same rules a user's would. Only into an empty app: a preview that
# already has data keeps it.
#
# Each application keeps its own copy of this file: the rows are its domain.
set -euo pipefail
url="${1:-http://localhost:8080}"

if [ "$(curl -fsS "$url/items")" != "[]" ]; then
  exit 0
fi

post() {
  curl -fsS -o /dev/null -X POST "$url$1" -H 'Content-Type: application/json' -d "$2"
}

post /items '{"title": "Buy paint", "note": "White, two litres"}'
post /items '{"title": "Call the plumber", "note": null}'
post /items '{"title": "Book the meeting room"}'
echo "Seeded 3 items."
