#!/usr/bin/env bash
#
# One command deploy. Builds both images here, pushes them to the registry, and
# tells the droplet to pull and restart. Nothing is built on the droplet, whose
# disk is shared with the garage object store.
#
#   ./docs/deploy/deploy.sh                     build HEAD and deploy it
#   ./docs/deploy/deploy.sh --rollback abc1234  redeploy a tag already pushed
#
# Run it from WSL, from the repository root. Overridable settings are below.

set -euo pipefail

REGISTRY="${REGISTRY:-ghcr.io/matthew-holmes}"
DROPLET="${DROPLET:-root@188.166.153.242}"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/mmPrivKey.ossh}"
REMOTE_DIR="${REMOTE_DIR:-/opt/mattsmaths}"
SITE_URL="${SITE_URL:-https://matthewsmathematics.uk/}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

BUILD=1
TAG=""

while [ $# -gt 0 ]; do
    case "$1" in
        --rollback) BUILD=0; TAG="${2:?--rollback needs a tag}"; shift 2 ;;
        -h|--help)  sed -n '2,12p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *)          echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

say() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
die() { printf '\n\033[1;31mdeploy failed: %s\033[0m\n' "$*" >&2; exit 1; }

ssh_droplet() { ssh -i "$SSH_KEY" -o BatchMode=yes "$DROPLET" "$@"; }

# ---------------------------------------------------------------- preflight

say "checking this machine and the droplet"

command -v docker >/dev/null || die "no docker on PATH - is WSL integration on in Docker Desktop?"
docker info >/dev/null 2>&1  || die "the docker daemon is not answering - start Docker Desktop"
[ -f "$SSH_KEY" ]            || die "no ssh key at $SSH_KEY"

ssh_droplet true || die "cannot ssh to $DROPLET with $SSH_KEY"

ssh_droplet 'command -v docker >/dev/null' \
    || die "docker is not installed on the droplet - see docs/deploy/README.md"

# a missing secret would otherwise show up as a container that restarts forever,
# which is a slower and much less obvious way to learn the same thing
for f in /etc/mattsmaths/mmWebsite.env /etc/mattsmaths/deploy_key /etc/mattsmaths/deepseek_api_key; do
    ssh_droplet "test -f $f" || die "$f is missing on the droplet - see docs/deploy/README.md"
done

# ssh reads the key as uid 10001 inside the container and refuses it otherwise
ssh_droplet 'test "$(stat -c %u:%a /etc/mattsmaths/deploy_key)" = "10001:600"' \
    || die "/etc/mattsmaths/deploy_key must be owned by uid 10001 with mode 600 - see docs/deploy/README.md"

# ---------------------------------------------------------------- build

if [ "$BUILD" = "1" ]; then
    TAG="$(git -C "$REPO_ROOT" rev-parse --short HEAD)"

    if ! git -C "$REPO_ROOT" diff-index --quiet HEAD --; then
        TAG="$TAG-dirty"
        printf '\n\033[1;33mworking tree is dirty - deploying as %s, which no commit reproduces\033[0m\n' "$TAG"
    fi

    for svc in websheets syntheticpdfs; do
        say "building and pushing $svc:$TAG"
        docker buildx build \
            --platform linux/amd64 \
            --file "$HERE/Dockerfile.$svc" \
            --tag "$REGISTRY/$svc:$TAG" \
            --tag "$REGISTRY/$svc:latest" \
            --push \
            "$REPO_ROOT"
    done
else
    say "rolling back to $TAG without building"
fi

# ---------------------------------------------------------------- deploy

say "deploying $TAG to $DROPLET"

ssh_droplet "mkdir -p $REMOTE_DIR"
scp -i "$SSH_KEY" "$HERE/docker-compose.yml" "$DROPLET:$REMOTE_DIR/docker-compose.yml"

# TAG is written to .env so that a plain `docker compose up -d` on the droplet,
# and the restart after a reboot, both keep serving the version last deployed
ssh_droplet "printf 'REGISTRY=%s\nTAG=%s\n' '$REGISTRY' '$TAG' > $REMOTE_DIR/.env"

ssh_droplet "cd $REMOTE_DIR && docker compose pull && docker compose up -d --remove-orphans"

# an image per deploy adds up quickly on a disk shared with the object store
ssh_droplet "docker image prune -af --filter until=168h" >/dev/null

# ---------------------------------------------------------------- check

say "checking what came up"

ssh_droplet "cd $REMOTE_DIR && docker compose ps"

# read-only, unlike /ping, which would start a real generation run
ssh_droplet 'curl -fsS --retry 10 --retry-delay 2 --retry-all-errors http://127.0.0.1:5432/languages >/dev/null' \
    && echo "generator: answering on 5432" \
    || die "the generator did not answer - docker compose logs syntheticpdfs"

code="$(curl -fsS --retry 10 --retry-delay 2 --retry-all-errors -o /dev/null -w '%{http_code}' "$SITE_URL")" \
    || die "the site did not answer - docker compose logs websheets"
echo "site: $SITE_URL returned $code"

say "deployed $TAG"
echo "roll back with: $0 --rollback <previous tag>"
