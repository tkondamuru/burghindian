using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WebsiteApi.Models;
using WebsiteApi.Services;

namespace WebsiteApi.Functions;

/// <summary>
/// Agent ingest endpoint. Lets a trusted automation client (e.g. Marley) create,
/// update, and delete Events/Businesses rows without the Gmail-auth web flow.
/// Authenticated by a shared secret sent in the X-Ingest-Key header; the expected
/// value comes from the INGEST_API_KEY app setting. Fail closed: when the app
/// setting is missing or empty, every request is rejected.
/// </summary>
public sealed class IngestFunctions
{
    public const string IngestKeyHeader = "X-Ingest-Key";
    private const string IngestKeySetting = "INGEST_API_KEY";
    private const string Submitter = "agent-ingest";

    private readonly TableStorageService _tableStorageService;
    private readonly EditCodeService _editCodeService;
    private readonly ILogger<IngestFunctions> _logger;

    public IngestFunctions(TableStorageService tableStorageService, EditCodeService editCodeService, ILogger<IngestFunctions> logger)
    {
        _tableStorageService = tableStorageService;
        _editCodeService = editCodeService;
        _logger = logger;
    }

    [Function("IngestCreateEvent")]
    public async Task<IActionResult> IngestCreateEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingest/events")] HttpRequest req)
    {
        try
        {
            var authError = CheckAuth(req);
            if (authError is not null) return authError;

            var body = await JsonSerializer.DeserializeAsync<EventSubmissionRequest>(req.Body, JsonOptions.Default);
            if (body is null)
            {
                return new BadRequestObjectResult(new { error = "Request body is required." });
            }

            var requiredError = ValidationHelpers.RequireFields(new Dictionary<string, string?>
            {
                ["Title"] = body.Title,
                ["Date"] = body.Date,
                ["Location"] = body.Location,
                ["Summary"] = body.Summary,
                ["Description"] = body.Description
            });
            if (requiredError is not null)
            {
                return new BadRequestObjectResult(new { error = requiredError });
            }

            var eventsTable = _tableStorageService.GetTableClient("Events");
            var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
            await Task.WhenAll(
                _tableStorageService.EnsureTableExistsAsync(eventsTable),
                _tableStorageService.EnsureTableExistsAsync(lookupTable));

            var createdAt = DateTimeOffset.UtcNow;
            var partitionKey = TableStorageService.GetMonthBucket(createdAt);
            var rowKey = TableStorageService.CreateContentRowKey(createdAt);
            var editCode = await _editCodeService.CreateUniqueEditCodeAsync(lookupTable);

            await eventsTable.AddEntityAsync(new TableEntity(partitionKey, rowKey)
            {
                ["Title"] = body.Title!.Trim(),
                ["Date"] = body.Date!.Trim(),
                ["Time"] = (body.Time ?? string.Empty).Trim(),
                ["Location"] = body.Location!.Trim(),
                ["Summary"] = body.Summary!.Trim(),
                ["Description"] = body.Description!.Trim(),
                ["Tags"] = ValidationHelpers.NormalizeTags(body.Tags),
                ["ImageUrl"] = (body.ImageUrl ?? string.Empty).Trim(),
                ["EditCode"] = editCode,
                ["SubmitterEmail"] = Submitter,
                ["IsApproved"] = true,
                ["CreatedAtUtc"] = createdAt.ToString("O"),
                ["UpdatedAtUtc"] = createdAt.ToString("O"),
                ["Source"] = Submitter
            });

            await lookupTable.AddEntityAsync(new TableEntity(EditCodeService.LookupPartitionKey, editCode)
            {
                ["EntityType"] = "Event",
                ["TargetTable"] = "Events",
                ["TargetPartitionKey"] = partitionKey,
                ["TargetRowKey"] = rowKey,
                ["SubmitterEmail"] = Submitter,
                ["IsApproved"] = true,
                ["CreatedAtUtc"] = createdAt.ToString("O")
            });

            return new OkObjectResult(new CreatePostResponse
            {
                Success = true,
                EditCode = editCode,
                Id = new { partitionKey, rowKey }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest event creation failed.");
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("IngestUpdateEvent")]
    public async Task<IActionResult> IngestUpdateEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ingest/events/{partitionKey}/{rowKey}")] HttpRequest req,
        string partitionKey,
        string rowKey)
    {
        try
        {
            var authError = CheckAuth(req);
            if (authError is not null) return authError;

            var body = await JsonSerializer.DeserializeAsync<EventSubmissionRequest>(req.Body, JsonOptions.Default);
            if (body is null)
            {
                return new BadRequestObjectResult(new { error = "Request body is required." });
            }

            // Partial update: only provided fields are validated and overwritten.
            var providedRequiredError = RequireProvidedFields(new Dictionary<string, string?>
            {
                ["Title"] = body.Title,
                ["Date"] = body.Date,
                ["Location"] = body.Location,
                ["Summary"] = body.Summary,
                ["Description"] = body.Description
            });
            if (providedRequiredError is not null)
            {
                return new BadRequestObjectResult(new { error = providedRequiredError });
            }

            var eventTable = _tableStorageService.GetTableClient("Events");
            var entity = await eventTable.GetEntityAsync<TableEntity>(partitionKey, rowKey);

            if (body.Title is not null) entity.Value["Title"] = body.Title.Trim();
            if (body.Date is not null) entity.Value["Date"] = body.Date.Trim();
            if (body.Time is not null) entity.Value["Time"] = body.Time.Trim();
            if (body.Location is not null) entity.Value["Location"] = body.Location.Trim();
            if (body.Summary is not null) entity.Value["Summary"] = body.Summary.Trim();
            if (body.Description is not null) entity.Value["Description"] = body.Description.Trim();
            if (body.Tags is not null) entity.Value["Tags"] = ValidationHelpers.NormalizeTags(body.Tags);
            if (body.ImageUrl is not null) entity.Value["ImageUrl"] = body.ImageUrl.Trim();
            entity.Value["UpdatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

            await eventTable.UpdateEntityAsync(entity.Value, ETag.All, TableUpdateMode.Replace);
            return new OkObjectResult(new { success = true });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new NotFoundObjectResult(new { error = "Event not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest event update failed for partition {PartitionKey}, row {RowKey}.", partitionKey, rowKey);
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("IngestDeleteEvent")]
    public async Task<IActionResult> IngestDeleteEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ingest/events/{partitionKey}/{rowKey}")] HttpRequest req,
        string partitionKey,
        string rowKey)
    {
        try
        {
            var authError = CheckAuth(req);
            if (authError is not null) return authError;

            var confirmError = await CheckDeleteConfirmedAsync(req);
            if (confirmError is not null) return confirmError;

            await DeletePostAsync("Events", "Event", partitionKey, rowKey);
            return new OkObjectResult(new { success = true });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new NotFoundObjectResult(new { error = "Event not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest event delete failed for partition {PartitionKey}, row {RowKey}.", partitionKey, rowKey);
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("IngestCreateBusiness")]
    public async Task<IActionResult> IngestCreateBusiness(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingest/businesses")] HttpRequest req)
    {
        try
        {
            var authError = CheckAuth(req);
            if (authError is not null) return authError;

            var body = await JsonSerializer.DeserializeAsync<BusinessSubmissionRequest>(req.Body, JsonOptions.Default);
            if (body is null)
            {
                return new BadRequestObjectResult(new { error = "Request body is required." });
            }

            var requiredError = ValidationHelpers.RequireFields(new Dictionary<string, string?>
            {
                ["Name"] = body.Name,
                ["Address"] = body.Address,
                ["Category"] = body.Category,
                ["Summary"] = body.Summary,
                ["Description"] = body.Description
            });
            if (requiredError is not null)
            {
                return new BadRequestObjectResult(new { error = requiredError });
            }

            var categoryValidation = ValidationHelpers.ValidateCategory(body.Category, TagCatalog.BusinessCategories);
            if (!categoryValidation.Ok)
            {
                return new BadRequestObjectResult(new { error = categoryValidation.Error, allowedCategories = TagCatalog.BusinessCategories });
            }

            var businessTable = _tableStorageService.GetTableClient("Businesses");
            var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
            await Task.WhenAll(
                _tableStorageService.EnsureTableExistsAsync(businessTable),
                _tableStorageService.EnsureTableExistsAsync(lookupTable));

            var createdAt = DateTimeOffset.UtcNow;
            var partitionKey = TableStorageService.GetMonthBucket(createdAt);
            var rowKey = TableStorageService.CreateContentRowKey(createdAt);
            var editCode = await _editCodeService.CreateUniqueEditCodeAsync(lookupTable);

            await businessTable.AddEntityAsync(new TableEntity(partitionKey, rowKey)
            {
                ["Name"] = body.Name!.Trim(),
                ["Address"] = body.Address!.Trim(),
                ["Phone"] = (body.Phone ?? string.Empty).Trim(),
                ["Category"] = categoryValidation.Category!,
                ["Summary"] = body.Summary!.Trim(),
                ["Description"] = body.Description!.Trim(),
                ["Tags"] = ValidationHelpers.NormalizeTags(body.Tags),
                ["ImageUrl"] = (body.ImageUrl ?? string.Empty).Trim(),
                ["EditCode"] = editCode,
                ["SubmitterEmail"] = Submitter,
                ["IsApproved"] = true,
                ["CreatedAtUtc"] = createdAt.ToString("O"),
                ["UpdatedAtUtc"] = createdAt.ToString("O"),
                ["Source"] = Submitter
            });

            await lookupTable.AddEntityAsync(new TableEntity(EditCodeService.LookupPartitionKey, editCode)
            {
                ["EntityType"] = "Business",
                ["TargetTable"] = "Businesses",
                ["TargetPartitionKey"] = partitionKey,
                ["TargetRowKey"] = rowKey,
                ["SubmitterEmail"] = Submitter,
                ["IsApproved"] = true,
                ["CreatedAtUtc"] = createdAt.ToString("O")
            });

            return new OkObjectResult(new CreatePostResponse
            {
                Success = true,
                EditCode = editCode,
                Id = new { partitionKey, rowKey }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest business creation failed.");
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("IngestUpdateBusiness")]
    public async Task<IActionResult> IngestUpdateBusiness(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ingest/businesses/{partitionKey}/{rowKey}")] HttpRequest req,
        string partitionKey,
        string rowKey)
    {
        try
        {
            var authError = CheckAuth(req);
            if (authError is not null) return authError;

            var body = await JsonSerializer.DeserializeAsync<BusinessSubmissionRequest>(req.Body, JsonOptions.Default);
            if (body is null)
            {
                return new BadRequestObjectResult(new { error = "Request body is required." });
            }

            var providedRequiredError = RequireProvidedFields(new Dictionary<string, string?>
            {
                ["Name"] = body.Name,
                ["Address"] = body.Address,
                ["Category"] = body.Category,
                ["Summary"] = body.Summary,
                ["Description"] = body.Description
            });
            if (providedRequiredError is not null)
            {
                return new BadRequestObjectResult(new { error = providedRequiredError });
            }

            string? validatedCategory = null;
            if (body.Category is not null)
            {
                var categoryValidation = ValidationHelpers.ValidateCategory(body.Category, TagCatalog.BusinessCategories);
                if (!categoryValidation.Ok)
                {
                    return new BadRequestObjectResult(new { error = categoryValidation.Error, allowedCategories = TagCatalog.BusinessCategories });
                }
                validatedCategory = categoryValidation.Category;
            }

            var businessTable = _tableStorageService.GetTableClient("Businesses");
            var entity = await businessTable.GetEntityAsync<TableEntity>(partitionKey, rowKey);

            if (body.Name is not null) entity.Value["Name"] = body.Name.Trim();
            if (body.Address is not null) entity.Value["Address"] = body.Address.Trim();
            if (body.Phone is not null) entity.Value["Phone"] = body.Phone.Trim();
            if (validatedCategory is not null) entity.Value["Category"] = validatedCategory;
            if (body.Summary is not null) entity.Value["Summary"] = body.Summary.Trim();
            if (body.Description is not null) entity.Value["Description"] = body.Description.Trim();
            if (body.Tags is not null) entity.Value["Tags"] = ValidationHelpers.NormalizeTags(body.Tags);
            if (body.ImageUrl is not null) entity.Value["ImageUrl"] = body.ImageUrl.Trim();
            entity.Value["UpdatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

            await businessTable.UpdateEntityAsync(entity.Value, ETag.All, TableUpdateMode.Replace);
            return new OkObjectResult(new { success = true });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new NotFoundObjectResult(new { error = "Business not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest business update failed for partition {PartitionKey}, row {RowKey}.", partitionKey, rowKey);
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("IngestDeleteBusiness")]
    public async Task<IActionResult> IngestDeleteBusiness(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ingest/businesses/{partitionKey}/{rowKey}")] HttpRequest req,
        string partitionKey,
        string rowKey)
    {
        try
        {
            var authError = CheckAuth(req);
            if (authError is not null) return authError;

            var confirmError = await CheckDeleteConfirmedAsync(req);
            if (confirmError is not null) return confirmError;

            await DeletePostAsync("Businesses", "Business", partitionKey, rowKey);
            return new OkObjectResult(new { success = true });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new NotFoundObjectResult(new { error = "Business not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingest business delete failed for partition {PartitionKey}, row {RowKey}.", partitionKey, rowKey);
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    internal static IActionResult? CheckAuth(HttpRequest req)
    {
        var expected = Environment.GetEnvironmentVariable(IngestKeySetting);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return new ObjectResult(new { error = "Ingest endpoint is not configured." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }

        var provided = req.Headers[IngestKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(provided) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided.Trim()),
                Encoding.UTF8.GetBytes(expected.Trim())))
        {
            return new UnauthorizedObjectResult(new { error = "Invalid or missing ingest key." });
        }

        return null;
    }

    private static async Task<IActionResult?> CheckDeleteConfirmedAsync(HttpRequest req)
    {
        IngestDeleteRequest? body = null;
        try
        {
            body = await JsonSerializer.DeserializeAsync<IngestDeleteRequest>(req.Body, JsonOptions.Default);
        }
        catch (JsonException)
        {
        }

        if (body?.Confirm != true)
        {
            return new BadRequestObjectResult(new { error = "Delete requires { \"confirm\": true } in the request body." });
        }

        return null;
    }

    /// <summary>
    /// For partial updates: provided fields must not be blank; absent (null) fields are skipped.
    /// </summary>
    private static string? RequireProvidedFields(IDictionary<string, string?> fields)
    {
        foreach (var entry in fields)
        {
            if (entry.Value is not null && string.IsNullOrWhiteSpace(entry.Value))
            {
                return $"{entry.Key} must not be blank.";
            }
        }

        return null;
    }

    private async Task DeletePostAsync(string tableName, string entityType, string partitionKey, string rowKey)
    {
        var table = _tableStorageService.GetTableClient(tableName);
        var entity = await table.GetEntityAsync<TableEntity>(partitionKey, rowKey);
        var editCode = entity.Value.GetString("EditCode");

        await table.DeleteEntityAsync(partitionKey, rowKey);

        if (!string.IsNullOrWhiteSpace(editCode))
        {
            var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
            try
            {
                await lookupTable.DeleteEntityAsync(EditCodeService.LookupPartitionKey, editCode);
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
            }
        }

        _logger.LogInformation("Ingest deleted {EntityType} at {PartitionKey}/{RowKey}.", entityType, partitionKey, rowKey);
    }
}
