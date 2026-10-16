#!/usr/bin/env bash
# Root 33 playtest tabletop: runs the Release build of src/Root33.Sim directly (dotnet <dll>).
# Builds it first, through the shared build lock, only when the dll is missing or older than any .cs, .csproj or
# sheet source. Usage: tools/sim.sh <command> [options]   (tools/sim.sh help lists the commands)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="/root/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DLL="$ROOT/src/Root33.Sim/bin/Release/net8.0/Root33.Sim.dll"
LOCK="${ROOT33_DOTNET_LOCK:-/tmp/claude-0/-home-user-Claude-Meltify-Root-33/3b274f29-fe85-5a34-842a-3b19381a363a/scratchpad/dotnet.lock}"
mkdir -p "$(dirname "$LOCK")"

stale() {
  [ ! -f "$DLL" ] && return 0
  [ -n "$(find "$ROOT/src/Root33.Sim" "$ROOT/src/Root33.Core" "$ROOT/sheets" \
      -path '*/bin' -prune -o -path '*/obj' -prune -o \
      \( -name '*.cs' -o -name '*.csproj' -o -name '*.json' \) -newer "$DLL" -print -quit)" ]
}

if stale; then
  export -f stale
  export ROOT DLL
  flock "$LOCK" bash -c 'if stale; then
      log="$(mktemp)"
      if dotnet build "$ROOT/src/Root33.Sim/Root33.Sim.csproj" -c Release -nologo -v q >"$log" 2>&1; then touch "$DLL"; rm -f "$log";
      else cat "$log" >&2; rm -f "$log"; exit 1; fi
    fi'
fi
exec dotnet "$DLL" "$@"
