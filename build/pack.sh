#!/usr/bin/env bash
# Pack the v2 NuGet packages (T15). Assumes `dotnet build -c $CONFIGURATION` already ran.
# Usage: bash ./build/pack.sh [nupkgs-dir]
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

# shellcheck source=./packable-projects.sh
source "$script_dir/packable-projects.sh"

nupkgs_dir="${1:-nupkgs}"
configuration="${CONFIGURATION:-Release}"

rm -rf "$nupkgs_dir"
mkdir -p "$nupkgs_dir"

for project in "${packable_projects[@]}"; do
	dotnet pack "$repo_root/$project" -c "$configuration" --no-build -o "$nupkgs_dir"
done
