#!/bin/bash
# Local database bootstrap (ADR-0021, ADR-0034): creates the SuperApp database and the logins of the services and the migrator,
# then runs the shared deploy/sql/01-bootstrap.sql (schemas, roles, users). Idempotent: safe on every start of the environment.
# On shared environments the DBA creates the logins; this file is for local development only.
set -euo pipefail

sqlcmd() {
  /opt/mssql-tools18/bin/sqlcmd -C -b -S mssql -U sa -P "$DB_SA_PASSWORD" "$@"
}

sqlcmd -Q "IF DB_ID(N'SuperApp') IS NULL CREATE DATABASE [SuperApp];"

# After a container restart the server accepts sa logins before it has started the user databases. Until SuperApp is started,
# DATABASEPROPERTYEX may already report Status = ONLINE, but Collation is NULL; connecting to SuperApp in that window fails with
# "Login failed for user 'sa'" (error 18456, state 38) and would stop the whole environment. Wait for both, checked from master
# so that waiting itself leaves no failed logins in the server log.
ready=""
for attempt in $(seq 1 90); do
  state=$(sqlcmd -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CONVERT(nvarchar(60), DATABASEPROPERTYEX(N'SuperApp', 'Status')), '|', IIF(DATABASEPROPERTYEX(N'SuperApp', 'Collation') IS NULL, 'not-started', 'started'));" | tr -d '[:space:]')
  if [ "$state" = "ONLINE|started" ]; then
    ready=1
    break
  fi
  sleep 2
done

if [ -z "$ready" ]; then
  echo "Database SuperApp is not online after 180 s (last state: ${state:-unknown})." >&2
  exit 1
fi

for login in superapp_migrator gateway_app knowledge_app sleepdiary_app; do
  sqlcmd -Q "IF SUSER_ID(N'${login}') IS NULL CREATE LOGIN [${login}] WITH PASSWORD = N'${DB_APP_PASSWORD}', CHECK_POLICY = OFF;"
done

sqlcmd -d SuperApp -i /sql/01-bootstrap.sql -v IncludeMigrator=1
echo "Database SuperApp bootstrapped."
