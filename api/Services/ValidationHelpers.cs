namespace WebsiteApi.Services;

public static class ValidationHelpers
{
    public static string? RequireFields(IDictionary<string, string?> fields)
    {
        foreach (var entry in fields)
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                return $"{entry.Key} is required.";
            }
        }

        return null;
    }

    public static string NormalizeTags(string? rawTags)
    {
        var requested = (rawTags ?? string.Empty)
            .Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(",", requested);
    }

    public static (bool Ok, string? Error, string? Category) ValidateCategory(string? rawCategory, IReadOnlyList<string> allowedCategories)
    {
        var requested = (rawCategory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requested))
        {
            return (false, "Category is required.", null);
        }

        var canonical = allowedCategories.FirstOrDefault(category => string.Equals(category, requested, StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            return (false, $"Invalid category: {requested}", null);
        }

        return (true, null, canonical);
    }
}
