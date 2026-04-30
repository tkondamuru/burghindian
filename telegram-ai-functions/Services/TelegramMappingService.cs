using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace api.Services;

public sealed class TelegramMappingService
{
    private const string TableName = "AdminSettings";
    private const string PartitionKey = "config";
    private const string RowKey = "telegram-email-map";

    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<TelegramMappingService> _logger;

    public TelegramMappingService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TelegramMappingService>();

        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("AzureWebJobsStorage configuration is missing.");
        }

        _tableServiceClient = new TableServiceClient(connectionString);
    }

    public async Task<string?> GetUserEmailAsync(long userId)
    {
        var mappings = await ReadMappingsAsync();
        if (mappings.TryGetValue(userId.ToString(), out var email) && !string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        return null;
    }

    private async Task<Dictionary<string, string>> ReadMappingsAsync()
    {
        var table = _tableServiceClient.GetTableClient(TableName);

        try
        {
            var entity = await table.GetEntityAsync<TableEntity>(PartitionKey, RowKey);
            var json = entity.Value.GetString("MappingsJson");
            return DeserializeMappings(json);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Table or entity not found, assume no mappings
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Telegram mappings from table storage.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
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

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
