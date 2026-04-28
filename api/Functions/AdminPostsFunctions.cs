using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WebsiteApi.Models;
using WebsiteApi.Services;

namespace WebsiteApi.Functions;

public sealed class AdminPostsFunctions
{
    private readonly TableStorageService _tableStorageService;
    private readonly ILogger<AdminPostsFunctions> _logger;

    public AdminPostsFunctions(TableStorageService tableStorageService, ILogger<AdminPostsFunctions> logger)
    {
        _tableStorageService = tableStorageService;
        _logger = logger;
    }

    [Function("GetAdminPosts")]
    public async Task<IActionResult> GetAdminPosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage-posts")] HttpRequest req)
    {
        try
        {
            var email = AuthHelpers.GetAuthenticatedEmail(req);
            if (string.IsNullOrWhiteSpace(email))
            {
                return new UnauthorizedObjectResult(new { error = "Login is required." });
            }

            if (!AuthHelpers.IsAdminEmail(email))
            {
                return new ObjectResult(new { error = "Admin access is required." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var results = new List<AdminPostSummary>();
            await LoadTableAsync("Events", "Event", "Title", results);
            await LoadTableAsync("Businesses", "Business", "Name", results);

            return new OkObjectResult(results
                .OrderByDescending(item => item.CreatedAtUtc, StringComparer.Ordinal)
                .ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin posts listing failed for signed-in user {Email}.", AuthHelpers.GetAuthenticatedEmail(req));
            return new ObjectResult(new { error = "Unexpected server error." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private async Task LoadTableAsync(string tableName, string entityType, string titleField, List<AdminPostSummary> results)
    {
        var table = _tableStorageService.GetTableClient(tableName);

        try
        {
            await foreach (var entity in table.QueryAsync<TableEntity>())
            {
                results.Add(new AdminPostSummary
                {
                    EntityType = entityType,
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey,
                    EditCode = entity.GetString("EditCode"),
                    SubmitterEmail = entity.GetString("SubmitterEmail"),
                    Title = entity.GetString(titleField),
                    Summary = entity.GetString("Summary"),
                    Tags = entity.GetString("Tags"),
                    ImageUrl = entity.GetString("ImageUrl"),
                    CreatedAtUtc = entity.GetString("CreatedAtUtc"),
                    UpdatedAtUtc = entity.GetString("UpdatedAtUtc"),
                    IsApproved = entity.GetBoolean("IsApproved") ?? false
                });
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }
}
