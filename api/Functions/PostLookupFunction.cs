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

public sealed class PostLookupFunction
{
    private readonly TableStorageService _tableStorageService;
    private readonly ILogger<PostLookupFunction> _logger;

    public PostLookupFunction(TableStorageService tableStorageService, ILogger<PostLookupFunction> logger)
    {
        _tableStorageService = tableStorageService;
        _logger = logger;
    }

    [Function("LookupPostByEditCode")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "posts/lookup")] HttpRequest req)
    {
        try
        {
            var email = AuthHelpers.GetAuthenticatedEmail(req);
            var isAdmin = AuthHelpers.IsAdminEmail(email);
            if (string.IsNullOrWhiteSpace(email))
            {
                return new UnauthorizedObjectResult(new { error = "Login is required." });
            }

            var body = await JsonSerializer.DeserializeAsync<LookupRequest>(req.Body, JsonOptions.Default);
            var editCode = body?.EditCode?.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(editCode))
            {
                return new BadRequestObjectResult(new { error = "Edit code is required." });
            }

            var lookupTable = _tableStorageService.GetTableClient("EditCodeLookup");
            var lookup = await lookupTable.GetEntityIfExistsAsync<TableEntity>(EditCodeService.LookupPartitionKey, editCode);
            if (!lookup.HasValue)
            {
                return new NotFoundObjectResult(new { error = "Edit code or post not found." });
            }

            var lookupEntity = lookup.Value;

            if (!isAdmin && !string.Equals(lookupEntity.GetString("SubmitterEmail"), email, StringComparison.OrdinalIgnoreCase))
            {
                return new ObjectResult(new { error = "This edit code does not belong to the signed-in Gmail account." }) { StatusCode = StatusCodes.Status403Forbidden };
            }

            var targetTable = lookupEntity.GetString("TargetTable") ?? string.Empty;
            var targetPartitionKey = lookupEntity.GetString("TargetPartitionKey") ?? string.Empty;
            var targetRowKey = lookupEntity.GetString("TargetRowKey") ?? string.Empty;
            var entityType = lookupEntity.GetString("EntityType") ?? string.Empty;

            var contentTable = _tableStorageService.GetTableClient(targetTable);
            var entity = await contentTable.GetEntityIfExistsAsync<TableEntity>(targetPartitionKey, targetRowKey);
            if (!entity.HasValue)
            {
                return new NotFoundObjectResult(new { error = "Edit code or post not found." });
            }

            var contentEntity = entity.Value;

            object post = entityType == "Event"
                ? new EventResponse
                {
                    PartitionKey = contentEntity.PartitionKey,
                    RowKey = contentEntity.RowKey,
                    Title = contentEntity.GetString("Title"),
                    Date = contentEntity.GetString("Date"),
                    Time = contentEntity.GetString("Time"),
                    Location = contentEntity.GetString("Location"),
                    Summary = contentEntity.GetString("Summary"),
                    Description = contentEntity.GetString("Description"),
                    Tags = contentEntity.GetString("Tags"),
                    ImageUrl = contentEntity.GetString("ImageUrl")
                }
                : new BusinessResponse
                {
                    PartitionKey = contentEntity.PartitionKey,
                    RowKey = contentEntity.RowKey,
                    Name = contentEntity.GetString("Name"),
                    Address = contentEntity.GetString("Address"),
                    Phone = contentEntity.GetString("Phone"),
                    Summary = contentEntity.GetString("Summary"),
                    Description = contentEntity.GetString("Description"),
                    Tags = contentEntity.GetString("Tags"),
                    ImageUrl = contentEntity.GetString("ImageUrl")
                };

            return new OkObjectResult(new LookupResponse
            {
                Success = true,
                EntityType = entityType,
                TargetTable = targetTable,
                PartitionKey = targetPartitionKey,
                RowKey = targetRowKey,
                EditCode = editCode,
                Post = post
            });
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Post lookup failed because edit code or target entity was not found.");
            return new NotFoundObjectResult(new { error = "Edit code or post not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post lookup failed for signed-in user {Email}.", AuthHelpers.GetAuthenticatedEmail(req));
            return new ObjectResult(new { error = "Unexpected server error." }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }
}
