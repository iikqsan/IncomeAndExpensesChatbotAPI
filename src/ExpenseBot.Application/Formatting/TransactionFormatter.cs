using System.Globalization;
using System.Text;
using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Summaries;

namespace ExpenseBot.Application.Formatting;

/// <summary>
/// The single place that builds every reply text. Output is Telegram HTML,
/// so any user-provided text goes through <see cref="Escape"/>.
/// (Callback-query toasts are plain text and say so in their method name.)
/// </summary>
public sealed class TransactionFormatter(ICategoryProvider categories)
{
    public const string ParseFailedText = "ไม่พบจำนวนเงิน ลองพิมพ์แบบ: ข้าวกลางวัน 60 หรือ +เงินเดือน 25000";

    private static readonly string[] ThaiMonthsShort =
        ["ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.", "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."];

    private static readonly string[] ThaiMonthsFull =
        ["มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
         "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม"];

    // ---------- Transactions ----------

    /// <summary><c>🔴 #42 | อาหาร | -60 บาท, ซื้อข้าวกลางวัน</c></summary>
    public string Format(Transaction tx)
    {
        var isIncome = tx.Type == TransactionType.Income;
        var dot = isIncome ? "🟢" : "🔴";
        var sign = isIncome ? "+" : "-";

        var line = $"{dot} #{tx.Seq} | {Escape(CategoryName(tx.CategoryId))} | {sign}{FormatAmount(tx.Amount)} บาท";
        return string.IsNullOrWhiteSpace(tx.Description) ? line : $"{line}, {Escape(tx.Description)}";
    }

    public string Deleted(Transaction tx) => $"ลบแล้ว: {Format(tx)}";

    public string Edited(Transaction tx) => $"แก้แล้ว: {Format(tx)}";

    public string EditParseFailed(Transaction tx) =>
        $"แก้ไม่สำเร็จ: ไม่พบจำนวนเงินในข้อความใหม่ รายการเดิมยังอยู่\n{Format(tx)}";

    public string ParseFailed() => ParseFailedText;

    public string List(IReadOnlyList<Transaction> items)
    {
        if (items.Count == 0)
            return "ยังไม่มีรายการ";

        var sb = new StringBuilder($"<b>{items.Count} รายการล่าสุด</b>");
        foreach (var tx in items)
            sb.Append('\n').Append(Format(tx));
        return sb.ToString();
    }

    public string NothingToUndo() => "ยังไม่มีรายการให้ลบ";

    public string SeqNotFound(long seq) => $"ไม่พบรายการ #{seq}";

    // ---------- Summary ----------

    public string Summary(SummaryReport report)
    {
        var sb = new StringBuilder($"<b>{PeriodTitle(report.Period)}</b>");

        if (report.Lines.Count == 0)
            return sb.Append("\nยังไม่มีรายการ").ToString();

        AppendSection(sb, report, TransactionType.Income, "รายรับ", "+");
        AppendSection(sb, report, TransactionType.Expense, "รายจ่าย", "-");

        var balance = report.Balance;
        var dot = balance >= 0 ? "🟢" : "🔴";
        var sign = balance > 0 ? "+" : balance < 0 ? "-" : "";
        sb.Append($"\n\n{dot} <b>คงเหลือ</b> {sign}{FormatAmount(balance)} บาท");
        return sb.ToString();
    }

    private void AppendSection(StringBuilder sb, SummaryReport report, TransactionType type, string title, string sign)
    {
        var lines = report.Lines
            .Where(l => l.Type == type)
            .OrderBy(l => categories.GetById(l.CategoryId)?.SortOrder ?? int.MaxValue)
            .ToList();
        var total = lines.Sum(l => l.Total);

        sb.Append($"\n\n<b>{title}</b> {(total > 0 ? sign : "")}{FormatAmount(total)} บาท");
        foreach (var l in lines)
            sb.Append($"\n• {Escape(CategoryName(l.CategoryId))} {sign}{FormatAmount(l.Total)}");
    }

