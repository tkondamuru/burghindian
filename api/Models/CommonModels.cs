namespace WebsiteApi.Models;

public sealed class LookupRequest
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
