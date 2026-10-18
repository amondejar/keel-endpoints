#!/usr/bin/env bash
# Publishes samples/Keel.AotSample with Native AOT (warnings are errors), runs the native binary and
# calls each endpoint. Usage: eng/aot-smoke.sh [runtime-identifier]   (default: linux-x64)
set -euo pipefail

rid="${1:-linux-x64}"
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/aot"
port=5099
base="http://127.0.0.1:$port/api"

dotnet publish "$root/samples/Keel.AotSample" -c Release -r "$rid" -o "$out" ${KEEL_PUBLISH_ARGS:-}

"$out/Keel.AotSample" --urls "http://127.0.0.1:$port" > "$out/run.log" 2>&1 &
pid=$!
trap 'kill $pid 2>/dev/null || true' EXIT
for _ in $(seq 1 50); do curl -s "$base/health" > /dev/null && break; sleep 0.1; done

fail=0
check() { # name expected-status actual-status [body-must-contain] [body]
  if [[ "$3" == "$2" && ( -z "${4:-}" || "${5:-}" == *"$4"* ) ]]; then echo "  PASS  $1"
  else echo "  FAIL  $1 (status $3, expected $2) ${5:-}"; fail=1; fi
}

body=$(curl -s -w '\n%{http_code}' "$base/health");                   check "health"           200 "${body##*$'\n'}" "ok" "$body"
body=$(curl -s -w '\n%{http_code}' "$base/todos/1");                  check "route binding"    200 "${body##*$'\n'}" '"id":1' "$body"
body=$(curl -s -w '\n%{http_code}' "$base/todos/99");                 check "typed NotFound"   404 "${body##*$'\n'}"
body=$(curl -s -w '\n%{http_code}' -H 'Content-Type: application/json' -d '{"title":"ship"}' "$base/todos")
                                                                      check "body binding"     200 "${body##*$'\n'}" '"title":"ship"' "$body"
body=$(curl -s -w '\n%{http_code}' -H 'Content-Type: application/json' -d '{"title":""}' "$base/todos")
                                                                      check "validation 400"   400 "${body##*$'\n'}" '"Title"' "$body"
body=$(curl -s -w '\n%{http_code}' "$base/todos/events?count=2");     check "server-sent events" 200 "${body##*$'\n'}" 'event: todo' "$body"

ls -la "$out/Keel.AotSample" | awk '{printf "  native binary: %.1f MB\n", $5/1048576}'
exit $fail
