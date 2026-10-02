# Build from the repository root:
#   docker build -f src/Analytics/SuperApp.AnalyticsForwarder/Dockerfile -t analytics-forwarder .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Analytics/SuperApp.AnalyticsForwarder/SuperApp.AnalyticsForwarder.csproj -c Release -o /app

# Runtime: chiseled image (no shell, non-root user), "extra" variant with ICU and tzdata, the same as the services.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SuperApp.AnalyticsForwarder.dll"]
