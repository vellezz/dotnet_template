#!/bin/bash
# Exports the ASP.NET Core development HTTPS certificate to deploy/local/certs/devcert.pfx for the gateways running in containers
# (the __Host-bff cookie requires HTTPS). The certificate is trusted locally by `dotnet dev-certs https --trust` (ADR-0034).
set -euo pipefail
password="${1:-dev-cert-password}"
target="$(cd "$(dirname "$0")" && pwd)/certs"
mkdir -p "$target"
dotnet dev-certs https --trust || true
dotnet dev-certs https -ep "$target/devcert.pfx" -p "$password"
echo "Certificate written to $target/devcert.pfx"
