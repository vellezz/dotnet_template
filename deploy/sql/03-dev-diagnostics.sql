/*
  Narzędzia diagnostyczne dla lokalnego środowiska deweloperskiego (ADR-0021, ADR-0034, ADR-0046).
  Tworzy procedury składowane używane przez CLI (dotnet superapp inbox|outbox).
  Tylko środowiska lokalne / dev / test.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Podsumowanie outbox/inbox dla wszystkich schematów serwisów
CREATE OR ALTER PROCEDURE dbo.sp_SuperApp_OutboxSummary
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX) = N'';
    SELECT @sql = @sql + IIF(@sql = N'', N'', N' UNION ALL ') +
        N'SELECT ''' + s.name + N''' AS Service, ' +
        N'CAST((SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.OutboxMessage) AS BIGINT) AS OutboxPending, ' +
        N'CAST((SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.InboxState) AS BIGINT) AS InboxProcessed, ' +
        N'CAST((SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.OutboxState WHERE LockId IS NOT NULL) AS BIGINT) AS ActiveLocks '
    FROM sys.schemas s
    JOIN sys.tables t ON s.schema_id = t.schema_id
    WHERE t.name = 'OutboxMessage';

    IF @sql <> N''
        EXEC sp_executesql @sql;
END;
GO

-- 2. Podsumowanie stanu MassTransit InboxState dla wszystkich schematów
CREATE OR ALTER PROCEDURE dbo.sp_SuperApp_InboxSummary
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX) = N'';
    SELECT @sql = @sql + IIF(@sql = N'', N'', N' UNION ALL ') +
        N'SELECT ''' + s.name + N''' AS Service, ' +
        N'COUNT_BIG(*) AS Total, ' +
        N'CAST(COALESCE(SUM(CASE WHEN ReceiveCount > 1 THEN 1 ELSE 0 END), 0) AS BIGINT) AS Retried, ' +
        N'CAST(COALESCE(SUM(CASE WHEN LockId != ''00000000-0000-0000-0000-000000000000'' THEN 1 ELSE 0 END), 0) AS BIGINT) AS Locked ' +
        N'FROM ' + QUOTENAME(s.name) + N'.InboxState '
    FROM sys.schemas s
    JOIN sys.tables t ON s.schema_id = t.schema_id
    WHERE t.name = 'InboxState';

    IF @sql <> N''
        EXEC sp_executesql @sql;
END;
GO

-- 3. Pobieranie ostatnich wiadomości InboxState dla wybranego serwisu
CREATE OR ALTER PROCEDURE dbo.sp_SuperApp_InboxMessages
    @Schema sysname,
    @Limit INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM sys.schemas s JOIN sys.tables t ON s.schema_id = t.schema_id WHERE s.name = @Schema AND t.name = 'InboxState')
    BEGIN
        DECLARE @sql NVARCHAR(MAX) = N'
            SELECT TOP (@pLimit)
                   MessageId,
                   ConsumerId,
                   Received,
                   ReceiveCount,
                   Consumed
            FROM ' + QUOTENAME(@Schema) + N'.InboxState
            ORDER BY Id DESC;';

        EXEC sp_executesql @sql, N'@pLimit INT', @pLimit = @Limit;
    END
END;
GO

-- 4. Czyszczenie stanu inboxa dla replaying wiadomości
CREATE OR ALTER PROCEDURE dbo.sp_SuperApp_CleanInbox
    @Schema sysname = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    DECLARE @sql NVARCHAR(MAX) = N'';
    SELECT @sql = @sql +
        N'DELETE FROM ' + QUOTENAME(s.name) + N'.OutboxMessage WHERE InboxMessageId IS NOT NULL; ' +
        N'DELETE FROM ' + QUOTENAME(s.name) + N'.InboxState; '
    FROM sys.schemas s
    JOIN sys.tables t ON s.schema_id = t.schema_id
    WHERE t.name = 'InboxState'
      AND (@Schema IS NULL OR s.name = @Schema);

    IF @sql <> N''
        EXEC sp_executesql @sql;
END;
GO
