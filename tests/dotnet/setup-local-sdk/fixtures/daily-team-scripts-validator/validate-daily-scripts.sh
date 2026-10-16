#!/usr/bin/env bash
# Behavioral check of the generated bash team script for a daily SDK. Runs the script against a
# stubbed network and a stubbed dotnet, and checks the outcomes, not the syntax:
#   1. manifests update mode is switched on before workload install, and build servers are shut down
#   2. a workload installed from an older band fails the script
#   3. a workload from another lane of the same rc band fails the script
#   4. without a NuGet config the script stops before workload install
#   5. when workload install fails, build servers are still shut down and the failure is propagated
#   6. workload install receives the requested ids (the fake dotnet rejects a call without ios and android)
#   7. the successful run leaves a valid global.json whose sdk.version is the SDK version and sdk.paths is [.dotnet, $host$], and .dotnet/ in .gitignore
#   8. dotnet-install is called with --channel 12.0.1xx --quality daily --install-dir .dotnet
#   9. the workspace NuGet config is valid XML that clears inherited sources and has active nuget.org, dotnet12,
#      android and macios daily sources on the expected dnceng host
#  10. a wrong-band or wrong-lane workload is caught in any position of the list, not only the first
#  11. run without arguments, the script installs ios and android by default
#  12. a NuGet config without an enabled dotnet feed (absent, commented out, disabled, or a look-alike URL on another host) stops the script before workload install
#  13. a failing workload config stops the script before workload install; a failing build-server shutdown fails the script
#  14. the installer is fetched from an official dotnet-install.sh URL (the network stubs refuse any other address)
set -uo pipefail
DOTNET_FEED=""
while [ $# -gt 0 ]; do case "$1" in --dotnet-feed) DOTNET_FEED="$2"; shift ;; *) echo "unknown argument: $1" >&2; exit 2 ;; esac; shift; done
[ -n "$DOTNET_FEED" ] || { echo "usage: $0 --dotnet-feed <name>" >&2; exit 2; }
export DOTNET_FEED
cd "$(dirname "$0")/.." || exit 2
candidate="$(ls install*.sh 2>/dev/null | head -1)"
[ -n "$candidate" ] || { echo "no install*.sh in the workspace" >&2; exit 1; }
root="$(mktemp -d "${TMPDIR:-/tmp}/daily-scripts-check.XXXXXX")"; trap 'rm -rf "$root"' EXIT
stubs="$root/stubs"; mkdir -p "$stubs"
# fake dotnet: logs every call, answers --version and workload list from the scenario
cat > "$stubs/fake-dotnet" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$FAKE_LOG"
case "${1:-}" in
  --version) echo "$FAKE_SDK_VERSION" ;;
  nuget) [ "${2:-}" = list ] && cat "$FAKE_SOURCES" ;;
  build-server) exit "${FAKE_SHUTDOWN_EXIT:-0}" ;;
  workload) case "${2:-}" in
    list) cat "$FAKE_LIST" ;;
    config) exit "${FAKE_CONFIG_EXIT:-0}" ;;
    install) shift 2; case " $* " in *" ios "*) ;; *) echo "fake dotnet: workload install without ios: $*" >&2; exit 2 ;; esac
             case " $* " in *" android "*) ;; *) echo "fake dotnet: workload install without android: $*" >&2; exit 2 ;; esac
             exit "${FAKE_INSTALL_EXIT:-0}" ;;
  esac ;;
esac
exit 0
STUB
# fake dotnet-install: creates <install-dir>/dotnet from the fake dotnet
cat > "$stubs/fake-install.sh" <<'STUB'
#!/usr/bin/env bash
dir=""; channel=""; quality=""
while [ $# -gt 0 ]; do case "$1" in
  --install-dir|-i) dir="$2"; shift ;; --install-dir=*) dir="${1#*=}" ;;
  --channel|-c) channel="$2"; shift ;; --channel=*) channel="${1#*=}" ;;
  --quality|-q) quality="$2"; shift ;; --quality=*) quality="${1#*=}" ;;
esac; shift; done
d="${dir#./}"; d="${d%/}"
if [ "$channel" != "$EXPECT_CHANNEL" ] || [ "$quality" != daily ] || [ "$d" != .dotnet ]; then
  echo "fake dotnet-install: expected --channel $EXPECT_CHANNEL --quality daily --install-dir .dotnet, got channel='$channel' quality='$quality' install-dir='$dir'" >&2; exit 3
