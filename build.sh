#!/usr/bin/env bash
# Build & publish AssetRipper.GUI.Free on macOS (also works on Linux).
# Mirrors .github/workflows/publish.yml: dotnet publish -c Release -r <rid> (Native AOT).
#
# Usage: ./build.sh [options]
#   -r, --runtime <rid>   osx-arm64 | osx-x64 | linux-x64 | linux-arm64   (default: host)
#   -c, --config <cfg>    Release | Debug                                   (default: Release)
#   -t, --test            run the unit tests before publishing
#       --clean           delete Source/0Bins before building
#       --no-aot          publish self-contained JIT build instead of Native AOT
#                         (faster, no Xcode tools needed, larger output)
#       --no-classic-linker  use Apple's default linker for Native AOT (see dotnet/runtime#119380)
#   -a, --archive         create AssetRipper_<name>.tar.xz like the CI does
#       --run             start the published app after building
#   -h, --help            show this help

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$ROOT/Source/AssetRipper.GUI.Free"
EXE="AssetRipper.GUI.Free"

CONFIG="Release"
RUNTIME=""
RUN_TESTS=false
CLEAN=false
AOT=true
CLASSIC_LINKER=true
ARCHIVE=false
RUN_APP=false

die()  { echo "error: $*" >&2; exit 1; }
step() { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
usage() { sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; exit 0; }

while [[ $# -gt 0 ]]; do
	case "$1" in
		-r|--runtime) RUNTIME="${2:?missing value for $1}"; shift 2 ;;
		-c|--config)  CONFIG="${2:?missing value for $1}"; shift 2 ;;
		-t|--test)    RUN_TESTS=true; shift ;;
		--clean)      CLEAN=true; shift ;;
		--no-aot)     AOT=false; shift ;;
		--no-classic-linker) CLASSIC_LINKER=false; shift ;;
		-a|--archive) ARCHIVE=true; shift ;;
		--run)        RUN_APP=true; shift ;;
		-h|--help)    usage ;;
		*) die "unknown option: $1 (see --help)" ;;
	esac
done

# Default runtime identifier = host OS/arch.
if [[ -z "$RUNTIME" ]]; then
	case "$(uname -s)-$(uname -m)" in
		Darwin-arm64)  RUNTIME="osx-arm64" ;;
		Darwin-x86_64) RUNTIME="osx-x64" ;;
		Linux-x86_64)  RUNTIME="linux-x64" ;;
		Linux-aarch64) RUNTIME="linux-arm64" ;;
		*) die "cannot detect runtime for $(uname -s)-$(uname -m); pass --runtime" ;;
	esac
fi
[[ "$CONFIG" == "Release" || "$CONFIG" == "Debug" ]] || die "config must be Release or Debug"

step "Checking prerequisites"
DOTNET="${DOTNET:-dotnet}"   # override with DOTNET=~/.dotnet/dotnet ./build.sh to use another SDK
command -v "$DOTNET" >/dev/null || die "dotnet not found. Install the .NET 10 SDK (see docs/articles/BuildingOnMac.md)"
SDK_VERSION="$("$DOTNET" --version)"
(( ${SDK_VERSION%%.*} >= 10 )) || die ".NET SDK $SDK_VERSION found, but 10.0+ is required"
DOTNET_ROOT_DIR="$("$DOTNET" --list-sdks | tail -1 | sed -E 's/.*\[(.*)\]$/\1/')"   # .../sdk
DOTNET_ROOT_DIR="$(dirname "$DOTNET_ROOT_DIR")"
echo "dotnet SDK: $SDK_VERSION ($DOTNET_ROOT_DIR)"
if $AOT && [[ "$(uname -s)" == "Darwin" ]]; then
	xcode-select -p >/dev/null 2>&1 || die "Native AOT needs Xcode Command Line Tools: run 'xcode-select --install' (or use --no-aot)"
	echo "Xcode tools: $(xcode-select -p)"
	# Source-built SDKs (e.g. Homebrew's dotnet) ship a non-portable NativeAOT pack that links
	# against libssl/libcrypto/libbrotli*, which macOS does not provide in the default search path.
	if find "$DOTNET_ROOT_DIR/packs" -path '*NativeAOT*' -name nonportable.txt -print -quit 2>/dev/null | grep -q .; then
		echo "warning: this SDK is source-built (non-portable); Native AOT will link against Homebrew libraries." >&2
		command -v brew >/dev/null || die "non-portable SDK needs Homebrew openssl@3 + brotli, or use the official SDK / --no-aot"
		LIBS=()
		for pkg in openssl@3 brotli; do
			prefix="$(brew --prefix "$pkg" 2>/dev/null)" && [[ -d "$prefix/lib" ]] || die "missing Homebrew package '$pkg': brew install $pkg"
			LIBS+=("$prefix/lib")
		done
		export LIBRARY_PATH="$(IFS=:; echo "${LIBS[*]}")${LIBRARY_PATH:+:$LIBRARY_PATH}"
		echo "warning: the resulting binary will only run on Macs that have these Homebrew libraries." >&2
		echo "         For a portable build use the official SDK: DOTNET=~/.dotnet/dotnet ./build.sh" >&2
	fi
