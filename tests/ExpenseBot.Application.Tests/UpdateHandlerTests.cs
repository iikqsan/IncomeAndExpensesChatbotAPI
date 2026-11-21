using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Formatting;
using ExpenseBot.Application.Updates;

namespace ExpenseBot.Application.Tests;

public class UpdateHandlerTests
{
    private const long Admin = TestApp.Admin;
    private const long Mom = 2002;
    private const long Kid = 3003;

    private readonly TestApp _app = new();

    // ---------- Recording ----------

    [Fact]
    public async Task Admin_records_expense_with_category()
    {
        await _app.Send(Admin, "ซื้อข้าวกลางวัน 60");

        var t = Assert.Single(_app.Transactions.Items);
        Assert.Equal(Admin, t.UserId);
        Assert.Equal(1, t.Seq);
        Assert.Equal("food", t.CategoryId);
        Assert.Equal(DateTimeKind.Utc, t.OccurredAt.Kind);
        Assert.Equal("🔴 #1 | อาหาร | -60 บาท, ซื้อข้าวกลางวัน", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Income_is_classified()
    {
        await _app.Send(Admin, "+เงินเดือน 25000");
        Assert.Equal("🟢 #1 | เงินเดือน | +25,000 บาท, เงินเดือน", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Unparsable_text_is_not_saved()
    {
        await _app.Send(Admin, "สวัสดี");

        Assert.Empty(_app.Transactions.Items);
        Assert.Equal(TransactionFormatter.ParseFailedText, _app.LastTo(Admin));
    }

    [Fact]
    public async Task Same_message_id_is_not_recorded_twice()
    {
        await _app.Send(Admin, "ข้าว 60", messageId: 50);
        await _app.Send(Admin, "ข้าว 60", messageId: 50);

        Assert.Single(_app.Transactions.Items);
    }

    // ---------- Gates ----------

    [Fact]
    public async Task Group_message_makes_bot_leave_and_is_not_processed()
    {
        await _app.Send(Admin, "ข้าว 60", chat: ChatKind.Group);

        Assert.Equal([-5000L], _app.Messenger.LeftChats);
        Assert.Empty(_app.Transactions.Items);
        Assert.Empty(_app.Users.Store);
    }

    [Fact]
    public async Task Added_to_group_leaves_but_removed_from_group_does_nothing()
    {
        await _app.Handler.HandleAsync(new BotUpdate
        {
            UpdateId = 1, Kind = BotUpdateKind.MyChatMember, ChatId = -1, Chat = ChatKind.Group,
            From = TestApp.Sender(Mom), BotIsMember = true,
        }, default);
        await _app.Handler.HandleAsync(new BotUpdate
        {
            UpdateId = 2, Kind = BotUpdateKind.MyChatMember, ChatId = -2, Chat = ChatKind.Group,
            From = TestApp.Sender(Mom), BotIsMember = false,
        }, default);

        Assert.Equal([-1L], _app.Messenger.LeftChats);
    }

    [Fact]
    public async Task Duplicate_update_is_ignored()
    {
        var update = new BotUpdate
        {
            UpdateId = 99, Kind = BotUpdateKind.Message, ChatId = Admin, Chat = ChatKind.Private,
            From = TestApp.Sender(Admin), MessageId = 1, Text = "ข้าว 60", DateUtc = DateTime.UtcNow,
        };
        await _app.Handler.HandleAsync(update, default);
        await _app.Handler.HandleAsync(update with { MessageId = 2 }, default);

        Assert.Single(_app.Transactions.Items);
    }

    [Fact]
    public async Task Username_and_display_name_are_refreshed_on_each_contact()
    {
        await _app.Send(Admin, "ข้าว 60");
        await _app.Handler.HandleAsync(new BotUpdate
        {
            UpdateId = 500, Kind = BotUpdateKind.Message, ChatId = Admin, Chat = ChatKind.Private,
            From = new BotSender(Admin, "newname", "ใหม่", null), MessageId = 500, Text = "/help",
        }, default);

        Assert.Equal("newname", _app.Users.Store[Admin].Username);
        Assert.Equal("ใหม่", _app.Users.Store[Admin].DisplayName);
    }

    // ---------- Registration & approval ----------

    [Fact]
    public async Task Start_creates_pending_user_and_notifies_admin_with_buttons()
    {
        await _app.Send(Mom, "/start");

        Assert.Equal(UserStatus.Pending, _app.Users.Store[Mom].Status);
        Assert.Equal(UserRole.Member, _app.Users.Store[Mom].Role);
        Assert.Equal("ส่งคำขอแล้ว รอผู้ดูแลอนุมัติ", _app.LastTo(Mom));

        var toAdmin = _app.Messenger.Sent.Single(s => s.ChatId == Admin);
        Assert.Contains("คุณ2002 (@user2002) ขอใช้งาน", toAdmin.Html);
        Assert.Equal(["approve:2002", "reject:2002"], toAdmin.Buttons!.Select(b => b.CallbackData));
    }

    [Fact]
    public async Task Pending_user_cannot_record()
    {
        await _app.Send(Mom, "/start");
        await _app.Send(Mom, "ข้าว 60");
        await _app.Send(Mom, "/today");

        Assert.Empty(_app.Transactions.Items);
        Assert.Equal("รอผู้ดูแลอนุมัติ ระหว่างนี้ยังบันทึกรายการไม่ได้", _app.LastTo(Mom));
    }

    [Fact]
    public async Task Admin_approves_via_callback()
    {
        await _app.Send(Mom, "/start");
        await _app.Callback(Admin, "approve:2002", messageId: 777);

        Assert.Equal(UserStatus.Active, _app.Users.Store[Mom].Status);
        Assert.Equal("อนุมัติแล้ว", _app.Messenger.CallbackAnswers.Single().Text);
        Assert.Equal((Admin, 777L, "✅ อนุมัติ คุณ2002 (@user2002) แล้ว"), _app.Messenger.Edits.Single());
        Assert.Contains("ผู้ดูแลอนุมัติแล้ว", _app.LastTo(Mom));

        await _app.Send(Mom, "ข้าว 60");
        Assert.Equal("🔴 #1 | อาหาร | -60 บาท, ข้าว", _app.LastTo(Mom));
    }

    [Fact]
    public async Task Admin_rejects_via_callback_and_user_becomes_blocked()
    {
        await _app.Send(Mom, "/start");
        await _app.Callback(Admin, "reject:2002");

        Assert.Equal(UserStatus.Blocked, _app.Users.Store[Mom].Status);
        Assert.Equal("ผู้ดูแลปฏิเสธคำขอใช้งาน", _app.LastTo(Mom));

        await _app.Send(Mom, "/start");
        Assert.Equal("บัญชีนี้ถูกระงับการใช้งาน", _app.LastTo(Mom));
        Assert.Single(_app.Messenger.Sent, s => s.ChatId == Admin); // no new request to admin
    }

    [Fact]
    public async Task Second_callback_on_same_request_is_already_decided()
    {
        await _app.Send(Mom, "/start");
        await _app.Callback(Admin, "approve:2002");
        await _app.Callback(Admin, "reject:2002");

        Assert.Equal(UserStatus.Active, _app.Users.Store[Mom].Status);
        Assert.Equal("คำขอนี้ถูกดำเนินการไปแล้ว", _app.Messenger.CallbackAnswers[^1].Text);
    }

    [Fact]
    public async Task Non_admin_cannot_approve()
    {
        await _app.AddActiveMember(Mom);
        await _app.Send(Kid, "/start");
        await _app.Callback(Mom, "approve:3003");

        Assert.Equal(UserStatus.Pending, _app.Users.Store[Kid].Status);
        Assert.Equal("เฉพาะผู้ดูแลเท่านั้น", _app.Messenger.CallbackAnswers[^1].Text);
    }

    // ---------- Commands ----------

    [Fact]
    public async Task Undo_soft_deletes_latest_own_transaction()
    {
        await _app.Send(Admin, "ข้าว 60");
        await _app.Send(Admin, "กาแฟ 45");
        await _app.Send(Admin, "/undo");

        Assert.Equal("ลบแล้ว: 🔴 #2 | อาหาร | -45 บาท, กาแฟ", _app.LastTo(Admin));
        Assert.True(_app.Transactions.Items.Single(t => t.Seq == 2).IsDeleted);
        Assert.Equal(2, _app.Transactions.Items.Count); // soft delete keeps the document

        await _app.Send(Admin, "/undo");
        await _app.Send(Admin, "/undo");
        Assert.Equal("ยังไม่มีรายการให้ลบ", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Delete_by_seq_only_touches_own_transactions()
    {
        await _app.AddActiveMember(Mom);
        await _app.Send(Admin, "ข้าว 60");   // admin #1
        await _app.Send(Mom, "ค่าไฟ 900");   // mom #1

        await _app.Send(Mom, "/delete 1");
        Assert.Equal("ลบแล้ว: 🔴 #1 | บิล | -900 บาท, ค่าไฟ", _app.LastTo(Mom));
        Assert.False(_app.Transactions.Items.Single(t => t.UserId == Admin).IsDeleted);

        await _app.Send(Mom, "/delete 1");
        Assert.Equal("ไม่พบรายการ #1", _app.LastTo(Mom));

        await _app.Send(Mom, "/delete abc");
        Assert.StartsWith("วิธีใช้:", _app.LastTo(Mom));
    }

    [Fact]
    public async Task List_shows_last_10_oldest_first_excluding_deleted()
    {
        for (var i = 1; i <= 12; i++)
            await _app.Send(Admin, $"ข้าว {i}");
        await _app.Send(Admin, "/delete 12");
        await _app.Send(Admin, "/list");

        var lines = _app.LastTo(Admin).Split('\n');
        Assert.Equal("<b>10 รายการล่าสุด</b>", lines[0]);
        Assert.Equal(11, lines.Length);
        Assert.StartsWith("🔴 #2 |", lines[1]);
        Assert.StartsWith("🔴 #11 |", lines[^1]);
    }

    [Fact]
    public async Task Summary_only_includes_own_data()
    {
        await _app.AddActiveMember(Mom);
        await _app.Send(Admin, "+เงินเดือน 50000");
        await _app.Send(Mom, "ข้าว 100");

        await _app.Send(Mom, "/today");
        var text = _app.LastTo(Mom);
        Assert.Contains("• อาหาร -100", text);
        Assert.DoesNotContain("50,000", text);
        Assert.Contains("🔴 <b>คงเหลือ</b> -100 บาท", text);
    }

    [Fact]
    public async Task Summary_commands_reply()
    {
        await _app.Send(Admin, "ข้าว 60");
        await _app.Send(Admin, "/week");
        Assert.StartsWith("<b>สรุปสัปดาห์นี้ (16–22 พ.ย. 2026)</b>", _app.LastTo(Admin));

        await _app.Send(Admin, "/month");
        Assert.StartsWith("<b>สรุปเดือนพฤศจิกายน 2026</b>", _app.LastTo(Admin));

        await _app.Send(Admin, "/month 2026-10");
        Assert.Equal("<b>สรุปเดือนตุลาคม 2026</b>\nยังไม่มีรายการ", _app.LastTo(Admin));

        await _app.Send(Admin, "/month 2026-13");
        Assert.StartsWith("วิธีใช้:", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Help_lists_admin_commands_only_for_admin()
    {
        await _app.AddActiveMember(Mom);
        await _app.Send(Admin, "/help");
        Assert.Contains("/addkeyword", _app.LastTo(Admin));
        Assert.Contains("อาหาร, เดินทาง, บิล, ของใช้, ช้อปปิ้ง, สุขภาพ, บันเทิง, อื่นๆ", _app.LastTo(Admin));
        Assert.Contains("เงินเดือน, รายได้เสริม, อื่นๆ", _app.LastTo(Admin));

        await _app.Send(Mom, "/help@ExpenseBot");
        Assert.DoesNotContain("/addkeyword", _app.LastTo(Mom));
    }

    [Fact]
    public async Task Unknown_command()
    {
        await _app.Send(Admin, "/foo");
        Assert.Equal("ไม่รู้จักคำสั่งนี้ พิมพ์ /help เพื่อดูคำสั่งทั้งหมด", _app.LastTo(Admin));
    }

    // ---------- Admin commands ----------

    [Fact]
    public async Task Member_cannot_use_admin_commands()
    {
        await _app.AddActiveMember(Mom);
        foreach (var cmd in new[] { "/users", "/block 1001", "/unblock 1001", "/addkeyword อาหาร ส้มตำปู" })
        {
            await _app.Send(Mom, cmd);
            Assert.Equal("คำสั่งนี้สำหรับผู้ดูแลเท่านั้น", _app.LastTo(Mom));
        }
    }

    [Fact]
    public async Task Users_lists_status_without_financial_data()
    {
        await _app.AddActiveMember(Mom);
        await _app.Send(Kid, "/start");
        await _app.Send(Mom, "ข้าว 999");

        await _app.Send(Admin, "/users");
        var text = _app.LastTo(Admin);
        Assert.Contains("<code>2002</code> — ใช้งาน", text);
        Assert.Contains("<code>3003</code> — รออนุมัติ", text);
        Assert.Contains("(admin)", text);
        Assert.DoesNotContain("999", text);
    }

    [Fact]
    public async Task Unblock_restores_access_and_notifies_user()
    {
        await _app.Send(Mom, "/start");
        await _app.Callback(Admin, "reject:2002");
        Assert.Equal(UserStatus.Blocked, _app.Users.Store[Mom].Status);

        await _app.Send(Admin, "/unblock 2002");
        Assert.Equal("คืนสิทธิ์ คุณ2002 (@user2002) แล้ว", _app.LastTo(Admin));
        Assert.Equal(UserStatus.Active, _app.Users.Store[Mom].Status);
        Assert.Equal("ผู้ดูแลคืนสิทธิ์ใช้งานแล้ว พิมพ์ /help เพื่อดูวิธีใช้", _app.LastTo(Mom));

        await _app.Send(Mom, "ข้าว 60");
        Assert.StartsWith("🔴 #1 | อาหาร | -60 บาท", _app.LastTo(Mom));

        await _app.Send(Admin, "/unblock 2002");
        Assert.Equal("คุณ2002 (@user2002) ไม่ได้ถูกระงับ", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Unblock_rejects_pending_unknown_and_bad_input()
    {
        await _app.Send(Kid, "/start");

        await _app.Send(Admin, "/unblock 3003");
        Assert.Equal("คุณ3003 (@user3003) ยังรออนุมัติ ใช้ปุ่มอนุมัติในคำขอแทน", _app.LastTo(Admin));
        Assert.Equal(UserStatus.Pending, _app.Users.Store[Kid].Status);

        await _app.Send(Admin, "/unblock 424242");
        Assert.Equal("ไม่พบผู้ใช้ ID <code>424242</code>", _app.LastTo(Admin));

        await _app.Send(Admin, "/unblock abc");
        Assert.StartsWith("วิธีใช้: /unblock &lt;id&gt;", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Block_user()
    {
        await _app.AddActiveMember(Mom);
        await _app.Send(Admin, "/block 2002");
        Assert.Equal("ระงับสิทธิ์ คุณ2002 (@user2002) แล้ว", _app.LastTo(Admin));

        await _app.Send(Mom, "ข้าว 60");
        Assert.Equal("บัญชีนี้ถูกระงับการใช้งาน", _app.LastTo(Mom));

        await _app.Send(Admin, "/block 1001");
        Assert.Equal("ระงับสิทธิ์ผู้ดูแลหลักไม่ได้", _app.LastTo(Admin));

        await _app.Send(Admin, "/block 424242");
        Assert.Equal("ไม่พบผู้ใช้ ID <code>424242</code>", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Addkeyword_updates_db_and_clears_cache()
    {
        await _app.Send(Admin, "หวย 80");
        Assert.Contains("| อื่นๆ |", _app.LastTo(Admin));

        await _app.Send(Admin, "/addkeyword บันเทิง หวย");
        Assert.Equal("เพิ่ม keyword \"หวย\" ให้หมวด บันเทิง แล้ว", _app.LastTo(Admin));
        Assert.Contains("หวย", _app.CategoryRepo.Items.Single(c => c.Id == "entertainment").Keywords);

        await _app.Send(Admin, "หวย 80");
        Assert.Contains("| บันเทิง |", _app.LastTo(Admin));

        await _app.Send(Admin, "/addkeyword บันเทิง หวย");
        Assert.Contains("มี keyword", _app.LastTo(Admin));

        await _app.Send(Admin, "/addkeyword ไม่มีหมวดนี้ x");
        Assert.StartsWith("ไม่พบหมวด", _app.LastTo(Admin));

        await _app.Send(Admin, "/addkeyword อื่นๆ x");
        Assert.StartsWith("ไม่พบหมวด", _app.LastTo(Admin));
    }

    // ---------- Edited messages ----------

    [Fact]
    public async Task Edited_message_updates_transaction()
    {
        await _app.Send(Admin, "ซื้อข้าวกลางวัน 60", messageId: 10);
        await _app.Edit(Admin, 10, "ซื้อข้าวกลางวัน 600");

        var t = Assert.Single(_app.Transactions.Items);
        Assert.Equal(600m, t.Amount);
        Assert.Equal(1, t.Seq);
        Assert.Equal("ซื้อข้าวกลางวัน 600", t.RawText);
        Assert.Equal("แก้แล้ว: 🔴 #1 | อาหาร | -600 บาท, ซื้อข้าวกลางวัน", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Unparsable_edit_keeps_original()
    {
        await _app.Send(Admin, "ข้าว 60", messageId: 10);
        await _app.Edit(Admin, 10, "ข้าว");

        Assert.Equal(60m, _app.Transactions.Items.Single().Amount);
        Assert.Equal("แก้ไม่สำเร็จ: ไม่พบจำนวนเงินในข้อความใหม่ รายการเดิมยังอยู่\n🔴 #1 | อาหาร | -60 บาท, ข้าว",
            _app.LastTo(Admin));
    }

    [Fact]
    public async Task Editing_a_message_that_had_no_amount_records_it()
    {
        await _app.Send(Admin, "ข้าว", messageId: 10);
        await _app.Edit(Admin, 10, "ข้าว 60");

        Assert.Equal("🔴 #1 | อาหาร | -60 บาท, ข้าว", _app.LastTo(Admin));
    }

    [Fact]
    public async Task Editing_a_deleted_transaction_does_not_resurrect_it()
    {
        await _app.Send(Admin, "ข้าว 60", messageId: 10);
        await _app.Send(Admin, "/undo");
        var sent = _app.Messenger.Sent.Count;

        await _app.Edit(Admin, 10, "ข้าว 70");

        Assert.True(_app.Transactions.Items.Single().IsDeleted);
        Assert.Equal(60m, _app.Transactions.Items.Single().Amount);
        Assert.Equal(sent, _app.Messenger.Sent.Count);
    }
}
