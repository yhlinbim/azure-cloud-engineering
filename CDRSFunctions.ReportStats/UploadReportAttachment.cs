using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CDRSFunctions.ReportStats;

public class UploadReportAttachment
{
    private readonly ILogger<UploadReportAttachment> _logger;
    private const string ContainerName = "report-attachments";
    private const string StorageAccountUrl = "https://stcdrsattachments.blob.core.windows.net";
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    // Content-Type and Content-Length are client-supplied and can't be trusted,
    // so the body itself is checked: its size while reading, and its leading
    // "magic bytes" against the signature of the declared type.
    private static readonly Dictionary<string, (string Extension, byte[] Signature)> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = (".pdf", "%PDF"u8.ToArray()),
            ["image/jpeg"] = (".jpg", [0xFF, 0xD8, 0xFF]),
            ["image/png"] = (".png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        };

    public UploadReportAttachment(ILogger<UploadReportAttachment> logger)
    {
        _logger = logger;
    }

    // AuthorizationLevel.Function — callers go through API Management, which
    // enforces the subscription key and rate limit, then forwards the Function
    // key via the x-functions-key header (stored as an APIM Named Value).
    // Without this, anyone with the Function URL could upload files directly.
    [Function("UploadReportAttachment")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("UploadReportAttachment processing a request.");

        // Strip parameters such as "; charset=..." before matching.
        var contentType = req.Headers.TryGetValues("Content-Type", out var values)
            ? values.First().Split(';')[0].Trim()
            : null;

        if (contentType is null || !AllowedTypes.TryGetValue(contentType, out var allowed))
        {
            return await ErrorAsync(req, HttpStatusCode.UnsupportedMediaType,
                "Only PDF, JPEG and PNG files are allowed.");
        }

        using var content = await ReadWithLimitAsync(req.Body, MaxUploadBytes);
        if (content is null)
        {
            return await ErrorAsync(req, HttpStatusCode.RequestEntityTooLarge,
                $"File exceeds the {MaxUploadBytes / 1024 / 1024} MB limit.");
        }

        if (!content.GetBuffer().AsSpan(0, (int)content.Length).StartsWith(allowed.Signature))
        {
            return await ErrorAsync(req, HttpStatusCode.BadRequest,
                "File content does not match its Content-Type.");
        }

        try
        {
            // Managed Identity — no connection string, no account key.
            var blobServiceClient = new BlobServiceClient(
                new Uri(StorageAccountUrl),
                new DefaultAzureCredential());

            var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);

            var fileName = $"{Guid.NewGuid()}{allowed.Extension}";
            var blobClient = containerClient.GetBlobClient(fileName);

            content.Position = 0;
            await blobClient.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });

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
            return await ErrorAsync(req, HttpStatusCode.InternalServerError,
                "Unable to upload attachment.");
        }
    }

    // Reads the body into memory, stopping as soon as it exceeds maxBytes.
    // Returns null if the limit is exceeded.
    private static async Task<MemoryStream?> ReadWithLimitAsync(Stream source, long maxBytes)
    {
        var destination = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk)) > 0)
        {
            if (destination.Length + read > maxBytes)
            {
                await destination.DisposeAsync();
                return null;
            }
            await destination.WriteAsync(chunk.AsMemory(0, read));
        }
        return destination;
    }

    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}