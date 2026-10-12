#!/usr/bin/env bash
# Preview this checkout on this machine: the real image, a local Postgres, http://localhost:8080.
#
#   scripts/preview.sh          build the image from the working tree and (re)start it
#   scripts/preview.sh fresh    the same, from an empty database
#   scripts/preview.sh down     stop it (the data is kept)
#   scripts/preview.sh logs     follow the app's and the migration's output
#
# The workbench (roadmap step 5a): build a change on its branch, check the branch out
# here, preview, edit the request, rebuild. See README "The workbench".
# Plain bash 3.2: macOS's /bin/bash runs it.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
project="$(basename "$PWD" | tr '[:upper:]' '[:lower:]')-preview"
port="${PREVIEW_PORT:-8080}"
url="http://localhost:$port"
compose() { docker compose -f preview.yml -p "$project" "$@"; }
cmd="${1:-up}"

case "$cmd" in
  down) compose down; exit 0 ;;
  logs) compose logs -f migrate app; exit 0 ;;
  up | fresh) ;;
  *) echo "usage: $0 [up|fresh|down|logs]" >&2; exit 2 ;;
esac

if [ "$cmd" = fresh ]; then
  compose down -v --remove-orphans
fi

branch="$(git rev-parse --abbrev-ref HEAD)"
dirty=""
[ -z "$(git status --porcelain -- src tests)" ] || dirty=" (with uncommitted changes)"
echo "Previewing $branch @ $(git rev-parse --short HEAD)$dirty"

# The image, from the working tree. A build failure is the build's, so it is told apart
# from a migration failure.
if [ "${PREVIEW_NO_BUILD:-0}" != 1 ] && ! compose build app; then
  echo "The image did not build. The same Dockerfile builds it in CI and in Azure." >&2
  exit 1
fi

# --wait would wait on migrate, which exits, so the app is polled below instead.
if ! compose up --no-build --detach --remove-orphans; then
  echo "" >&2
  compose logs migrate | tail -20 >&2
  echo "" >&2
  echo "The preview did not start. A [migrate] FAILED line above is the cause; if a rebuild" >&2
  echo "changed a migration this branch had already applied here, start over: $0 fresh" >&2
  exit 1
fi

for _ in $(seq 1 60); do
  if curl -fsS "$url/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
if ! curl -fsS "$url/health" >/dev/null 2>&1; then
  compose logs app | tail -30 >&2
  echo "The app did not answer $url/health within 60 seconds." >&2
  exit 1
fi

scripts/preview-seed.sh "$url"
echo "Ready: $url  ($(curl -fsS "$url/ready"))"
if [ "${PREVIEW_OPEN:-1}" = 1 ] && command -v open >/dev/null 2>&1; then
  open "$url"
fi
