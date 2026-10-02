#!/usr/bin/env bash
# Builds the developer tool SuperApp.Cli into tools/packages and restores the local .NET tools (dotnet-ef, refitter, superapp).
# Run once after cloning and after every change of src/Tools/SuperApp.Cli (ADR-0046). Then: dotnet superapp --help
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/src/Tools/SuperApp.Cli/SuperApp.Cli.csproj"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"

dotnet pack "$project" --configuration Release --output "$root/tools/packages" --nologo

# NuGet caches packages by version: remove the cached copy, so a rebuilt tool is picked up even when the version did not change.
global_packages="$(dotnet nuget locals global-packages --list | sed 's/^[^:]*:[[:space:]]*//')"
rm -rf "${global_packages%/}/superapp.cli/$version"
# The local tool resolver remembers the entry point per package version: forget it, so a changed entry point is found.
rm -f "${DOTNET_CLI_HOME:-$HOME}/.dotnet/toolResolverCache/1/superapp.cli"

cd "$root"
dotnet tool restore
echo "SuperApp.Cli $version installed. Try: dotnet superapp doctor"
