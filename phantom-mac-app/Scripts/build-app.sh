#!/bin/zsh
set -euo pipefail

SCRIPT_DIR=${0:A:h}
ROOT_DIR=${SCRIPT_DIR:h}
APP_BUNDLE="$ROOT_DIR/dist/Phantom.app"

export CLANG_MODULE_CACHE_PATH="$ROOT_DIR/.build-cache/clang"
export SWIFTPM_CONFIG_DIR="$ROOT_DIR/.build-cache/swiftpm/config"
export SWIFTPM_SECURITY_DIR="$ROOT_DIR/.build-cache/swiftpm/security"
export SWIFTPM_CACHE_PATH="$ROOT_DIR/.build-cache/swiftpm/cache"
mkdir -p "$CLANG_MODULE_CACHE_PATH" "$SWIFTPM_CONFIG_DIR" "$SWIFTPM_SECURITY_DIR" "$SWIFTPM_CACHE_PATH"

if [[ -d /Library/Developer/CommandLineTools/SDKs/MacOSX15.4.sdk ]]; then
    export SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX15.4.sdk
fi

swift build --disable-sandbox --package-path "$ROOT_DIR" -c release --product Phantom
BIN_DIR=$(swift build --disable-sandbox --package-path "$ROOT_DIR" -c release --show-bin-path)

if [[ "$APP_BUNDLE" != "$ROOT_DIR/dist/Phantom.app" ]]; then
    print -u2 "Unexpected app bundle path."
    exit 1
fi

rm -rf -- "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Resources"
cp "$BIN_DIR/Phantom" "$APP_BUNDLE/Contents/MacOS/Phantom"
cp "$ROOT_DIR/Packaging/Info.plist" "$APP_BUNDLE/Contents/Info.plist"
cp "$ROOT_DIR/phantom.hosted.json" "$APP_BUNDLE/Contents/Resources/phantom.hosted.json"
cp "$ROOT_DIR/../phantom-windows-app/Assets/brand/phantom-logo-512.png" "$APP_BUNDLE/Contents/Resources/phantom-logo.png"
cp "$ROOT_DIR/../phantom-windows-app/assets/mermaid/mermaid.min.js" "$APP_BUNDLE/Contents/Resources/mermaid.min.js"
plutil -lint "$APP_BUNDLE/Contents/Info.plist"
codesign --force --sign - --options runtime "$APP_BUNDLE"

print "$APP_BUNDLE"
