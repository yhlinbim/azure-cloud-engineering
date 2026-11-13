-- Enforce idempotency at the database level. The application previously did
-- check-then-insert, which races when the same message is delivered twice
-- concurrently. With this constraint, the second INSERT fails with error 2627
-- and the Function treats it as already processed.
--
-- Pre-check (2026-11-13): no existing duplicates on these three columns.

IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE name = N'UQ_NotificationLog_ReportId_EventType_TriggeredAtUtc'
      AND parent_object_id = OBJECT_ID(N'dbo.NotificationLog')
)
BEGIN
    ALTER TABLE dbo.NotificationLog
        ADD CONSTRAINT UQ_NotificationLog_ReportId_EventType_TriggeredAtUtc
        UNIQUE (ReportId, EventType, TriggeredAtUtc);
END
