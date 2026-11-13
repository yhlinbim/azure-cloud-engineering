using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CDRSFunctions.ReportStats;

public class ProcessReportApprovedNotification
{
    // SQL Server error numbers for duplicate-key violations.
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    private readonly ILogger<ProcessReportApprovedNotification> _logger;
    private readonly IConfiguration _configuration;

    public ProcessReportApprovedNotification(
        ILogger<ProcessReportApprovedNotification> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function(nameof(ProcessReportApprovedNotification))]
    public async Task Run(
        [ServiceBusTrigger("report-approved", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        _logger.LogInformation(
            "Processing report-approved message. MessageId={MessageId}, DeliveryCount={DeliveryCount}",
            message.MessageId, message.DeliveryCount);

        try
        {
            // Malformed payloads are permanent failures — retrying can never succeed,
            // so dead-letter immediately instead of burning through MaxDeliveryCount.
            ReportApprovedEvent? payload = null;
            string? invalidReason = null;
            try
            {
                payload = JsonSerializer.Deserialize<ReportApprovedEvent>(message.Body.ToString());
            }
            catch (JsonException ex)
            {
                invalidReason = $"Invalid JSON: {ex.Message}";
            }

            // Missing properties deserialize to defaults (Guid.Empty / null) rather than
            // throwing, so they must be checked explicitly.
            if (invalidReason is null &&
                (payload is null || payload.ReportId == Guid.Empty || string.IsNullOrWhiteSpace(payload.ProjectId)))
            {
                invalidReason = "Message body is missing ReportId or ProjectId";
            }

            if (payload is null || invalidReason is not null)
            {
                _logger.LogWarning(
                    "Message {MessageId} has an invalid payload: {Reason}. Dead-lettering.",
                    message.MessageId, invalidReason);
                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "InvalidPayload",
                    deadLetterErrorDescription: invalidReason);
                return;
            }

            var connectionString = _configuration.GetConnectionString("SqlConnectionString");
            using var connection = new SqlConnection(connectionString);

            // Idempotency — Service Bus guarantees at-least-once delivery, not exactly-once,
            // so the same message can arrive more than once, even concurrently. A
            // check-then-insert races in that case, so the UNIQUE constraint on
            // (ReportId, EventType, TriggeredAtUtc) is the source of truth instead:
            // a duplicate INSERT fails and is treated as already processed.
            // See database/002_add_unique_NotificationLog.sql.
            const string insertSql = @"
                INSERT INTO NotificationLog
                    (Id, ReportId, ProjectId, EventType, TriggeredAtUtc, ProcessedAtUtc, Status)
                VALUES
                    (NEWID(), @ReportId, @ProjectId, 'ReportApproved', @TriggeredAtUtc, GETUTCDATE(), 'Processed')";

            try
            {
                await connection.ExecuteAsync(insertSql, new
                {
                    payload.ReportId,
                    payload.ProjectId,
                    payload.TriggeredAtUtc
                });
            }
            catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or UniqueIndexViolation)
            {
                _logger.LogInformation(
                    "Message {MessageId} for ReportId={ReportId} already processed. Skipping duplicate, completing message.",
                    message.MessageId, payload.ReportId);
                await messageActions.CompleteMessageAsync(message);
                return;
            }

            await messageActions.CompleteMessageAsync(message);

            _logger.LogInformation(
                "Notification logged for ReportId={ReportId}", payload.ReportId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process report-approved message {MessageId}.", message.MessageId);
            throw;
        }
    }
}

public record ReportApprovedEvent(Guid ReportId, string ProjectId, DateTime TriggeredAtUtc);