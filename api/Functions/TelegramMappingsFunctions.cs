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

public sealed class TelegramMappingsFunctions
{
    private const string TableName = "AdminSettings";
    private const string PartitionKey = "config";
    private const string RowKey = "telegram-email-map";

    private readonly TableStorageService _tableStorageService;
    private readonly ILogger<TelegramMappingsFunctions> _logger;

    public TelegramMappingsFunctions(TableStorageService tableStorageService, ILogger<TelegramMappingsFunctions> logger)
    {
        _tableStorageService = tableStorageService;
        _logger = logger;
    }

    [Function("ManageTelegramMappings")]
    public async Task<IActionResult> ManageTelegramMappings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", "delete", Route = "manage-telegram-links/{telegramId?}")] HttpRequest req,
        string? telegramId)
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

            return HttpMethods.IsGet(req.Method)
                ? await GetMappingsAsync()
                : HttpMethods.IsPut(req.Method)
                    ? await UpsertMappingAsync(req)
                    : await DeleteMappingAsync(telegramId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram mapping management failed for signed-in user {Email}.", AuthHelpers.GetAuthenticatedEmail(req));
            return new ObjectResult(new { error = "Unexpected server error." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private async Task<IActionResult> GetMappingsAsync()
    {
        var mappings = await ReadMappingsAsync();
        return new OkObjectResult(new TelegramMappingResponse
        {
            Success = true,
            Mappings = mappings
        });
    }

    private async Task<IActionResult> UpsertMappingAsync(HttpRequest req)
    {
        var body = await JsonSerializer.DeserializeAsync<TelegramMappingUpsertRequest>(req.Body, JsonOptions.Default);
        if (body is null)
        {
            return new BadRequestObjectResult(new { error = "Request body is required." });
        }

        var telegramId = (body.TelegramId ?? string.Empty).Trim();
        var email = (body.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(telegramId) || string.IsNullOrWhiteSpace(email))
        {
            return new BadRequestObjectResult(new { error = "Telegram ID and email are required." });
        }

        var mappings = await ReadMappingsAsync();
        mappings[telegramId] = email;
        await SaveMappingsAsync(mappings);

        return new OkObjectResult(new TelegramMappingResponse
        {
            Success = true,
            Mappings = mappings
        });
    }

    private async Task<IActionResult> DeleteMappingAsync(string? telegramId)
    {
        var normalizedTelegramId = (telegramId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTelegramId))
        {
            return new BadRequestObjectResult(new { error = "Telegram ID is required." });
        }

        var mappings = await ReadMappingsAsync();
        if (!mappings.Remove(normalizedTelegramId))
        {
            return new NotFoundObjectResult(new { error = "Telegram mapping not found." });
        }

        await SaveMappingsAsync(mappings);
        return new OkObjectResult(new TelegramMappingResponse
        {
            Success = true,
            Mappings = mappings
        });
    }

    private async Task<Dictionary<string, string>> ReadMappingsAsync()
    {
        var table = _tableStorageService.GetTableClient(TableName);

        try
        {
            var entity = await table.GetEntityAsync<TableEntity>(PartitionKey, RowKey);
            var json = entity.Value.GetString("MappingsJson");
            return DeserializeMappings(json);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveMappingsAsync(Dictionary<string, string> mappings)
    {
        var table = _tableStorageService.GetTableClient(TableName);
        await _tableStorageService.EnsureTableExistsAsync(table);

        var entity = new TableEntity(PartitionKey, RowKey)
        {
            ["MappingsJson"] = JsonSerializer.Serialize(mappings, JsonOptions.Default),
            ["UpdatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    private static Dictionary<string, string> DeserializeMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions.Default);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
