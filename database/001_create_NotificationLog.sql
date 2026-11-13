-- Baseline: NotificationLog was originally created by hand in the Azure Portal
-- with no script under version control. This reproduces its structure as of
-- 2026-11-13 so the table can be recreated in a new environment.
--
-- Note: the original constraints were unnamed, so the live database has a
-- system-generated PK name (PK__Notifica__3214EC073AF9215D). Running this
-- script elsewhere produces explicitly named constraints instead.

IF OBJECT_ID(N'dbo.NotificationLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationLog
    (
        Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_NotificationLog_Id             DEFAULT NEWID(),
        ReportId       UNIQUEIDENTIFIER NOT NULL,
        ProjectId      NVARCHAR(50)     NOT NULL,
        EventType      NVARCHAR(50)     NOT NULL,
        TriggeredAtUtc DATETIME2        NOT NULL,
        ProcessedAtUtc DATETIME2        NOT NULL CONSTRAINT DF_NotificationLog_ProcessedAtUtc DEFAULT GETUTCDATE(),
        Status         NVARCHAR(20)     NOT NULL CONSTRAINT DF_NotificationLog_Status         DEFAULT 'Processed',
        CONSTRAINT PK_NotificationLog PRIMARY KEY CLUSTERED (Id)
    );
END
