namespace WebsiteApi.Services;

public static class TagCatalog
{
    public static readonly IReadOnlyList<string> BusinessCategories =
    [
        "Restaurants",
        "Grocery & Markets",
        "Temples",
        "Services & Others"
    ];

    public static readonly IReadOnlyList<string> EventTags =
    [
        "Community",
        "Culture",
        "Family",
        "Food",
        "Temple",
        "Professional",
        "Kids",
        "Other"
    ];

    public static readonly IReadOnlyList<string> BusinessTags =
    [
        "Restaurant",
        "Grocery",
        "Temple",
        "Service",
        "Shopping",
        "Education",
        "Health",
        "Other"
    ];
}
