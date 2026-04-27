namespace WebsiteApi.Models;

public class EventSubmissionRequest
{
    public string? Title { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? Location { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class EventResponse
{
    public string? PartitionKey { get; set; }
    public string? RowKey { get; set; }
    public string? CreatedAtUtc { get; set; }
    public string? UpdatedAtUtc { get; set; }
    public string? Title { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? Location { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string? ImageUrl { get; set; }
}
