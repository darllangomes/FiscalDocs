#!/usr/bin/env bash
# Sobe a stack num projeto separado (fiscaldocs-load), roda os cenários do k6 num container
# da mesma rede e mede quanto a outbox demora para esvaziar depois da ingestão.
#
# Uso: loadtests/run.sh [ingest|queries|all] [--keep]
#   RATE, DURATION, SEED: veja os scripts em loadtests/k6/
#   --keep deixa a stack no ar no final (padrão: compose down -v)
set -euo pipefail

cd "$(dirname "$0")/.."

WHAT="${1:-all}"
KEEP=false
[[ "${2:-}" == "--keep" || "${1:-}" == "--keep" ]] && KEEP=true
[[ "$WHAT" == "--keep" ]] && WHAT=all

if [[ ! -f .env ]]; then
  echo "No .env found, copying .env.example"
  cp .env.example .env
fi
set -a
# shellcheck disable=SC1091
. ./.env
set +a
: "${API_KEY:?Set API_KEY in .env (see .env.example)}"

PROJECT="fiscaldocs-load"
compose() { docker compose -p "$PROJECT" -f docker-compose.yml -f loadtests/docker-compose.loadtest.yml "$@"; }
API="http://localhost:${API_PORT:-8080}"
RESULTS="loadtests/results"
mkdir -p "$RESULTS"

step() { echo; echo "==> $*"; }
fail() { echo "FAIL: $*" >&2; compose logs --tail=40 >&2 || true; exit 1; }

cleanup() {
  if [[ "$KEEP" == false ]]; then
    compose down -v >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

psql_value() {
  compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "$1" | tr -d '[:space:]'
}

k6() { # <script> <arquivo-de-resumo> [VAR=valor ...]
  local script="$1" summary="$2"
  shift 2
  local env_args=()
  for kv in "$@"; do env_args+=(-e "$kv"); done
  MSYS_NO_PATHCONV=1 docker run --rm \
    --network "${PROJECT}_default" \
    -v "$PWD/loadtests/k6:/scripts:ro" \
    -v "$PWD/$RESULTS:/results" \
    -e BASE_URL=http://api:8080 -e API_KEY="$API_KEY" "${env_args[@]}" \
    grafana/k6 run --summary-export "/results/$summary" "/scripts/$script"
}

step "Checking for a development stack"
others="$(docker ps -a --filter "name=^fiscaldocs-(postgres|rabbitmq|api|worker)$" \
  --format '{{.Names}} {{.Label "com.docker.compose.project"}}' | grep -v " $PROJECT\$" || true)"
if [[ -n "$others" ]]; then
  echo "$others" >&2
  echo "Stop the stack above first with 'docker compose down' (keeps its volumes) and rerun." >&2
  exit 1
fi

step "Environment"
docker info --format 'Docker: {{.NCPU}} CPUs, {{.MemTotal}} bytes of memory' | tee "$RESULTS/environment.txt"

step "Starting the stack (rate limit raised)"
compose up -d --build
for _ in $(seq 1 120); do curl -sf "$API/health/ready" >/dev/null 2>&1 && break; sleep 1; done
curl -sf "$API/health/ready" >/dev/null || fail "API did not become ready"

if [[ "$WHAT" == "ingest" || "$WHAT" == "all" ]]; then
  step "k6: ingestion"
  k6 ingest.js ingest.json ${RATE:+RATE=$RATE} ${DURATION:+DURATION=$DURATION} || echo "(thresholds failed; see above)"

  step "Waiting for the outbox to drain"
  start=$SECONDS
  until [[ "$(psql_value 'SELECT count(*) FROM outbox_messages WHERE processed_at IS NULL')" == "0" ]]; do
    (( SECONDS - start < 600 )) || fail "outbox did not drain in 10 minutes"
    sleep 1
  done
  echo "outbox drained $((SECONDS - start))s after the load ended"
  echo "documents: $(psql_value 'SELECT count(*) FROM fiscal_documents')"
  echo "aggregated documents: $(psql_value 'SELECT coalesce(sum(document_count),0) FROM daily_issuer_aggregates')"
fi

if [[ "$WHAT" == "queries" || "$WHAT" == "all" ]]; then
  step "k6: queries"
  k6 queries.js queries.json ${SEED:+SEED=$SEED} ${RATE:+RATE=$RATE} ${DURATION:+DURATION=$DURATION} \
    || echo "(thresholds failed; see above)"
fi

echo
echo "Summaries in $RESULTS/"
