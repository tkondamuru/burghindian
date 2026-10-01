using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace WebsiteApi.Functions;

/// <summary>
/// Agent image upload endpoint. Accepts a photo as multipart/form-data, stores it
/// in blob storage server-side, and returns a public URL for use as ImageUrl on
/// events/businesses created through the ingest API.
/// Authenticated by the same X-Ingest-Key shared secret as IngestFunctions
/// (fail closed when INGEST_API_KEY is not configured).
/// </summary>
public sealed class IngestImageFunctions
{
    private const string ContainerName = "listing-photos";
    private const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    private readonly ILogger<IngestImageFunctions> _logger;

    public IngestImageFunctions(ILogger<IngestImageFunctions> logger)
    {
        _logger = logger;
    }

    [Function("IngestUploadImage")]
    public async Task<IActionResult> IngestUploadImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingest/images")] HttpRequest req)
    {
        try
        {
            var authError = IngestFunctions.CheckAuth(req);
            if (authError is not null) return authError;

            if (!req.HasFormContentType)
            {
                return new BadRequestObjectResult(new { error = "Expected multipart/form-data with a 'file' field." });
            }

            var form = await req.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return new BadRequestObjectResult(new { error = "No file was uploaded." });
            }
            if (file.Length > MaxFileBytes)
            {
                return new BadRequestObjectResult(new { error = "File is too large. Maximum size is 10 MB." });
            }

            var contentType = (file.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            if (!ImageExtensions.TryGetValue(contentType, out var extension))
            {
                return new BadRequestObjectResult(new { error = "Only JPEG, PNG, WebP, and GIF images are accepted." });
            }

            var kind = form["kind"].ToString().Trim().ToLowerInvariant();
            if (kind != "event" && kind != "business") kind = "listing";
            var blobName = $"{kind}-{Guid.NewGuid():N}{extension}";

            var blobServiceClient = CreateBlobServiceClient();
            var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(blobName);
            await using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

            var imageUrl = blobClient.Uri.ToString();
            _logger.LogInformation("Ingest image uploaded: {BlobName} ({Bytes} bytes).", blobName, file.Length);
            return new OkObjectResult(new { success = true, imageUrl });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Ingest image upload failed: storage is not configured.");
            return new ObjectResult(new { error = "Image storage is not configured." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest image upload failed.");
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    private static BlobServiceClient CreateBlobServiceClient()
    {
        var connectionString = Environment.GetEnvironmentVariable("TABLE_STORAGE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new BlobServiceClient(connectionString);
        }

        var accountName = Environment.GetEnvironmentVariable("TABLE_STORAGE_ACCOUNT_NAME");
        var accountKey = Environment.GetEnvironmentVariable("TABLE_STORAGE_ACCOUNT_KEY");
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
        {
            throw new InvalidOperationException("Storage configuration is missing.");
        }

        return new BlobServiceClient(
            new Uri($"https://{accountName}.blob.core.windows.net"),
            new StorageSharedKeyCredential(accountName, accountKey));
    }
}