    public static string PeriodTitle(SummaryPeriod p) => p.Kind switch
    {
        PeriodKind.Day => $"สรุปวันนี้ ({ThaiDate(p.FirstDay)})",
        PeriodKind.Week => $"สรุปสัปดาห์นี้ ({ThaiDateRange(p.FirstDay, p.LastDay)})",
        _ => $"สรุปเดือน{ThaiMonthsFull[p.FirstDay.Month - 1]} {p.FirstDay.Year}",
    };

    public static string ThaiDate(DateOnly d) => $"{d.Day} {ThaiMonthsShort[d.Month - 1]} {d.Year}";

    public static string ThaiDateRange(DateOnly from, DateOnly to)
    {
        if (from.Year == to.Year && from.Month == to.Month)
            return $"{from.Day}–{to.Day} {ThaiMonthsShort[to.Month - 1]} {to.Year}";
        if (from.Year == to.Year)
            return $"{from.Day} {ThaiMonthsShort[from.Month - 1]} – {to.Day} {ThaiMonthsShort[to.Month - 1]} {to.Year}";
        return $"{ThaiDate(from)} – {ThaiDate(to)}";
    }

    // ---------- Help ----------

    public string Help(bool isAdmin)
    {
        var sb = new StringBuilder();
        sb.Append("<b>วิธีบันทึก</b>\n");
        sb.Append("พิมพ์รายการตามด้วยจำนวนเงิน เช่น\n");
        sb.Append("• ซื้อข้าวกลางวัน 60\n");
        sb.Append("• ค่าเช่า 1,200\n");
        sb.Append("• กาแฟ 45.50\n");
        sb.Append("• +เงินเดือน 25000\n");
        sb.Append("ถือเป็นรายจ่าย ยกเว้นขึ้นต้นด้วย + หรือมีคำว่า รับ, เงินเดือน, ได้เงิน, โบนัส\n");
        sb.Append("แก้ข้อความที่ส่งไปแล้วใน Telegram เพื่อแก้รายการนั้นได้\n\n");

        sb.Append("<b>คำสั่ง</b>\n");
        sb.Append("/today สรุปวันนี้\n");
        sb.Append("/week สรุปสัปดาห์นี้ (เริ่มวันจันทร์)\n");
        sb.Append("/month สรุปเดือนนี้\n");
        sb.Append("/month 2026-10 สรุปเดือนย้อนหลัง\n");
        sb.Append("/list 10 รายการล่าสุด\n");
        sb.Append("/undo ลบรายการล่าสุด\n");
        sb.Append("/delete &lt;#&gt; ลบรายการที่ระบุ เช่น /delete 42\n");
        sb.Append("/help วิธีใช้\n");

        if (isAdmin)
        {
            sb.Append("\n<b>คำสั่งผู้ดูแล</b>\n");
            sb.Append("/users รายชื่อผู้ใช้และสถานะ\n");
            sb.Append("/block &lt;id&gt; ระงับสิทธิ์ผู้ใช้\n");
            sb.Append("/addkeyword &lt;หมวด&gt; &lt;คำ&gt; เพิ่ม keyword ให้หมวด\n");
        }

        sb.Append("\n<b>หมวด</b>\n");
        sb.Append($"รายจ่าย: {CategoryNames(CategoryType.Expense)}\n");
        sb.Append($"รายรับ: {CategoryNames(CategoryType.Income)}");
        return sb.ToString();
    }

