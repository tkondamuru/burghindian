using Azure;
using Azure.Data.Tables;

namespace WebsiteApi.Services;

public sealed class TableStorageService
{
    public TableClient GetTableClient(string tableName)
    {
        var connectionString = Environment.GetEnvironmentVariable("TABLE_STORAGE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var serviceClient = new TableServiceClient(connectionString);
            return serviceClient.GetTableClient(tableName);
        }

        var accountName = Environment.GetEnvironmentVariable("TABLE_STORAGE_ACCOUNT_NAME");
        var accountKey = Environment.GetEnvironmentVariable("TABLE_STORAGE_ACCOUNT_KEY");

        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
        {
            throw new InvalidOperationException("Table Storage configuration is missing.");
        }

        var credential = new TableSharedKeyCredential(accountName, accountKey);
        var serviceClientWithCredential = new TableServiceClient(new Uri($"https://{accountName}.table.core.windows.net"), credential);
        return serviceClientWithCredential.GetTableClient(tableName);
    }

    public async Task EnsureTableExistsAsync(TableClient tableClient)
    {
        try
        {
            await tableClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
        }
    }

    public static string GetMonthBucket(DateTimeOffset createdAtUtc) => createdAtUtc.ToString("yyyy-MM");

    public static string CreateContentRowKey(DateTimeOffset createdAtUtc)
    {
        var suffix = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        return $"{createdAtUtc:yyyyMMddHHmmssfff}-{suffix}";
    }
}
