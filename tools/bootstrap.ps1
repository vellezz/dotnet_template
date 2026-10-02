# Builds the developer tool SuperApp.Cli into tools/packages and restores the local .NET tools (dotnet-ef, refitter, superapp).
# Run once after cloning and after every change of src/Tools/SuperApp.Cli (ADR-0046). Then: dotnet superapp --help
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Tools/SuperApp.Cli/SuperApp.Cli.csproj'
$packages = Join-Path $root 'tools/packages'

[xml]$xml = Get-Content $project
$version = @($xml.Project.PropertyGroup | Where-Object { $_.Version } | ForEach-Object { $_.Version })[0]

dotnet pack $project --configuration Release --output $packages --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# NuGet caches packages by version: remove the cached copy, so a rebuilt tool is picked up even when the version did not change.
$globalPackages = ((dotnet nuget locals global-packages --list) -replace '^[^:]+:\s*', '').Trim()
$cached = Join-Path $globalPackages "superapp.cli/$version"
if (Test-Path $cached) { Remove-Item -Recurse -Force $cached }
# The local tool resolver remembers the entry point per package version: forget it, so a changed entry point is found.
$dotnetHome = if ($env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME } else { $HOME }
$resolverCache = Join-Path $dotnetHome '.dotnet/toolResolverCache/1/superapp.cli'
if (Test-Path $resolverCache) { Remove-Item -Force $resolverCache }

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "SuperApp.Cli $version installed. Try: dotnet superapp doctor"
}
finally {
    Pop-Location
}
