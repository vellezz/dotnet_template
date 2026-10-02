# One image for both profiles; the Gateway__Profile variable selects bff-web or gateway-mobile. Build from the repository root:
#   docker build -f src/Gateway/SuperApp.Gateway/Dockerfile -t superapp-gateway .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Gateway/SuperApp.Gateway/SuperApp.Gateway.csproj -c Release -o /app

# Runtime: chiseled image (no shell, non-root user), "extra" variant with ICU and tzdata:
# Microsoft.Data.SqlClient does not support globalization-invariant mode, which the plain chiseled image enforces.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SuperApp.Gateway.dll"]