fi
mkdir -p "$dir" && cp "$FAKE_DOTNET_SRC" "$dir/dotnet" && chmod +x "$dir/dotnet"
STUB
# curl / wget stubs: serve the fake installer to -o/-O <file> or to stdout, but only for an official dotnet-install.sh URL;
# anything else (another host, no URL) fails like a real download would
cat > "$stubs/curl" <<'STUB'
#!/usr/bin/env bash
out=""; url=""; while [ $# -gt 0 ]; do case "$1" in -o|--output) out="$2"; shift ;; -[a-zA-Z]*o) out="$2"; shift ;; --output=*) out="${1#*=}" ;; http://*|https://*) url="$1" ;; esac; shift; done
case "$url" in https://dot.net/v1/dotnet-install.sh|https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh|https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh) ;; *) echo "fake curl: not an official dotnet-install URL: '$url'" >&2; exit 22 ;; esac
if [ -n "$out" ]; then cp "$FAKE_INSTALL_SRC" "$out"; else cat "$FAKE_INSTALL_SRC"; fi
STUB
cat > "$stubs/wget" <<'STUB'
#!/usr/bin/env bash
out=""; url=""; while [ $# -gt 0 ]; do case "$1" in -O|--output-document) out="$2"; shift ;; -*O) out="$2"; shift ;; --output-document=*) out="${1#*=}" ;; http://*|https://*) url="$1" ;; esac; shift; done
case "$url" in https://dot.net/v1/dotnet-install.sh|https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh|https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh) ;; *) echo "fake wget: not an official dotnet-install URL: '$url'" >&2; exit 8 ;; esac
if [ -n "$out" ] && [ "$out" != "-" ]; then cp "$FAKE_INSTALL_SRC" "$out"; else cat "$FAKE_INSTALL_SRC"; fi
STUB
chmod +x "$stubs"/*
export FAKE_DOTNET_SRC="$stubs/fake-dotnet" FAKE_INSTALL_SRC="$stubs/fake-install.sh" FAKE_INSTALL_EXIT=0 FAKE_CONFIG_EXIT=0 FAKE_SHUTDOWN_EXIT=0 EXPECT_CHANNEL="12.0.1xx"
RUN_ARGS="ios android"  # arguments passed to the candidate; empty for the no-argument default case
failures=0
# the NuGet config next to the scripts: valid XML, with the four sources active
cfg=""; for f in NuGet.Config NuGet.config nuget.config; do if [ -f "$f" ]; then cfg="$f"; break; fi; done
py=""
for python_cmd in python3 python; do
  if command -v "$python_cmd" >/dev/null 2>&1 && "$python_cmd" --version >/dev/null 2>&1; then
    py="$(command -v "$python_cmd")"
    break
  fi
done
if [ -z "$cfg" ]; then echo "FAIL  no NuGet config in the workspace"; failures=$((failures+1))
elif [ -z "$py" ]; then echo "FAIL  python is needed to parse $cfg"; failures=$((failures+1))
elif "$py" - "$cfg" <<'PY'
import sys, xml.etree.ElementTree as ET
try:
    root = ET.parse(sys.argv[1]).getroot()
except ET.ParseError as exc:
    print("not valid XML: %s" % exc); sys.exit(1)
if root.find("./packageSources/clear") is None:
    print("no <clear /> under <packageSources>: user-level feeds would be inherited and the setup is not reproducible"); sys.exit(1)
adds = {a.get("key", ""): a.get("value", "") for a in root.findall("./packageSources/add")}
# NuGet disables a source only when the entry's value is true; value="false" leaves it enabled
disabled = {d.get("key", "") for d in root.findall("./disabledPackageSources/add") if d.get("value", "true").strip().lower() == "true"}
active = [v for k, v in adds.items() if k not in disabled]
import os, re
dnceng = "https://pkgs.dev.azure.com/dnceng/public/_packaging/%s/nuget/v3/index.json"
present = {v.strip() for v in active}
need = {
    "nuget.org": lambda u: u == "https://api.nuget.org/v3/index.json",
    os.environ["DOTNET_FEED"]: lambda u: u == dnceng % os.environ["DOTNET_FEED"],
    "android": lambda u: re.fullmatch(r"https://pkgs\.dev\.azure\.com/dnceng/public/_packaging/darc-pub-dotnet-android-[^/]+/nuget/v3/index\.json", u) is not None,
    "macios": lambda u: re.fullmatch(r"https://pkgs\.dev\.azure\.com/dnceng/public/_packaging/darc-pub-dotnet-macios-[^/]+/nuget/v3/index\.json", u) is not None,
}
missing = [name for name, matches in need.items() if not any(matches(url) for url in present)]
if missing:
    print("no active source for: %s" % ", ".join(missing)); sys.exit(1)
PY
then echo "ok    $cfg has active nuget.org, dotnet12, android and macios sources"
else echo "FAIL  $cfg: see above"; failures=$((failures+1)); fi
D11="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json"; D12="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet12/nuget/v3/index.json"; NUGET="https://api.nuget.org/v3/index.json"
run() { # name, sdk version, expected exit (0 or nonzero), config (yes / no / nofeed / disabled / commented), list lines...
  local name="$1" version="$2" expect="$3" config="$4"; shift 4
  local box="$root/$name"; mkdir -p "$box"; cp "$candidate" "$box/install.sh"
  export FAKE_LOG="$box/calls.log" FAKE_SDK_VERSION="$version" FAKE_LIST="$box/list.txt" FAKE_SOURCES="$box/sources.txt"
  : > "$FAKE_LOG"; { printf 'Installed Workload Id   Manifest Versions   Installation Source\n---\n'; printf '%s\n' "$@"; } > "$FAKE_LIST"
  # the config file and what `dotnet nuget list source --format Short` reports for it (E enabled, D disabled)
  case "$config" in
    yes) printf '<configuration><packageSources><add key="dotnet11" value="%s" /><add key="dotnet12" value="%s" /></packageSources></configuration>' "$D11" "$D12" > "$box/NuGet.config"; printf 'E %s\nE %s\n' "$D11" "$D12" > "$FAKE_SOURCES" ;;
    nofeed) printf '<configuration><packageSources><add key="nuget.org" value="%s" /></packageSources></configuration>' "$NUGET" > "$box/NuGet.config"; printf 'E %s\n' "$NUGET" > "$FAKE_SOURCES" ;;
    disabled) printf '<configuration><packageSources><add key="nuget.org" value="%s" /><add key="dotnet12" value="%s" /></packageSources><disabledPackageSources><add key="dotnet12" value="true" /></disabledPackageSources></configuration>' "$NUGET" "$D12" > "$box/NuGet.config"; printf 'E %s\nD %s\n' "$NUGET" "$D12" > "$FAKE_SOURCES" ;;
    commented) printf '<configuration><packageSources><!-- <add key="dotnet12" value="%s" /> --><add key="nuget.org" value="%s" /></packageSources></configuration>' "$D12" "$NUGET" > "$box/NuGet.config"; printf 'E %s\n' "$NUGET" > "$FAKE_SOURCES" ;;
    lookalike) printf '<configuration><packageSources><add key="nuget.org" value="%s" /><add key="dotnet12" value="https://pkgs.dev.azureXcom/dnceng/public/_packaging/dotnet12/nuget/v3/index.json" /></packageSources></configuration>' "$NUGET" > "$box/NuGet.config"; printf 'E %s\nE https://pkgs.dev.azureXcom/dnceng/public/_packaging/dotnet12/nuget/v3/index.json\n' "$NUGET" > "$FAKE_SOURCES" ;;
    bogus) printf '<configuration><packageSources><add key="nuget.org" value="%s" /><add key="dotnet12" value="https://example.invalid/_packaging/dotnet12/not-a-feed" /></packageSources></configuration>' "$NUGET" > "$box/NuGet.config"; printf 'E %s\nE https://example.invalid/_packaging/dotnet12/not-a-feed\n' "$NUGET" > "$FAKE_SOURCES" ;;
    *) : > "$FAKE_SOURCES" ;;
  esac
  # shellcheck disable=SC2086
  local rc=0; ( cd "$box" && PATH="$stubs:$PATH" bash ./install.sh $RUN_ARGS ) > "$box/out.txt" 2>&1 || rc=$?
  if [ "$expect" = 0 ] && [ "$rc" -ne 0 ]; then echo "FAIL  $name: exited $rc, expected success"; sed 's/^/      /' "$box/out.txt" | tail -5; failures=$((failures+1)); return; fi
  if [ "$expect" != 0 ] && [ "$rc" -eq 0 ]; then echo "FAIL  $name: exited 0, expected a failure"; failures=$((failures+1)); return; fi
  echo "ok    $name (exit $rc)"
}
V12="12.0.100-alpha.1.26509.101"
run alpha-right-band "$V12" 0 yes "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1" "android 37.99.0-preview.1.145/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
# the successful run must switch to manifests mode before installing, and shut down build servers
if [ -f "$root/alpha-right-band/calls.log" ]; then
  cfg=$(grep -n '^workload config --update-mode manifests' "$root/alpha-right-band/calls.log" | head -1 | cut -d: -f1)
  ins=$(grep -n '^workload install' "$root/alpha-right-band/calls.log" | head -1 | cut -d: -f1)
  if [ -z "$cfg" ] || [ -z "$ins" ] || [ "$cfg" -gt "$ins" ]; then echo "FAIL  manifests update mode is not switched on before workload install"; failures=$((failures+1)); else echo "ok    manifests update mode before workload install"; fi
  sd=$(grep -n '^build-server shutdown' "$root/alpha-right-band/calls.log" | tail -1 | cut -d: -f1)
  if [ -n "$sd" ] && [ -n "$ins" ] && [ "$sd" -gt "$ins" ]; then echo "ok    build-server shutdown after workload install"; else echo "FAIL  build-server shutdown missing or not after workload install"; failures=$((failures+1)); fi
  gj="$root/alpha-right-band/global.json"
  if [ -f "$gj" ] && [ -n "$py" ] && "$py" - "$gj" "$V12" <<'PY'
import json, sys
try:
    doc = json.load(open(sys.argv[1]))
except Exception as exc:
    print("global.json is not valid JSON: %s" % exc); sys.exit(1)
sdk = doc.get("sdk") if isinstance(doc, dict) else None
if not isinstance(sdk, dict) or sdk.get("version") != sys.argv[2] or sdk.get("paths") != [".dotnet", "$host$"]:
    print("sdk.version=%r sdk.paths=%r, expected %r and ['.dotnet', '$host$']" % ((sdk or {}).get("version"), (sdk or {}).get("paths"), sys.argv[2])); sys.exit(1)
PY
  then echo "ok    global.json pins $V12 with sdk.paths [.dotnet, \$host\$]"; else echo "FAIL  global.json missing, not valid JSON, or sdk.version / sdk.paths wrong (see above)"; failures=$((failures+1)); fi
  if [ -f "$root/alpha-right-band/.gitignore" ] && grep -qx '\.dotnet/' "$root/alpha-right-band/.gitignore"; then echo "ok    .gitignore excludes .dotnet/"; else echo "FAIL  .gitignore does not exclude .dotnet/"; failures=$((failures+1)); fi
fi
run alpha-stale-band "$V12" 1 yes "ios     26.5.12193-net11-rc.1/11.0.100-rc.1   SDK 12.0.100-alpha.1" "android 37.99.0-preview.1.145/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
run alpha-stale-second "$V12" 1 yes "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1" "android 37.0.0-rc.1.2257/11.0.100-rc.1   SDK 12.0.100-alpha.1"
run no-config "$V12" 1 no "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
if [ -f "$root/no-config/calls.log" ] && grep -q '^workload install' "$root/no-config/calls.log"; then echo "FAIL  workload install ran without a NuGet config"; failures=$((failures+1)); else echo "ok    no workload install without a NuGet config"; fi
for cfgcase in nofeed disabled commented bogus lookalike; do
  run "config-$cfgcase" "$V12" 1 "$cfgcase" "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
  if [ -f "$root/config-$cfgcase/calls.log" ] && grep -q '^workload install' "$root/config-$cfgcase/calls.log"; then echo "FAIL  workload install ran although the dotnet12 feed is $cfgcase"; failures=$((failures+1)); else echo "ok    no workload install when the dotnet12 feed is $cfgcase"; fi
done
RUN_ARGS="" run alpha-no-args "$V12" 0 yes "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1" "android 37.99.0-preview.1.145/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
if [ -f "$root/alpha-no-args/calls.log" ] && grep -q '^workload install' "$root/alpha-no-args/calls.log"; then echo "ok    no arguments: workload install ran with the default ids"; else echo "FAIL  no arguments: workload install did not run (no default workloads)"; failures=$((failures+1)); fi
FAKE_INSTALL_EXIT=1 run install-fails "$V12" 1 yes "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1" "android 37.99.0-preview.1.145/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
ins2=$(grep -n '^workload install' "$root/install-fails/calls.log" 2>/dev/null | head -1 | cut -d: -f1); sd2=$(grep -n '^build-server shutdown' "$root/install-fails/calls.log" 2>/dev/null | tail -1 | cut -d: -f1)
if [ -n "$sd2" ] && [ -n "$ins2" ] && [ "$sd2" -gt "$ins2" ]; then echo "ok    build-server shutdown after a failed install"; else echo "FAIL  build-server shutdown missing or not after the failed install"; failures=$((failures+1)); fi
FAKE_CONFIG_EXIT=1 run config-fails "$V12" 1 yes "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1" "android 37.99.0-preview.1.145/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
if [ -f "$root/config-fails/calls.log" ] && grep -q '^workload install' "$root/config-fails/calls.log"; then echo "FAIL  workload install ran after workload config failed"; failures=$((failures+1)); else echo "ok    no workload install after a failed workload config"; fi
FAKE_SHUTDOWN_EXIT=1 run shutdown-fails "$V12" 1 yes "ios     27.0.12277-net12-p1/12.0.100-alpha.1   SDK 12.0.100-alpha.1" "android 37.99.0-preview.1.145/12.0.100-alpha.1   SDK 12.0.100-alpha.1"
[ "$failures" -eq 0 ] && echo "all checks passed" || { echo "$failures check(s) failed"; exit 1; }
