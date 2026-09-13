using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CDRSFunctions.ReportStats;

public class UploadReportAttachment
{
    private readonly ILogger<UploadReportAttachment> _logger;
    private const string ContainerName = "report-attachments";
    private const string StorageAccountUrl = "https://stcdrsattachments.blob.core.windows.net";

    public UploadReportAttachment(ILogger<UploadReportAttachment> logger)
    {
        _logger = logger;
    }

    [Function("UploadReportAttachment")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        _logger.LogInformation("UploadReportAttachment processing a request.");

        try
        {
            // Managed Identity — no connection string, no account key.
            var blobServiceClient = new BlobServiceClient(
                new Uri(StorageAccountUrl),
                new DefaultAzureCredential());

            var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);

            var fileName = $"{Guid.NewGuid()}.bin";
            var blobClient = containerClient.GetBlobClient(fileName);

            // Request body is treated as the raw file content.
            await blobClient.UploadAsync(req.Body, overwrite: true);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                BlobUrl = blobClient.Uri.ToString(),
                FileName = fileName
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload attachment.");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Unable to upload attachment." });
            return errorResponse;
        }
    }
}