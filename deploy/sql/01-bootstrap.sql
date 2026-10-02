/*
  Bootstrap bazy aplikacji (ADR-0021): schematy, role i użytkownicy. Uruchamia DBA przed pierwszą migracją.
  Skrypt jest idempotentny. Loginy serwerowe (lub użytkownicy z Vault) tworzy DBA; skrypt wiąże je z bazą.

  Dodanie serwisu = jeden wiersz w @Services.
  Tryb sqlcmd: sqlcmd -S <serwer> -d <baza> -i 01-bootstrap.sql -v IncludeMigrator=1   (dev/test)
                                                                -v IncludeMigrator=0   (prod: migracje wykonuje DBA)
*/
SET NOCOUNT ON;

DECLARE @Services TABLE ([Schema] sysname PRIMARY KEY);
INSERT INTO @Services ([Schema]) VALUES
    (N'gateway'),
    (N'knowledge'),
    (N'sleepdiary');

DECLARE @schema sysname, @sql nvarchar(max);
DECLARE services CURSOR LOCAL FAST_FORWARD FOR SELECT [Schema] FROM @Services;
OPEN services;
FETCH NEXT FROM services INTO @schema;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @role sysname = @schema + N'_role';
    DECLARE @user sysname = @schema + N'_app';

    IF SCHEMA_ID(@schema) IS NULL
    BEGIN
        SET @sql = N'CREATE SCHEMA ' + QUOTENAME(@schema) + N' AUTHORIZATION dbo;';
        EXEC (@sql);
    END

    IF DATABASE_PRINCIPAL_ID(@role) IS NULL
    BEGIN
        SET @sql = N'CREATE ROLE ' + QUOTENAME(@role) + N';';
        EXEC (@sql);
    END

    -- DML wyłącznie na własnym schemacie; brak dostępu do innych schematów (ADR-0021).
    SET @sql = N'GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::' + QUOTENAME(@schema) + N' TO ' + QUOTENAME(@role) + N';';
    EXEC (@sql);

    IF DATABASE_PRINCIPAL_ID(@user) IS NULL AND SUSER_ID(@user) IS NOT NULL
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@user) + N' FOR LOGIN ' + QUOTENAME(@user) + N' WITH DEFAULT_SCHEMA = ' + QUOTENAME(@schema) + N';';
        EXEC (@sql);
    END

    IF DATABASE_PRINCIPAL_ID(@user) IS NOT NULL
    BEGIN
        SET @sql = N'ALTER ROLE ' + QUOTENAME(@role) + N' ADD MEMBER ' + QUOTENAME(@user) + N';';
        EXEC (@sql);
    END
    ELSE
        PRINT N'Brak loginu ' + @user + N': utwórz login i uruchom skrypt ponownie.';

    FETCH NEXT FROM services INTO @schema;
END
CLOSE services;
DEALLOCATE services;

-- Użytkownik Migratora: DDL i migracje danych we wszystkich schematach; tylko dev/test (ADR-0004).
IF '$(IncludeMigrator)' = '1'
BEGIN
    IF DATABASE_PRINCIPAL_ID(N'superapp_migrator') IS NULL AND SUSER_ID(N'superapp_migrator') IS NOT NULL
        CREATE USER [superapp_migrator] FOR LOGIN [superapp_migrator];

    IF DATABASE_PRINCIPAL_ID(N'superapp_migrator') IS NOT NULL
    BEGIN
        ALTER ROLE [db_ddladmin] ADD MEMBER [superapp_migrator];
        ALTER ROLE [db_datareader] ADD MEMBER [superapp_migrator];
        ALTER ROLE [db_datawriter] ADD MEMBER [superapp_migrator];
    END
END
