namespace WebsiteApi.Models;

public class BusinessSubmissionRequest
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Category { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class BusinessResponse
{
    public string? PartitionKey { get; set; }
    public string? RowKey { get; set; }
    public string? CreatedAtUtc { get; set; }
    public string? UpdatedAtUtc { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Category { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string? ImageUrl { get; set; }
}
