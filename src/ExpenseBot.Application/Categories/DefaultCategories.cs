using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Categories;

/// <summary>
/// The default household categories seeded when <c>categories</c> is empty.
/// Keywords are lowercase; matching is a case-insensitive substring match (see <see cref="CategoryClassifier"/>).
/// </summary>
public static class DefaultCategories
{
    public const string ExpenseOther = "expense-other";
    public const string IncomeOther = "income-other";

    public static string OtherFor(TransactionType type) =>
        type == TransactionType.Income ? IncomeOther : ExpenseOther;

    public static bool IsOther(string categoryId) => categoryId is ExpenseOther or IncomeOther;

    public static IReadOnlyList<Category> Seed() =>
    [
        new()
        {
            Id = "food", Name = "อาหาร", Type = CategoryType.Expense, SortOrder = 10,
            Keywords =
            [
                "ข้าว", "อาหาร", "กับข้าว", "ก๋วยเตี๋ยว", "ผัดไทย", "ผัดกะเพรา", "กะเพรา", "ส้มตำ", "ไก่ทอด", "หมูกระทะ",
                "ชาบู", "สุกี้", "บุฟเฟ่ต์", "ตี๋น้อย", "ร้านอาหาร", "ขนม", "กาแฟ", "นม", "นมโอ๊ต", "ชานม", "ชาเย็น", "ชาไข่มุก", "น้ำดื่ม",
                "น้ำเปล่า", "น้ำแข็ง", "น้ำผลไม้", "ผลไม้", "น้ำปั่น", "เครื่องดื่ม", "เบเกอรี่", "ขนมปัง", "พิซซ่า", "มื้อ",
                "grabfood", "สั่ง grab", "lineman", "สั่ง lineman", "kfc", "mcdonald", "starbucks", "cafe",
            ],
        },
        new()
        {
            Id = "transport", Name = "เดินทาง", Type = CategoryType.Expense, SortOrder = 20,
            Keywords =
            [
                "รถ", "แท็กซี่", "taxi", "grab", "bolt", "bts", "mrt", "รถไฟ", "รถเมล์", "รถตู้", "วินมอไซค์",
                "มอไซค์", "น้ำมัน", "เติมน้ำมัน", "ทางด่วน", "ค่าจอด", "ที่จอดรถ", "ค่าโดยสาร", "ตั๋ว", "เครื่องบิน",
                "เรือ", "ค่าเดินทาง", "easy pass", "ล้างรถ",
            ],
        },
        new()
        {
            Id = "bills", Name = "บิล/ค่าน้ำ-ค่าไฟ", Type = CategoryType.Expense, SortOrder = 30,
            Keywords =
            [
                "ค่าน้ำ", "ค่าไฟ", "ค่าเน็ต", "อินเทอร์เน็ต", "อินเตอร์เน็ต", "เน็ตบ้าน", "ค่าโทรศัพท์", "ค่าโทร",
                "ค่ามือถือ", "เติมเงิน", "ค่าเช่า", "ค่าห้อง", "ค่าหอ", "ค่าส่วนกลาง", "ประกัน", "ผ่อน", "ค่างวด",
                "บัตรเครดิต", "ภาษี", "ค่าบริการ",
            ],
        },
        new()
        {
            Id = "household", Name = "ของใช้", Type = CategoryType.Expense, SortOrder = 40,
            Keywords =
            [
                "ของใช้", "สบู่", "สบู่เหลว", "ยาสีฟัน", "แปรงสีฟัน", "แชมพู", "ทิชชู่", "กระดาษชำระ", "ผงซักฟอก", "น้ำยาล้างจาน",
                "น้ำยาปรับผ้านุ่ม", "ถุงขยะ", "ผ้าอนามัย", "ผ้าอ้อม", "แพมเพิส", "เซเว่น", "7-11", "โลตัส", "lotus",
                "บิ๊กซี", "big c", "แม็คโคร", "makro", "ท็อปส์", "ซุปเปอร์",
            ],
        },
        new()
        {
            Id = "shopping", Name = "ช้อปปิ้ง", Type = CategoryType.Expense, SortOrder = 50,
            Keywords =
            [
                "ช้อป", "ช็อป", "shopee", "lazada", "tiktok shop", "เสื้อ", "กางเกง", "กระโปรง", "รองเท้า", "กระเป๋า",
                "เครื่องสำอาง", "ลิปสติก", "น้ำหอม", "นาฬิกา", "ของขวัญ", "เครื่องประดับ", "แว่น", "มือถือใหม่",
            ],
        },
        new()
        {
            Id = "health", Name = "สุขภาพ", Type = CategoryType.Expense, SortOrder = 60,
            Keywords =
            [
                "ยา", "หมอ", "โรงพยาบาล", "รพ.", "คลินิก", "ทำฟัน", "หมอฟัน", "ทันตกรรม", "ตรวจสุขภาพ", "วิตามิน",
                "อาหารเสริม", "ฟิตเนส", "ยิม", "นวด", "แว่นสายตา",
            ],
        },
        new()
        {
            Id = "entertainment", Name = "บันเทิง", Type = CategoryType.Expense, SortOrder = 70,
            Keywords =
            [
                "ดูหนัง", "หนัง", "โรงหนัง", "ตั๋วหนัง", "ตั๋วคอนเสิร์ต", "netflix", "youtube", "spotify", "disney", "เกม", "game", "steam",
                "คอนเสิร์ต", "คาราโอเกะ", "เที่ยว", "โรงแรม", "ที่พัก", "เบียร์", "เหล้า", "ปาร์ตี้", "สวนสนุก",
            ],
        },
        new() { Id = ExpenseOther, Name = "อื่นๆ", Type = CategoryType.Expense, SortOrder = 99 },
        new()
        {
            Id = "salary", Name = "เงินเดือน", Type = CategoryType.Income, SortOrder = 110,
            Keywords = ["เงินเดือน", "โบนัส", "ค่าล่วงเวลา", "โอที", "ot"],
        },
        new()
        {
            Id = "side-income", Name = "รายได้เสริม", Type = CategoryType.Income, SortOrder = 120,
            Keywords =
            [
                "ขาย", "ค่าจ้าง", "ฟรีแลนซ์", "freelance", "งานเสริม", "ค่าคอม", "คอมมิชชั่น", "ดอกเบี้ย", "ปันผล",
                "ค่าเช่าห้อง", "ทิป", "รายได้เสริม",
            ],
        },
        new() { Id = IncomeOther, Name = "อื่นๆ", Type = CategoryType.Income, SortOrder = 199 },
    ];
}
