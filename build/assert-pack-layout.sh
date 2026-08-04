#!/usr/bin/env bash
# Assert dry-run / release nupkgs match the v2 packaging contract (T15).
# Usage: bash ./build/assert-pack-layout.sh [nupkgs-dir]
set -euo pipefail

nupkgs_dir="${1:-nupkgs}"

if [[ ! -d "$nupkgs_dir" ]]; then
	echo "error: nupkgs directory not found: $nupkgs_dir" >&2
	exit 1
fi

required_packages=(
	Vexel.Telegram.Client
	Vexel.Telegram.Handlers
	Vexel.Telegram.Hosting
	Vexel.Telegram.AspNetCore
)

for id in "${required_packages[@]}"; do
	matches=("$nupkgs_dir"/"$id".*.nupkg)
	if [[ ! -e "${matches[0]}" ]]; then
		echo "error: missing nupkg for $id in $nupkgs_dir" >&2
		ls -la "$nupkgs_dir" >&2 || true
		exit 1
	fi
	echo "ok: found ${matches[0]##*/}"
done

handlers_nupkg=$(ls -1 "$nupkgs_dir"/Vexel.Telegram.Handlers.*.nupkg | head -n 1)
listing=$(unzip -Z1 "$handlers_nupkg")

if ! grep -qx 'analyzers/dotnet/cs/Vexel.Telegram.Generators.dll' <<<"$listing"; then
	echo "error: Handlers nupkg missing analyzers/dotnet/cs/Vexel.Telegram.Generators.dll" >&2
	echo "$listing" >&2
	exit 1
fi
echo "ok: Handlers embeds analyzers/dotnet/cs/Vexel.Telegram.Generators.dll"

# Generator must not also land under lib/ (would load as a normal reference assembly).
if grep -E 'lib/.+/Vexel\.Telegram\.Generators\.dll' <<<"$listing"; then
	echo "error: Generators.dll must not appear under lib/" >&2
	exit 1
fi
echo "ok: Generators.dll is analyzer-only (not under lib/)"

nuspec=$(unzip -p "$handlers_nupkg" '*.nuspec')
if ! grep -q 'id="Immediate.Handlers"' <<<"$nuspec"; then
	echo "error: Handlers nuspec missing Immediate.Handlers dependency (peer honesty / P4)" >&2
	echo "$nuspec" >&2
	exit 1
fi
echo "ok: Handlers depends on Immediate.Handlers"

# Generators is not a published package - must not appear as a dependency.
if grep -q 'id="Vexel.Telegram.Generators"' <<<"$nuspec"; then
	echo "error: Handlers nuspec must not depend on Vexel.Telegram.Generators package id" >&2
	echo "$nuspec" >&2
	exit 1
fi
echo "ok: Handlers does not depend on a Generators package id"

echo "pack layout assertions passed"