    private string CategoryNames(CategoryType type) =>
        Escape(string.Join(", ", categories.GetAll()
            .Where(c => c.Type == type || c.Type == CategoryType.Both)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Name)));

    // ---------- Access / users ----------

    public string Welcome(BotUser user) =>
        $"สวัสดี {Escape(user.DisplayName)} พิมพ์รายการได้เลย เช่น ข้าวกลางวัน 60 หรือพิมพ์ /help เพื่อดูวิธีใช้";

    public string AccessRequested() => "ส่งคำขอแล้ว รอผู้ดูแลอนุมัติ";

    public string Pending() => "รอผู้ดูแลอนุมัติ ระหว่างนี้ยังบันทึกรายการไม่ได้";

    public string Blocked() => "บัญชีนี้ถูกระงับการใช้งาน";

    public string AccessRequestForAdmin(BotUser user) =>
        $"<b>คำขอใช้งานใหม่</b>\n{UserLabel(user)} ขอใช้งาน\nID: <code>{user.Id}</code>";

    public string ApproveButton() => "อนุมัติ";

    public string RejectButton() => "ปฏิเสธ";

    public string AdminDecisionDone(BotUser user, bool approved) =>
        approved ? $"✅ อนุมัติ {UserLabel(user)} แล้ว" : $"⛔ ปฏิเสธ {UserLabel(user)} แล้ว";

    public string ApprovedNotice() => "ผู้ดูแลอนุมัติแล้ว เริ่มบันทึกได้เลย พิมพ์ /help เพื่อดูวิธีใช้";

    public string RejectedNotice() => "ผู้ดูแลปฏิเสธคำขอใช้งาน";

    public string CallbackDonePlain(bool approved) => approved ? "อนุมัติแล้ว" : "ปฏิเสธแล้ว";

    public string CallbackAlreadyDecidedPlain() => "คำขอนี้ถูกดำเนินการไปแล้ว";

    public string CallbackUserNotFoundPlain() => "ไม่พบผู้ใช้";

    public string CallbackNotAllowedPlain() => "เฉพาะผู้ดูแลเท่านั้น";

    public string Users(IReadOnlyList<BotUser> users)
    {
        if (users.Count == 0)
            return "ยังไม่มีผู้ใช้";

        var sb = new StringBuilder($"<b>ผู้ใช้ทั้งหมด {users.Count} คน</b>");
        foreach (var u in users)
        {
            var role = u.Role == UserRole.Admin ? " (admin)" : "";
            sb.Append($"\n• {UserLabel(u)}{role} <code>{u.Id}</code> — {StatusText(u.Status)}");
        }
        return sb.ToString();
    }

    public string UserBlocked(BotUser user) => $"ระงับสิทธิ์ {UserLabel(user)} แล้ว";

    public string UserAlreadyBlocked(BotUser user) => $"{UserLabel(user)} ถูกระงับอยู่แล้ว";

    public string CannotBlockAdmin() => "ระงับสิทธิ์ผู้ดูแลหลักไม่ได้";

    public string UserNotFound(long userId) => $"ไม่พบผู้ใช้ ID <code>{userId}</code>";

    public string NotAdmin() => "คำสั่งนี้สำหรับผู้ดูแลเท่านั้น";

    // ---------- Keywords ----------

    public string KeywordAdded(Category category, string keyword) =>
        $"เพิ่ม keyword \"{Escape(keyword)}\" ให้หมวด {Escape(category.Name)} แล้ว";

    public string KeywordExists(Category category, string keyword) =>
        $"หมวด {Escape(category.Name)} มี keyword \"{Escape(keyword)}\" อยู่แล้ว";

    public string CategoryNotFound(string name) =>
        $"ไม่พบหมวด \"{Escape(name)}\" หมวดที่เพิ่ม keyword ได้: " +
        Escape(string.Join(", ", categories.GetAll().Where(c => c.Name != "อื่นๆ").Select(c => c.Name)));

    // ---------- Misc ----------

    public string UnknownCommand() => "ไม่รู้จักคำสั่งนี้ พิมพ์ /help เพื่อดูคำสั่งทั้งหมด";

    public string Usage(string usage) => $"วิธีใช้: {Escape(usage)}";

    // ---------- Helpers ----------

    /// <summary>Thousands separator; 2 decimals only when there is a fractional part.</summary>
    public static string FormatAmount(decimal amount)
    {
        var abs = Math.Abs(amount);
        var format = abs == decimal.Truncate(abs) ? "#,##0" : "#,##0.00";
        return abs.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> for Telegram HTML parse mode.</summary>
    public static string Escape(string? text) =>
        (text ?? "")
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    private string CategoryName(string categoryId) => categories.GetById(categoryId)?.Name ?? "อื่นๆ";

    private static string UserLabel(BotUser u) =>
        string.IsNullOrEmpty(u.Username) ? Escape(u.DisplayName) : $"{Escape(u.DisplayName)} (@{Escape(u.Username)})";

    private static string StatusText(UserStatus s) => s switch
    {
        UserStatus.Active => "ใช้งาน",
        UserStatus.Pending => "รออนุมัติ",
        _ => "ถูกระงับ",
    };
}
