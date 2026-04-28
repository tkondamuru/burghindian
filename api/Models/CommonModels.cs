namespace WebsiteApi.Models;

public sealed class LookupRequest
{
    public string? EditCode { get; set; }
}

public sealed class DeletePostRequest
{
    public string? EditCode { get; set; }
}

public sealed class UpdateEventRequest : EventSubmissionRequest
{
    public string? EditCode { get; set; }
}

public sealed class UpdateBusinessRequest : BusinessSubmissionRequest
{
    public string? EditCode { get; set; }
}

public sealed class CreatePostResponse
{
    public bool Success { get; set; }
    public string? EditCode { get; set; }
    public object? Id { get; set; }
}

public sealed class LookupResponse
{
    public bool Success { get; set; }
    public string? EntityType { get; set; }
    public string? TargetTable { get; set; }
    public string? PartitionKey { get; set; }
    public string? RowKey { get; set; }
    public string? EditCode { get; set; }
    public object? Post { get; set; }
}

public sealed class AdminPostSummary
{
    public string? EntityType { get; set; }
    public string? PartitionKey { get; set; }
    public string? RowKey { get; set; }
    public string? EditCode { get; set; }
    public string? SubmitterEmail { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Tags { get; set; }
    public string? ImageUrl { get; set; }
    public string? CreatedAtUtc { get; set; }
    public string? UpdatedAtUtc { get; set; }
    public bool IsApproved { get; set; }
}

public sealed class TelegramMappingUpsertRequest
{
    public string? TelegramId { get; set; }
    public string? Email { get; set; }
}

public sealed class TelegramMappingResponse
{
    public bool Success { get; set; }
    public Dictionary<string, string> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
