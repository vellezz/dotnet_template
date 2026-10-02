# Image of the superapp-migrator Job (dev/test only; production migrations are run by the DBA, ADR-0004). Build from the repository root:
#   docker build -f src/Migrator/SuperApp.Migrator/Dockerfile -t superapp-migrator .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Migrator/SuperApp.Migrator/SuperApp.Migrator.csproj -c Release -o /app /p:OpenApiGenerateDocuments=false

# Runtime: chiseled image (no shell, non-root user), "extra" variant with ICU and tzdata:
# Microsoft.Data.SqlClient does not support globalization-invariant mode, which the plain chiseled image enforces.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "SuperApp.Migrator.dll"]
