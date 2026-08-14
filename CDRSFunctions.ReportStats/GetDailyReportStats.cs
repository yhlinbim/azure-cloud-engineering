using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CDRSFunctions.ReportStats
{
    public class GetDailyReportStats
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public GetDailyReportStats(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<GetDailyReportStats>();
            _configuration = configuration;
        }

        [Function("GetDailyReportStats")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            _logger.LogInformation("GetDailyReportStats function processing a request.");

            try
            {
                var connectionString = _configuration.GetConnectionString("SqlConnectionString");

                using var connection = new SqlConnection(connectionString);

                // Aggregated at the database level rather than pulling all rows
                // back and counting in memory ¡X keeps this read-only endpoint
                // fast even as DailyReports grows.
                const string sql = @"
                    SELECT
                        COUNT(CASE WHEN CAST(CreatedAtUtc AS DATE) = CAST(GETUTCDATE() AS DATE) THEN 1 END) AS NewToday,
                        COUNT(CASE WHEN Status IN ('Submitted', 'UnderReview') THEN 1 END) AS PendingReview
                    FROM DailyReports";

                var stats = await connection.QuerySingleAsync<ReportStats>(sql);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(stats));
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query report statistics.");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync(
                    JsonSerializer.Serialize(new { error = "Unable to retrieve report statistics." }));
                return errorResponse;
            }
        }
    }

    public record ReportStats(int NewToday, int PendingReview);
}