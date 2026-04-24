using Azure;
using Azure.Data.Tables;

namespace WebsiteApi.Services;

public sealed class EditCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const string LookupPartitionKey = "edit";

    public async Task<string> CreateUniqueEditCodeAsync(TableClient lookupTableClient, int attempts = 12)
    {
        for (var index = 0; index < attempts; index += 1)
        {
            var editCode = CreateEditCode();

            try
            {
                await lookupTableClient.GetEntityAsync<TableEntity>(LookupPartitionKey, editCode);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return editCode;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique edit code.");
    }

    private static string CreateEditCode(int length = 5)
    {
        var chars = new char[length];
        for (var index = 0; index < length; index += 1)
        {
            chars[index] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        }

        return new string(chars);
    }
}
