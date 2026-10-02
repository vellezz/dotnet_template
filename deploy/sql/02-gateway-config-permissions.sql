/*
  Konfiguracja tras bramy tylko do odczytu dla użytkownika bramy (ADR-0022): zapis wyłącznie migracjami
  (superapp_migrator na dev/test, DBA na prod). Uruchamiany po migracjach schematu gateway; idempotentny.
*/
SET NOCOUNT ON;

DECLARE @table sysname, @sql nvarchar(max);
DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'gateway')
      AND name IN (N'Clusters', N'Destinations', N'Routes', N'RouteMethods', N'RouteHosts', N'RouteTransforms');
OPEN tables;
FETCH NEXT FROM tables INTO @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'DENY INSERT, UPDATE, DELETE ON [gateway].' + QUOTENAME(@table) + N' TO [gateway_role];';
    EXEC (@sql);
    FETCH NEXT FROM tables INTO @table;
END
CLOSE tables;
DEALLOCATE tables;
