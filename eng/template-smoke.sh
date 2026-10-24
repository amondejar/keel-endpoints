#!/usr/bin/env bash
# Packs Keel.Endpoints and its templates, installs the templates, creates an API with keel-api, adds
# slices with keel-slice, builds it and calls it; then creates the --aot variant, publishes it natively
# and calls it. Usage: eng/template-smoke.sh [runtime-identifier]   (default: linux-x64)
# KEEL_EXTRA_SOURCES (space-separated folders) replaces nuget.org, e.g. for offline runs;
# KEEL_PACK_ARGS and KEEL_BUILD_ARGS pass extra MSBuild arguments.
set -euo pipefail

rid="${1:-linux-x64}"
root="$(cd "$(dirname "$0")/.." && pwd)"
# Outside the repository, so the generated projects do not inherit its Directory.Build.* files.
work="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/keel-template-smoke.XXXXXX")"
feed="$work/feed"
version="0.0.0-smoke.$(date +%s)"
mkdir -p "$feed"
trap 'kill $(jobs -p) 2>/dev/null || true; dotnet new uninstall Keel.Endpoints.Templates > /dev/null 2>&1 || true; rm -rf "$work"' EXIT
export NUGET_PACKAGES="$work/packages"   # never reuse a cached Keel package from another run

# A throwaway version, lower than the published baseline: package validation does not apply.
dotnet pack "$root/src/Keel.Endpoints" -c Release -p:Version="$version" -p:KeelPackageValidation=false -o "$feed" ${KEEL_PACK_ARGS:-}
dotnet pack "$root/src/Keel.Endpoints.Templates" -c Release -p:Version="$version" -o "$feed" ${KEEL_PACK_ARGS:-}

dotnet new uninstall Keel.Endpoints.Templates > /dev/null 2>&1 || true
dotnet new install "$feed/Keel.Endpoints.Templates.$version.nupkg"

{
  echo '<configuration><packageSources><clear />'
  echo "  <add key=\"keel\" value=\"$feed\" />"
  for s in ${KEEL_EXTRA_SOURCES:-}; do echo "  <add key=\"$(basename "$s")\" value=\"$s\" />"; done
  [[ -z "${KEEL_EXTRA_SOURCES:-}" ]] && echo '  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
  echo '</packageSources></configuration>'
} > "$work/nuget.config"

fail=0
check() { if [[ "$3" == "$2" ]]; then echo "  PASS  $1"; else echo "  FAIL  $1 (got $3, expected $2)"; fail=1; fi; }
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
wait_up() { for _ in $(seq 1 80); do curl -s "$1" > /dev/null && return; sleep 0.25; done; }

# 1. keel-api + keel-slice, JIT
cd "$work" && dotnet new keel-api -n Shop && cd Shop
dotnet new keel-slice -n Rename --feature Todos --method put
dotnet new keel-slice -n Get --feature Orders
dotnet new keel-slice -n Create --feature Orders --method post
cat > Features/Orders/OrdersGroup.cs <<'EOF'
using Keel.Endpoints;
namespace Shop.Features.Orders;
public sealed class OrdersGroup : EndpointGroup
{
    public override void Configure(EndpointOptions options) => options.AllowAnonymous();
}
EOF
dotnet build -warnaserror ${KEEL_BUILD_ARGS:-}
dotnet run --no-build --urls http://127.0.0.1:5097 > run.log 2>&1 & pid=$!
wait_up http://127.0.0.1:5097/api/health
check "keel-api: health"                 200 "$(status http://127.0.0.1:5097/api/health)"
check "keel-api: create todo"            201 "$(status -H 'Content-Type: application/json' -d '{"title":"x"}' http://127.0.0.1:5097/api/todos)"
check "keel-api: validation"             400 "$(status -H 'Content-Type: application/json' -d '{"title":""}' http://127.0.0.1:5097/api/todos)"
check "keel-slice put: body validation"  400 "$(status -X PUT -H 'Content-Type: application/json' -d '{"name":""}' http://127.0.0.1:5097/api/todos/1)"
check "keel-api: scalar page"            200 "$(status -L http://127.0.0.1:5097/scalar)"   # Scalar redirects /scalar to /scalar/
kill $pid 2>/dev/null || true

# 2. keel-api --aot, Native AOT
cd "$work" && dotnet new keel-api -n ShopAot --aot --no-restore && cd ShopAot
dotnet publish -c Release -r "$rid" -o out ${KEEL_BUILD_ARGS:-}
./out/ShopAot --urls http://127.0.0.1:5098 > run.log 2>&1 & pid=$!
wait_up http://127.0.0.1:5098/api/health
check "keel-api --aot: health"           200 "$(status http://127.0.0.1:5098/api/health)"
check "keel-api --aot: create todo"      201 "$(status -H 'Content-Type: application/json' -d '{"title":"x"}' http://127.0.0.1:5098/api/todos)"
check "keel-api --aot: validation"       400 "$(status -H 'Content-Type: application/json' -d '{"title":""}' http://127.0.0.1:5098/api/todos)"
kill $pid 2>/dev/null || true

exit $fail