fi
echo "Runtime: $RUNTIME | Config: $CONFIG | Native AOT: $AOT"

if $CLEAN; then
	step "Cleaning Source/0Bins"
	rm -rf "$ROOT/Source/0Bins"
fi

if $RUN_TESTS; then
	step "Running tests"
	"$DOTNET" test "$ROOT/AssetRipper.slnx" -c Debug
fi

step "Publishing $EXE"
PUBLISH_ARGS=(-c "$CONFIG" -r "$RUNTIME")
if ! $AOT; then
	PUBLISH_ARGS+=(-p:PublishAot=false --self-contained true -p:PublishSingleFile=false)
elif [[ "$(uname -s)" == "Darwin" ]] && $CLASSIC_LINKER; then
	# Xcode 16+/26 ld crashes on large NativeAOT objects ("too many large addends"),
	# see https://github.com/dotnet/runtime/issues/119380 — fall back to the classic linker.
	LINKER_DIR="$(mktemp -d)"
	trap 'rm -rf "$LINKER_DIR"' EXIT
	LINKER_TARGETS="$LINKER_DIR/ClassicLinker.targets"
	cat > "$LINKER_TARGETS" <<'EOF'
<Project>
	<ItemGroup>
		<LinkerArg Include="-Wl,-ld_classic" />
	</ItemGroup>
</Project>
EOF
	PUBLISH_ARGS+=("-p:CustomAfterMicrosoftCommonTargets=$LINKER_TARGETS")
	echo "Using classic Apple linker (-ld_classic); disable with --no-classic-linker"
fi
(cd "$PROJECT_DIR" && "$DOTNET" publish "${PUBLISH_ARGS[@]}")

OUT="$ROOT/Source/0Bins/AssetRipper.GUI.Free/$CONFIG/$RUNTIME/publish"
[[ -f "$OUT/$EXE" ]] || die "publish finished but $OUT/$EXE was not found"
date -u > "$OUT/compile_time.txt"
chmod +x "$OUT/$EXE"
# Locally built files are not quarantined, but clear the flag in case the tree was downloaded.
[[ "$(uname -s)" == "Darwin" ]] && xattr -dr com.apple.quarantine "$OUT" 2>/dev/null || true

if $ARCHIVE; then
	step "Creating archive"
	NAME="${RUNTIME/osx/mac}"; NAME="${NAME/-/_}"
	ARCHIVE_PATH="$OUT/AssetRipper_${NAME}.tar.xz"
	shopt -s nullglob
	if $AOT; then
		FILES=("$EXE" compile_time.txt *.dll *.so *.dylib)
	else
		FILES=(.)  # JIT build needs the whole publish folder
	fi
	(cd "$OUT" && tar cJf "$ARCHIVE_PATH" --exclude='*.tar.xz' --exclude='*.dSYM' "${FILES[@]}")
	echo "Archive: $ARCHIVE_PATH ($(du -h "$ARCHIVE_PATH" | cut -f1))"
fi

step "Done"
echo "Output:  $OUT"
echo "Size:    $(du -sh "$OUT" | cut -f1)"
echo "Run:     \"$OUT/$EXE\"            (add --headless --port 8080 for server mode)"

if $RUN_APP; then
	step "Starting AssetRipper"
	exec "$OUT/$EXE"
fi
