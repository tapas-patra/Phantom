#!/usr/bin/env bash
# Build the same desktop zip names as .github/workflows/desktop-release.yml,
# locally, without GitHub artifact storage.
#
#   ./build-desktop-artifacts.sh              # this OS only
#   ./build-desktop-artifacts.sh --mac
#   ./build-desktop-artifacts.sh --windows
#   ./build-desktop-artifacts.sh --all
#   ./build-desktop-artifacts.sh --mac --no-self-check
#
# Output lives in release/local/ (gitignored):
#   Phantom-macOS.zip
#   Phantom-Windows-x64.zip
#   macos/Phantom.app          <- open this to test on Mac
#   windows/svchost-shell.exe  <- run this to test on Windows
#
# Windows WPF cannot be built on macOS. Run Build-Desktop-Artifacts.ps1
# on a Windows machine for Phantom-Windows-x64.zip.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/release/local"
WANT_MAC=0
WANT_WINDOWS=0
WANT_ALL=0
SELF_CHECK=1
CLEAN=1
PHANTOM_PRODUCT_VERSION="${PHANTOM_PRODUCT_VERSION:-0.0.0-local}"
export PHANTOM_PRODUCT_VERSION

usage() {
  awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "$0"
  exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mac) WANT_MAC=1 ;;
    --windows) WANT_WINDOWS=1 ;;
    --all) WANT_ALL=1 ;;
    --no-self-check) SELF_CHECK=0 ;;
    --no-clean) CLEAN=0 ;;
    -h|--help) usage 0 ;;
    *)
      echo "Unknown argument: $1" >&2
      usage 1
      ;;
  esac
  shift
done

uname_s="$(uname -s 2>/dev/null || echo unknown)"
on_mac=0
on_windows=0
case "$uname_s" in
  Darwin) on_mac=1 ;;
  MINGW*|MSYS*|CYGWIN*) on_windows=1 ;;
esac
if [[ -n "${OS:-}" && "$OS" == "Windows_NT" ]]; then
  on_windows=1
fi

if [[ $WANT_ALL -eq 1 ]]; then
  WANT_MAC=1
  WANT_WINDOWS=1
elif [[ $WANT_MAC -eq 0 && $WANT_WINDOWS -eq 0 ]]; then
  if [[ $on_mac -eq 1 ]]; then
    WANT_MAC=1
  elif [[ $on_windows -eq 1 ]]; then
    WANT_WINDOWS=1
  else
    echo "This OS cannot build Phantom desktop apps. Use macOS or Windows." >&2
    exit 1
  fi
fi

git_sha="$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo unknown)"
built_at="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
skipped=()
built=()
join_or_none() {
  if [[ $# -eq 0 ]]; then
    printf '%s' none
  else
    local IFS=,
    printf '%s' "$*"
  fi
}

need_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

prepare_out() {
  mkdir -p "$OUT"
  if [[ $CLEAN -eq 1 ]]; then
    rm -rf "$OUT/macos" "$OUT/windows" \
      "$OUT/Phantom-macOS.zip" "$OUT/Phantom-Windows-x64.zip" \
      "$OUT/MANIFEST.txt"
  fi
  mkdir -p "$OUT"
}

write_manifest() {
  {
    echo "Phantom local desktop artifacts"
    echo "git=$git_sha"
    echo "built_at_utc=$built_at"
    echo "version=$PHANTOM_PRODUCT_VERSION"
    echo "host=$(uname -a 2>/dev/null || echo unknown)"
    echo "built=$(join_or_none "${built[@]+"${built[@]}"}")"
    echo "skipped=$(join_or_none "${skipped[@]+"${skipped[@]}"}")"
  } > "$OUT/MANIFEST.txt"
}

build_mac() {
  if [[ $on_mac -ne 1 ]]; then
    echo "Skipping macOS artifact: this host is not macOS."
    skipped+=("macos")
    return 0
  fi

  need_cmd swift
  need_cmd ditto
  local builder="$ROOT/phantom-mac-app/Scripts/build-app.sh"
  if [[ ! -x "$builder" ]]; then
    chmod +x "$builder"
  fi

  echo "==> Building macOS app (same path as CI)"
  "$builder"

  local app="$ROOT/phantom-mac-app/dist/Phantom.app"
  if [[ ! -d "$app" ]]; then
    echo "macOS build did not produce $app" >&2
    exit 1
  fi

  mkdir -p "$OUT/macos"
  rm -rf "$OUT/macos/Phantom.app"
  ditto "$app" "$OUT/macos/Phantom.app"
  xattr -cr "$OUT/macos/Phantom.app" 2>/dev/null || true

  echo "==> Packaging Phantom-macOS.zip"
  rm -f "$OUT/Phantom-macOS.zip"
  ditto -c -k --sequesterRsrc --keepParent "$app" "$OUT/Phantom-macOS.zip"

  if [[ $SELF_CHECK -eq 1 ]]; then
    echo "==> Running --self-check"
    "$OUT/macos/Phantom.app/Contents/MacOS/Phantom" --self-check
  fi

  built+=("macos")
  echo "macOS artifact: $OUT/Phantom-macOS.zip"
  echo "Test with: open $OUT/macos/Phantom.app"
}

build_windows() {
  if [[ $on_windows -ne 1 ]]; then
    echo "Skipping Windows artifact: WPF / net8.0-windows must be published on Windows."
    echo "On a Windows machine run:"
    echo "  powershell -ExecutionPolicy Bypass -File .\\Build-Desktop-Artifacts.ps1"
    skipped+=("windows")
    return 0
  fi

  need_cmd dotnet
  echo "==> Publishing Windows app (same flags as CI)"
  mkdir -p "$OUT/windows"
  dotnet publish "$ROOT/phantom-windows-app/SecureOverlay.csproj" \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PhantomProductVersion="${PHANTOM_PRODUCT_VERSION:-0.0.0-local}" \
    -p:IncludeSourceRevisionInInformationalVersion=false \
    -o "$OUT/windows"

  echo "==> Packaging Phantom-Windows-x64.zip"
  rm -f "$OUT/Phantom-Windows-x64.zip"
  if command -v powershell.exe >/dev/null 2>&1; then
    powershell.exe -NoProfile -Command \
      "Compress-Archive -Path '$OUT/windows/*' -DestinationPath '$OUT/Phantom-Windows-x64.zip' -Force"
  elif command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -Command \
      "Compress-Archive -Path '$OUT/windows/*' -DestinationPath '$OUT/Phantom-Windows-x64.zip' -Force"
  else
    echo "Need powershell.exe or pwsh to create Phantom-Windows-x64.zip" >&2
    exit 1
  fi

  built+=("windows")
  echo "Windows artifact: $OUT/Phantom-Windows-x64.zip"
  echo "Test with: $OUT/windows/svchost-shell.exe"
}

prepare_out
if [[ $WANT_MAC -eq 1 ]]; then
  build_mac
fi
if [[ $WANT_WINDOWS -eq 1 ]]; then
  build_windows
fi
write_manifest

echo
echo "Done. See $OUT/MANIFEST.txt"
if [[ ${#built[@]} -eq 0 ]]; then
  echo "No artifacts were built." >&2
  exit 1
fi
if [[ $WANT_WINDOWS -eq 1 && $on_windows -ne 1 ]]; then
  echo "Windows zip was not produced on this Mac. Build it on Windows with Build-Desktop-Artifacts.ps1."
fi
