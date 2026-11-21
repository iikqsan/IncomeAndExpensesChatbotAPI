using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Tests;

public class CategoryClassifierTests
{
    private static readonly IReadOnlyList<Category> Seed = DefaultCategories.Seed();

    [Theory]
    [InlineData("ซื้อข้าวกลางวัน", "food")]
    [InlineData("กาแฟ", "food")]
    [InlineData("ค่าน้ำแข็ง", "food")]          // "น้ำแข็ง" (longer) beats "ค่าน้ำ"
    [InlineData("ค่าน้ำ", "bills")]
    [InlineData("ค่าไฟเดือนนี้", "bills")]
    [InlineData("ค่าเช่า", "bills")]
    [InlineData("แท็กซี่ไปทำงาน", "transport")]
    [InlineData("เติมน้ำมัน", "transport")]
    [InlineData("Grab ไปสนามบิน", "transport")]  // case-insensitive
    [InlineData("GrabFood", "food")]
    [InlineData("ยาสีฟัน", "household")]        // "ยาสีฟัน" beats "ยา"
    [InlineData("ยาแก้ไข้", "health")]
    [InlineData("รองเท้าวิ่ง", "shopping")]
    [InlineData("Netflix", "entertainment")]
    [InlineData("ตั๋วหนัง", "entertainment")]
    [InlineData("หวย", DefaultCategories.ExpenseOther)]
    [InlineData("", DefaultCategories.ExpenseOther)]
    public void Expense(string description, string expected)
    {
        Assert.Equal(expected, CategoryClassifier.Classify(description, TransactionType.Expense, Seed));
    }

    [Theory]
    [InlineData("เงินเดือน", "salary")]
    [InlineData("เงินเดือนพฤศจิกายน", "salary")]
    [InlineData("โบนัส", "salary")]
    [InlineData("ขายของออนไลน์", "side-income")]
    [InlineData("ได้เงินจากแม่", DefaultCategories.IncomeOther)]
    public void Income(string description, string expected)
    {
        Assert.Equal(expected, CategoryClassifier.Classify(description, TransactionType.Income, Seed));
    }

    [Fact]
    public void Only_categories_of_the_same_type_are_considered()
    {
        // "ค่าจ้าง" is an income keyword; as an expense it must not land in an income category.
        Assert.Equal(DefaultCategories.ExpenseOther, CategoryClassifier.Classify("ค่าจ้าง", TransactionType.Expense, Seed));
    }

    [Fact]
    public void Both_type_category_matches_either()
    {
        Category[] cats = [new() { Id = "gift", Name = "ของขวัญ", Type = CategoryType.Both, Keywords = ["ของขวัญ"] }];
        Assert.Equal("gift", CategoryClassifier.Classify("ของขวัญวันเกิด", TransactionType.Expense, cats));
        Assert.Equal("gift", CategoryClassifier.Classify("ของขวัญวันเกิด", TransactionType.Income, cats));
    }

    [Fact]
    public void Tie_goes_to_lower_sort_order()
    {
        Category[] cats =
        [
            new() { Id = "b", Name = "B", Type = CategoryType.Expense, SortOrder = 2, Keywords = ["ab"] },
            new() { Id = "a", Name = "A", Type = CategoryType.Expense, SortOrder = 1, Keywords = ["xy"] },
        ];
        Assert.Equal("a", CategoryClassifier.Classify("ab xy", TransactionType.Expense, cats));
    }

    [Fact]
    public void Default_seed_matches_design()
    {
        Assert.Equal(["อาหาร", "เดินทาง", "บิล", "ของใช้", "ช้อปปิ้ง", "สุขภาพ", "บันเทิง", "อื่นๆ"],
            Seed.Where(c => c.Type == CategoryType.Expense).OrderBy(c => c.SortOrder).Select(c => c.Name));
        Assert.Equal(["เงินเดือน", "รายได้เสริม", "อื่นๆ"],
            Seed.Where(c => c.Type == CategoryType.Income).OrderBy(c => c.SortOrder).Select(c => c.Name));
        Assert.All(Seed.SelectMany(c => c.Keywords), k => Assert.Equal(k.ToLowerInvariant(), k));
    }
}
