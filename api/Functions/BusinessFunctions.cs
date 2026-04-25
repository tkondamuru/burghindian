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

public sealed class BusinessFunctions
{
    private readonly TableStorageService _tableStorageService;
    private readonly EditCodeService _editCodeService;
    private readonly ILogger<BusinessFunctions> _logger;

    public BusinessFunctions(TableStorageService tableStorageService, EditCodeService editCodeService, ILogger<BusinessFunctions> logger)
    {
        _tableStorageService = tableStorageService;
        _editCodeService = editCodeService;
        _logger = logger;
    }

    [Function("GetOrCreateBusinesses")]
    public async Task<IActionResult> GetOrCreateBusinesses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "businesses")] HttpRequest req)
    {
        try
        {
            return HttpMethods.IsGet(req.Method)
                ? await ListApprovedBusinessesAsync()
                : await CreateBusinessAsync(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Businesses endpoint failed.");
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("UpdateBusiness")]
    public async Task<IActionResult> UpdateBusiness(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "businesses/{partitionKey}/{rowKey}")] HttpRequest req,
        string partitionKey,
        string rowKey)
    {
        try
        {
            var email = AuthHelpers.GetAuthenticatedEmail(req);
            if (string.IsNullOrWhiteSpace(email))
            {
                return new UnauthorizedObjectResult(new { error = "Login is required." });
            }

            var body = await JsonSerializer.DeserializeAsync<UpdateBusinessRequest>(req.Body, JsonOptions.Default);
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
                ["Name"] = body.Name,
                ["Address"] = body.Address,
                ["Summary"] = body.Summary,
                ["Description"] = body.Description
            });

            if (requiredError is not null)
            {
                return new BadRequestObjectResult(new { error = requiredError });
            }

            var tagValidation = ValidationHelpers.ValidateTags(body.Tags, TagCatalog.BusinessTags);
            if (!tagValidation.Ok)
            {
                return new BadRequestObjectResult(new { error = tagValidation.Error, allowedTags = TagCatalog.BusinessTags });
            }

            var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
            var lookup = await lookupTable.GetEntityAsync<TableEntity>(EditCodeService.LookupPartitionKey, body.EditCode.Trim().ToUpperInvariant());

            if (!string.Equals(lookup.Value.GetString("EntityType"), "Business", StringComparison.Ordinal) ||
                !string.Equals(lookup.Value.GetString("TargetPartitionKey"), partitionKey, StringComparison.Ordinal) ||
                !string.Equals(lookup.Value.GetString("TargetRowKey"), rowKey, StringComparison.Ordinal))
            {
                return new ObjectResult(new { error = "Edit code does not match this business." }) { StatusCode = StatusCodes.Status403Forbidden };
            }

            if (!string.Equals(lookup.Value.GetString("SubmitterEmail"), email, StringComparison.OrdinalIgnoreCase))
            {
                return new ObjectResult(new { error = "This business does not belong to the signed-in Gmail account." }) { StatusCode = StatusCodes.Status403Forbidden };
            }

            var businessTable = _tableStorageService.GetTableClient("Businesses");
            var entity = await businessTable.GetEntityAsync<TableEntity>(partitionKey, rowKey);

            entity.Value["Name"] = body.Name!.Trim();
            entity.Value["Address"] = body.Address!.Trim();
            entity.Value["Phone"] = (body.Phone ?? string.Empty).Trim();
            entity.Value["Summary"] = body.Summary!.Trim();
            entity.Value["Description"] = body.Description!.Trim();
            entity.Value["Tags"] = tagValidation.TagsString!;
            entity.Value["ImageUrl"] = (body.ImageUrl ?? string.Empty).Trim();
            entity.Value["UpdatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

            await businessTable.UpdateEntityAsync(entity.Value, ETag.All, TableUpdateMode.Replace);
            return new OkObjectResult(new { success = true });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new NotFoundObjectResult(new { error = "Business or edit code not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Business update failed.");
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    private async Task<IActionResult> ListApprovedBusinessesAsync()
    {
        var table = _tableStorageService.GetTableClient("Businesses");
        var rows = new List<BusinessResponse>();

        try
        {
            await foreach (var entity in table.QueryAsync<TableEntity>("IsApproved eq true"))
            {
                rows.Add(new BusinessResponse
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey,
                    Name = entity.GetString("Name"),
                    Address = entity.GetString("Address"),
                    Phone = entity.GetString("Phone"),
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

    private async Task<IActionResult> CreateBusinessAsync(HttpRequest req)
    {
        var email = AuthHelpers.GetAuthenticatedEmail(req);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new UnauthorizedObjectResult(new { error = "Login is required." });
        }

        var body = await JsonSerializer.DeserializeAsync<BusinessSubmissionRequest>(req.Body, JsonOptions.Default);
        if (body is null)
        {
            return new BadRequestObjectResult(new { error = "Request body is required." });
        }

        var requiredError = ValidationHelpers.RequireFields(new Dictionary<string, string?>
        {
            ["Name"] = body.Name,
            ["Address"] = body.Address,
            ["Summary"] = body.Summary,
            ["Description"] = body.Description
        });

        if (requiredError is not null)
        {
            return new BadRequestObjectResult(new { error = requiredError });
        }

        var tagValidation = ValidationHelpers.ValidateTags(body.Tags, TagCatalog.BusinessTags);
        if (!tagValidation.Ok)
        {
            return new BadRequestObjectResult(new { error = tagValidation.Error, allowedTags = TagCatalog.BusinessTags });
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
            ["EntityType"] = "Business",
            ["TargetTable"] = "Businesses",
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
}
