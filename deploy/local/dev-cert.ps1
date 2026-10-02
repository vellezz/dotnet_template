# Exports the ASP.NET Core development HTTPS certificate to deploy/local/certs/devcert.pfx for the gateways running in containers
# (the __Host-bff cookie requires HTTPS). The certificate is trusted locally by `dotnet dev-certs https --trust` (ADR-0034).
param([string]$Password = "dev-cert-password")

$target = Join-Path $PSScriptRoot "certs"
New-Item -ItemType Directory -Force $target | Out-Null
dotnet dev-certs https --trust
dotnet dev-certs https -ep (Join-Path $target "devcert.pfx") -p $Password
Write-Host "Certificate written to $target\devcert.pfx"
