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
            var payload = JsonSerializer.Deserialize<ReportApprovedEvent>(message.Body.ToString());

            if (payload is null)
            {
                _logger.LogWarning(
                    "Message {MessageId} could not be deserialized. Dead-lettering.",
                    message.MessageId);
                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "InvalidPayload",
                    deadLetterErrorDescription: "Message body could not be deserialized into ReportApprovedEvent");
                return;
            }

            var connectionString = _configuration.GetConnectionString("SqlConnectionString");
            using var connection = new SqlConnection(connectionString);

            // Idempotency check ¡X Service Bus guarantees at-least-once delivery,
            // not exactly-once. Retries (e.g. after a transient DB timeout) can
            // redeliver a message that already succeeded. Without this check,
            // every redelivery writes a duplicate row.
            const string checkSql = @"
                SELECT COUNT(1) FROM NotificationLog
                WHERE ReportId = @ReportId
                  AND EventType = 'ReportApproved'
                  AND TriggeredAtUtc = @TriggeredAtUtc";

            var alreadyProcessed = await connection.ExecuteScalarAsync<int>(checkSql, new
            {
                payload.ReportId,
                payload.TriggeredAtUtc
            }) > 0;

            if (alreadyProcessed)
            {
                _logger.LogInformation(
                    "Message {MessageId} for ReportId={ReportId} already processed. Skipping duplicate, completing message.",
                    message.MessageId, payload.ReportId);
                await messageActions.CompleteMessageAsync(message);
                return;
            }

            const string insertSql = @"
                INSERT INTO NotificationLog
                    (Id, ReportId, ProjectId, EventType, TriggeredAtUtc, ProcessedAtUtc, Status)
                VALUES
                    (NEWID(), @ReportId, @ProjectId, 'ReportApproved', @TriggeredAtUtc, GETUTCDATE(), 'Processed')";

            await connection.ExecuteAsync(insertSql, new
            {
                payload.ReportId,
                payload.ProjectId,
                payload.TriggeredAtUtc
            });

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