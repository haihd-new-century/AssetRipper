#!/usr/bin/env bash
# Cross-platform (macOS/Linux) equivalent of generate.bat.
# Builds AssetRipper.SourceGenerated.dll with the AssemblyDumper, decompiles it,
# recompiles it into a NuGet package, and strips the package's dependency references.
#
# Usage: ./generate.sh <package-version> [--download]
#   --download   fetch type_tree.tpk / engine_assets.tpk via AssemblyDumper.Downloader first

set -euo pipefail

if [[ $# -lt 1 || "$1" == -* ]]; then
	echo "Usage: $0 <package-version> [--download]" >&2
	exit 1
fi

VERSION="$1"
DOWNLOAD=false
[[ "${2:-}" == "--download" ]] && DOWNLOAD=true

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BINS="$SOURCE_DIR/0Bins"
DUMPER_DIR="$BINS/AssetRipper.AssemblyDumper/Debug"
RECOMPILER_DIR="$BINS/AssetRipper.AssemblyDumper.Recompiler/Debug"
FIXER_DIR="$BINS/AssetRipper.AssemblyDumper.NuGetFixer/Debug"
# The generated csproj references ..\..\..\..\AssetRipper.Assets, so Output must stay inside RECOMPILER_DIR.
OUTPUT_DIR="$RECOMPILER_DIR/Output"

step() { printf '\n==> %s\n' "$*"; }

# Run a built tool by its managed dll (works regardless of apphost / .exe naming).
run_tool() {
	local dir="$1" name="$2"; shift 2
	(cd "$dir" && dotnet "$name.dll" "$@")
}

step "Build solution (Debug)"
dotnet build -c Debug "$SOURCE_DIR/.."

if $DOWNLOAD; then
	step "Download TPK files"
	run_tool "$BINS/AssetRipper.AssemblyDumper.Downloader/Debug" AssetRipper.AssemblyDumper.Downloader
fi

step "Check AssemblyDumper inputs in $DUMPER_DIR"
missing=()
for f in type_tree.tpk engine_assets.tpk consolidated.json native_enums.json assemblies.json; do
	[[ -f "$DUMPER_DIR/$f" ]] || missing+=("$f")
done
if (( ${#missing[@]} > 0 )); then
	echo "Missing input files: ${missing[*]}" >&2
	echo "  *.tpk             -> rerun with --download" >&2
	echo "  consolidated.json -> AssetRipper.DocExtraction.ConsoleApp <unity-installations-dir>" >&2
	echo "  native_enums.json -> AssetRipper.AssemblyDumper.NativeEnumExtractor" >&2
	echo "  assemblies.json   -> reference assembly data (see Pass557_CreateSourceTpkClass)" >&2
	exit 1
fi

step "Generate AssetRipper.SourceGenerated.dll"
run_tool "$DUMPER_DIR" AssetRipper.AssemblyDumper

step "Decompile dll into a project"
run_tool "$RECOMPILER_DIR" AssetRipper.AssemblyDumper.Recompiler \
	"$DUMPER_DIR/AssetRipper.SourceGenerated.dll" "$OUTPUT_DIR" "$VERSION"

step "Recompile into a NuGet package"
dotnet build -c Release "$OUTPUT_DIR"

step "Remove dependency references from the package"
run_tool "$FIXER_DIR" AssetRipper.AssemblyDumper.NuGetFixer "$OUTPUT_DIR/bin/Release/" "$VERSION"

step "Done: $FIXER_DIR/AssetRipper.SourceGenerated.$VERSION.nupkg"
