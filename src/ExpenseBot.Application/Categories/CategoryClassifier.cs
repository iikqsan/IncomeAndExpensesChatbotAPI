using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Categories;

/// <summary>
/// Picks a category from keywords. Pure function.
/// Only categories of the transaction's type (or <see cref="CategoryType.Both"/>) are considered.
/// The longest matching keyword wins ("ค่าน้ำแข็ง": "น้ำแข็ง" beats "ค่าน้ำ"); ties go to the lower sortOrder.
/// No match gives the "อื่นๆ" category of that type.
/// </summary>
public static class CategoryClassifier
{
    public static string Classify(string? description, TransactionType type, IEnumerable<Category> categories)
    {
        var text = (description ?? "").ToLowerInvariant();
        if (text.Length == 0)
            return DefaultCategories.OtherFor(type);

        var wanted = type == TransactionType.Income ? CategoryType.Income : CategoryType.Expense;

        Category? best = null;
        var bestLength = 0;
        foreach (var category in categories.OrderBy(c => c.SortOrder))
        {
            if (category.Type != wanted && category.Type != CategoryType.Both)
                continue;
            if (DefaultCategories.IsOther(category.Id))
                continue;

            foreach (var keyword in category.Keywords)
            {
                var k = keyword.Trim().ToLowerInvariant();
                if (k.Length > bestLength && text.Contains(k, StringComparison.Ordinal))
                {
                    best = category;
                    bestLength = k.Length;
                }
            }
        }

        return best?.Id ?? DefaultCategories.OtherFor(type);
    }
}
