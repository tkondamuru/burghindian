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
            var lookup = await lookupTable.GetEntityAsync<TableEntity>(EditCodeService.LookupPartitionKey, editCode);

            if (!isAdmin && !string.Equals(lookup.Value.GetString("SubmitterEmail"), email, StringComparison.OrdinalIgnoreCase))
            {
                return new ObjectResult(new { error = "This edit code does not belong to the signed-in Gmail account." }) { StatusCode = StatusCodes.Status403Forbidden };
            }

            var targetTable = lookup.Value.GetString("TargetTable") ?? string.Empty;
            var targetPartitionKey = lookup.Value.GetString("TargetPartitionKey") ?? string.Empty;
            var targetRowKey = lookup.Value.GetString("TargetRowKey") ?? string.Empty;
            var entityType = lookup.Value.GetString("EntityType") ?? string.Empty;

            var contentTable = _tableStorageService.GetTableClient(targetTable);
            var entity = await contentTable.GetEntityAsync<TableEntity>(targetPartitionKey, targetRowKey);

            object post = entityType == "Event"
                ? new EventResponse
                {
                    PartitionKey = entity.Value.PartitionKey,
                    RowKey = entity.Value.RowKey,
                    Title = entity.Value.GetString("Title"),
                    Date = entity.Value.GetString("Date"),
                    Time = entity.Value.GetString("Time"),
                    Location = entity.Value.GetString("Location"),
                    Summary = entity.Value.GetString("Summary"),
                    Description = entity.Value.GetString("Description"),
                    Tags = entity.Value.GetString("Tags"),
                    ImageUrl = entity.Value.GetString("ImageUrl")
                }
                : new BusinessResponse
                {
                    PartitionKey = entity.Value.PartitionKey,
                    RowKey = entity.Value.RowKey,
                    Name = entity.Value.GetString("Name"),
                    Address = entity.Value.GetString("Address"),
                    Phone = entity.Value.GetString("Phone"),
                    Summary = entity.Value.GetString("Summary"),
                    Description = entity.Value.GetString("Description"),
                    Tags = entity.Value.GetString("Tags"),
                    ImageUrl = entity.Value.GetString("ImageUrl")
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
