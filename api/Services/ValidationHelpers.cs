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

    public static (bool Ok, string? Error, string? TagsString, IReadOnlyList<string>? Tags) ValidateTags(string? rawTags, IReadOnlyList<string> allowedTags)
    {
        var allowed = new HashSet<string>(allowedTags, StringComparer.Ordinal);
        var requested = (rawTags ?? string.Empty)
            .Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Count == 0)
        {
            return (false, "At least one tag is required.", null, null);
        }

        var invalid = requested.FirstOrDefault(tag => !allowed.Contains(tag));
        if (invalid is not null)
        {
            return (false, $"Invalid tag: {invalid}", null, null);
        }

        var canonical = allowedTags.Where(requested.Contains).ToList();
        return (true, null, string.Join(",", canonical), canonical);
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
