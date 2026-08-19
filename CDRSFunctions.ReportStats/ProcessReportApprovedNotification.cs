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
            "Processing report-approved message. MessageId={MessageId}",
            message.MessageId);

        try
        {
            var payload = JsonSerializer.Deserialize<ReportApprovedEvent>(message.Body.ToString());

            if (payload is null)
            {
                _logger.LogWarning(
                    "Message {MessageId} could not be deserialized. Dead-lettering.",
                    message.MessageId);
                await messageActions.DeadLetterMessageAsync(message, "InvalidPayload");
                return;
            }

            var connectionString = _configuration.GetConnectionString("SqlConnectionString");
            using var connection = new SqlConnection(connectionString);

            const string sql = @"
                INSERT INTO NotificationLog
                    (Id, ReportId, ProjectId, EventType, TriggeredAtUtc, ProcessedAtUtc, Status)
                VALUES
                    (NEWID(), @ReportId, @ProjectId, 'ReportApproved', @TriggeredAtUtc, GETUTCDATE(), 'Processed')";

            await connection.ExecuteAsync(sql, new
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

            // Don't complete ¡X let Service Bus's built-in retry/dead-letter
            // handle transient failures automatically.
            throw;
        }
    }
}

public record ReportApprovedEvent(Guid ReportId, string ProjectId, DateTime TriggeredAtUtc);