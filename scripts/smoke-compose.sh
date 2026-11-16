#!/usr/bin/env bash
# Verificação ponta a ponta da stack inteira rodando no Docker Compose:
# API -> outbox -> relay -> RabbitMQ -> consumidor -> agregado diário no Postgres.
#
# Uso: scripts/smoke-compose.sh [--keep]
#   --keep  deixa a stack no ar no final (padrão: compose down -v)
set -euo pipefail

cd "$(dirname "$0")/.."

KEEP=false
[[ "${1:-}" == "--keep" ]] && KEEP=true

if [[ ! -f .env ]]; then
  echo "No .env found, copying .env.example"
  cp .env.example .env
fi
set -a
# shellcheck disable=SC1091
. ./.env
set +a
: "${API_KEY:?Set API_KEY in .env (see .env.example)}"

# Um nome de projeto separado mantém os volumes longe da stack de desenvolvimento,
# então o "down -v" do final nunca apaga dados de desenvolvimento.
PROJECT="fiscaldocs-smoke"
compose() { docker compose -p "$PROJECT" "$@"; }

API="http://localhost:${API_PORT:-8080}"
ROUTE="$API/api/v1/documents"
BODY="$(mktemp)"

step() { echo; echo "==> $*"; }
fail() { echo "FAIL: $*" >&2; compose logs --tail=40 >&2 || true; exit 1; }

cleanup() {
  rm -f "$BODY"
  if [[ "$KEEP" == false ]]; then
    compose down -v >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

# wait_for <descrição> <timeout-em-segundos> <comando...>
wait_for() {
  local description="$1" timeout="$2"
  shift 2
  local deadline=$((SECONDS + timeout))
  until "$@" >/dev/null 2>&1; do
    (( SECONDS < deadline )) || fail "timed out waiting for: $description"
    sleep 1
  done
}

post_xml() { # <arquivo> -> imprime o status HTTP; o corpo vai para $BODY
  curl -s -o "$BODY" -w "%{http_code}" -X POST "$ROUTE" \
    -H "X-Api-Key: $API_KEY" -H "Content-Type: application/xml" \
    --data-binary "@$1"
}

aggregate() { # <cnpj-emitente> -> "contagem|total" ou vazio
  compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc \
    "SELECT document_count || '|' || total_amount FROM daily_issuer_aggregates WHERE issuer_document = '$1'"
}

aggregate_is() { [[ "$(aggregate "$1" | tr -d '[:space:]')" == "$2" ]]; }

count_is() { # <cnpj-emitente> <contagem-esperada>
  [[ "$(compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc     "SELECT document_count FROM daily_issuer_aggregates WHERE issuer_document = '$1'" | tr -d '[:space:]')" == "$2" ]]
}

step "Checking for a development stack"
# Os containers têm nomes fixos, então uma stack de outro projeto colidiria com esta.
others="$(docker ps -a --filter "name=^fiscaldocs-(postgres|rabbitmq|api|worker)$"   --format '{{.Names}} {{.Label "com.docker.compose.project"}}' | grep -v " $PROJECT\$" || true)"
if [[ -n "$others" ]]; then
  echo "$others" >&2
  echo "Stop the stack above first with 'docker compose down' (keeps its volumes) and rerun." >&2
  exit 1
fi

step "Starting the stack"
compose up -d --build

step "Waiting for the API to be ready"
wait_for "GET /health/ready" 120 curl -sf "$API/health/ready"

step "Ingesting a document"
status="$(post_xml samples/nfe-cnpj-numerico.xml)"
[[ "$status" == "201" ]] || fail "expected 201 on first POST, got $status"
id="$(sed -E 's/.*"id":"([^"]+)".*/\1/' "$BODY")"
echo "created $id"

step "Reading it back"
status="$(curl -s -o /dev/null -w "%{http_code}" -H "X-Api-Key: $API_KEY" "$ROUTE/$id")"
[[ "$status" == "200" ]] || fail "expected 200 on GET, got $status"

step "Waiting for the aggregate (outbox -> RabbitMQ -> worker)"
wait_for "aggregate 1|1500.00 for 11222333000181" 60 aggregate_is 11222333000181 "1|1500.00"

step "Posting the same document again must not count twice"
status="$(post_xml samples/nfe-cnpj-numerico.xml)"
[[ "$status" == "200" ]] || fail "expected 200 on duplicate POST, got $status"
sleep 3
aggregate_is 11222333000181 "1|1500.00" || fail "aggregate changed after a duplicate POST"

step "Ingesting while RabbitMQ is down (the API must not depend on it)"
compose stop rabbitmq
status="$(post_xml samples/nfe-cnpj-alfanumerico.xml)"
[[ "$status" == "201" ]] || fail "expected 201 with the broker down, got $status"

step "Bringing RabbitMQ back: the pending event must be delivered"
compose start rabbitmq
wait_for "aggregate for 12ABC34501DE35" 120 count_is 12ABC34501DE35 1

echo
echo "OK: the whole pipeline works end to end."
