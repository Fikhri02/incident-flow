#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5089}"
CORRELATION_ID="${CORRELATION_ID:-incidentflow-demo-$(date +%s)}"

curl -sS -X POST "$BASE_URL/api/v1/signals/deployments" \
  -H "content-type: application/json" \
  -H "x-correlation-id: $CORRELATION_ID" \
  -d '{"sourceSystem":"deploy-bot","serviceName":"Payment API","environment":"production","deploymentId":"deploy-1.8.2","version":"1.8.2"}'

for index in 1 2 3 4 5; do
  curl -sS -X POST "$BASE_URL/api/v1/signals/errors" \
    -H "content-type: application/json" \
    -H "x-correlation-id: $CORRELATION_ID" \
    -d "{\"sourceSystem\":\"checkout-web\",\"serviceName\":\"Payment API\",\"environment\":\"production\",\"message\":\"Timeout while authorizing payment\",\"fingerprint\":\"payment-timeout\",\"deploymentId\":\"deploy-1.8.2\",\"idempotencyKey\":\"payment-timeout-$index\"}"
done

curl -sS "$BASE_URL/api/v1/incidents/open"
echo
curl -sS "$BASE_URL/api/v1/reports/summary?serviceName=Payment%20API&environment=production"
