using System.Net;
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

public sealed class EventFunctions
{
    private readonly TableStorageService _tableStorageService;
    private readonly EditCodeService _editCodeService;
    private readonly ILogger<EventFunctions> _logger;

    public EventFunctions(TableStorageService tableStorageService, EditCodeService editCodeService, ILogger<EventFunctions> logger)
    {
        _tableStorageService = tableStorageService;
        _editCodeService = editCodeService;
        _logger = logger;
    }

    [Function("GetOrCreateEvents")]
    public async Task<IActionResult> GetOrCreateEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "events")] HttpRequest req)
    {
        try
        {
            return HttpMethods.IsGet(req.Method)
                ? await ListApprovedEventsAsync()
                : await CreateEventAsync(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Events endpoint failed for method {Method} at path {Path}.", req.Method, req.Path.Value);
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("UpdateEvent")]
    public async Task<IActionResult> UpdateEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete", Route = "events/{partitionKey}/{rowKey}")] HttpRequest req,
        string partitionKey,
        string rowKey)
    {
        try
        {
            if (HttpMethods.IsDelete(req.Method))
            {
                return await DeleteEventAsync(req, partitionKey, rowKey);
            }

            var email = AuthHelpers.GetAuthenticatedEmail(req);
            var isAdmin = AuthHelpers.IsAdminEmail(email);
            if (string.IsNullOrWhiteSpace(email))
            {
                return new UnauthorizedObjectResult(new { error = "Login is required." });
            }

            var body = await JsonSerializer.DeserializeAsync<UpdateEventRequest>(req.Body, JsonOptions.Default);
            if (body is null)
            {
                return new BadRequestObjectResult(new { error = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(body.EditCode))
            {
                return new BadRequestObjectResult(new { error = "Edit code is required." });
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

            var tagValidation = ValidationHelpers.ValidateTags(body.Tags, TagCatalog.EventTags);
            if (!tagValidation.Ok)
            {
                return new BadRequestObjectResult(new { error = tagValidation.Error, allowedTags = TagCatalog.EventTags });
            }

            var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
            var lookup = await lookupTable.GetEntityAsync<TableEntity>(EditCodeService.LookupPartitionKey, body.EditCode.Trim().ToUpperInvariant());

            if (!string.Equals(lookup.Value.GetString("EntityType"), "Event", StringComparison.Ordinal) ||
                !string.Equals(lookup.Value.GetString("TargetPartitionKey"), partitionKey, StringComparison.Ordinal) ||
                !string.Equals(lookup.Value.GetString("TargetRowKey"), rowKey, StringComparison.Ordinal))
            {
                return new ObjectResult(new { error = "Edit code does not match this event." }) { StatusCode = StatusCodes.Status403Forbidden };
            }

            if (!isAdmin && !string.Equals(lookup.Value.GetString("SubmitterEmail"), email, StringComparison.OrdinalIgnoreCase))
            {
                return new ObjectResult(new { error = "This event does not belong to the signed-in Gmail account." }) { StatusCode = StatusCodes.Status403Forbidden };
            }

            var eventTable = _tableStorageService.GetTableClient("Events");
            var entity = await eventTable.GetEntityAsync<TableEntity>(partitionKey, rowKey);

            entity.Value["Title"] = body.Title!.Trim();
            entity.Value["Date"] = body.Date!.Trim();
            entity.Value["Time"] = (body.Time ?? string.Empty).Trim();
            entity.Value["Location"] = body.Location!.Trim();
            entity.Value["Summary"] = body.Summary!.Trim();
            entity.Value["Description"] = body.Description!.Trim();
            entity.Value["Tags"] = tagValidation.TagsString!;
            entity.Value["ImageUrl"] = (body.ImageUrl ?? string.Empty).Trim();
            entity.Value["UpdatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

            await eventTable.UpdateEntityAsync(entity.Value, ETag.All, TableUpdateMode.Replace);
            return new OkObjectResult(new { success = true });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Event update target not found for partition {PartitionKey}, row {RowKey}.", partitionKey, rowKey);
            return new NotFoundObjectResult(new { error = "Event or edit code not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event update failed for partition {PartitionKey}, row {RowKey}, signed-in user {Email}.", partitionKey, rowKey, AuthHelpers.GetAuthenticatedEmail(req));
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    private async Task<IActionResult> ListApprovedEventsAsync()
    {
        var table = _tableStorageService.GetTableClient("Events");
        var rows = new List<EventResponse>();

        try
        {
            await foreach (var entity in table.QueryAsync<TableEntity>("IsApproved eq true"))
            {
                rows.Add(new EventResponse
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey,
                    CreatedAtUtc = entity.GetString("CreatedAtUtc"),
                    UpdatedAtUtc = entity.GetString("UpdatedAtUtc"),
                    Title = entity.GetString("Title"),
                    Date = entity.GetString("Date"),
                    Time = entity.GetString("Time"),
                    Location = entity.GetString("Location"),
                    Summary = entity.GetString("Summary"),
                    Description = entity.GetString("Description"),
                    Tags = entity.GetString("Tags"),
                    ImageUrl = entity.GetString("ImageUrl")
                });
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        return new OkObjectResult(rows);
    }

    private async Task<IActionResult> CreateEventAsync(HttpRequest req)
    {
        var email = AuthHelpers.GetAuthenticatedEmail(req);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new UnauthorizedObjectResult(new { error = "Login is required." });
        }

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

        var tagValidation = ValidationHelpers.ValidateTags(body.Tags, TagCatalog.EventTags);
        if (!tagValidation.Ok)
        {
            return new BadRequestObjectResult(new { error = tagValidation.Error, allowedTags = TagCatalog.EventTags });
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
            ["Tags"] = tagValidation.TagsString!,
            ["ImageUrl"] = (body.ImageUrl ?? string.Empty).Trim(),
            ["EditCode"] = editCode,
            ["SubmitterEmail"] = email,
            ["IsApproved"] = true,
            ["CreatedAtUtc"] = createdAt.ToString("O"),
            ["UpdatedAtUtc"] = createdAt.ToString("O"),
            ["Source"] = "website-form"
        });

        await lookupTable.AddEntityAsync(new TableEntity(EditCodeService.LookupPartitionKey, editCode)
        {
            ["EntityType"] = "Event",
            ["TargetTable"] = "Events",
            ["TargetPartitionKey"] = partitionKey,
            ["TargetRowKey"] = rowKey,
            ["SubmitterEmail"] = email,
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

    private async Task<IActionResult> DeleteEventAsync(HttpRequest req, string partitionKey, string rowKey)
    {
        var email = AuthHelpers.GetAuthenticatedEmail(req);
        var isAdmin = AuthHelpers.IsAdminEmail(email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new UnauthorizedObjectResult(new { error = "Login is required." });
        }

        var body = await JsonSerializer.DeserializeAsync<DeletePostRequest>(req.Body, JsonOptions.Default);
        var editCode = body?.EditCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(editCode))
        {
            return new BadRequestObjectResult(new { error = "Edit code is required." });
        }

        var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
        var lookup = await lookupTable.GetEntityAsync<TableEntity>(EditCodeService.LookupPartitionKey, editCode);

        if (!string.Equals(lookup.Value.GetString("EntityType"), "Event", StringComparison.Ordinal) ||
            !string.Equals(lookup.Value.GetString("TargetPartitionKey"), partitionKey, StringComparison.Ordinal) ||
            !string.Equals(lookup.Value.GetString("TargetRowKey"), rowKey, StringComparison.Ordinal))
        {
            return new ObjectResult(new { error = "Edit code does not match this event." }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        if (!isAdmin && !string.Equals(lookup.Value.GetString("SubmitterEmail"), email, StringComparison.OrdinalIgnoreCase))
        {
            return new ObjectResult(new { error = "This event does not belong to the signed-in Gmail account." }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        var eventTable = _tableStorageService.GetTableClient("Events");
        await eventTable.DeleteEntityAsync(partitionKey, rowKey);
        await lookupTable.DeleteEntityAsync(EditCodeService.LookupPartitionKey, editCode);

        return new OkObjectResult(new { success = true });
    }
}
