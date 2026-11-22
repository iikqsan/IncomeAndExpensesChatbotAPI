# iikQsan Money Bot: จดรายรับ-รายจ่ายผ่าน Telegram

## 1. สร้าง bot กับ BotFather

1. เปิดแชทกับ [@BotFather](https://t.me/BotFather) แล้วพิมพ์ `/newbot`
2. ตั้งชื่อที่แสดง และ username ที่ลงท้ายด้วย `bot` เช่น `my_home_expense_bot`
3. BotFather จะให้ **token** รูปแบบ `123456789:AA...` เก็บเป็นความลับ ห้าม commit
4. (แนะนำ) `/setjoingroups` → เลือก bot → **Disable** เพื่อไม่ให้ใครดึงบอทเข้ากลุ่มได้ ถ้าถูกดึงเข้าไป บอทก็จะออกเองอยู่แล้ว
5. (ไม่บังคับ) `/setcommands` แล้ววางรายการนี้ เพื่อให้มีเมนูคำสั่ง
   ```
   help - วิธีใช้และหมวดที่มี
   today - สรุปวันนี้
   week - สรุปสัปดาห์นี้
   month - สรุปรอบเงินเดือน หรือ /month 2026-10
   list - 10 รายการล่าสุด
   undo - ลบรายการล่าสุด
   delete - ลบรายการ เช่น /delete 42
   ```
6. หา **Telegram user ID ของตัวเอง** (เป็นตัวเลข) สำหรับ `Bot:AdminUserId` เช่น ทักไปที่ [@userinfobot](https://t.me/userinfobot)

## 2. ตั้งค่า

| Key | ความหมาย |
| --- | --- |
| `Telegram:BotToken` | token จาก BotFather (**secret**) |
| `Telegram:Mode` | `Polling` (dev) หรือ `Webhook` (production) |
| `Telegram:WebhookUrl` | URL สาธารณะแบบ https ที่ลงท้ายด้วย `/telegram/webhook` (โหมด Webhook) |
| `Telegram:WebhookSecret` | สตริงสุ่ม 1–256 ตัว ใช้ได้เฉพาะ `A-Z a-z 0-9 _ -` (โหมด Webhook, **secret**) |
| `Mongo:ConnectionString` | เช่น `mongodb://localhost:27017` หรือ URI ของ Atlas (**secret**) |
| `Mongo:Database` | ค่าเริ่มต้น `expensebot` |
| `Bot:AdminUserId` | Telegram user ID ของผู้ดูแลคนแรก |
| `Bot:DefaultTimezone` | ค่าเริ่มต้น `Asia/Bangkok` |

`appsettings.json` มีแค่ชื่อ key ส่วนค่าจริงให้ใส่ด้วยวิธีใดวิธีหนึ่งด้านล่าง ดูตัวอย่างได้ที่ `src/ExpenseBot.Api/appsettings.Example.json` และ `.env.example`

**รันบนเครื่อง: User Secrets**
```bash
cd src/ExpenseBot.Api
dotnet user-secrets set "Telegram:BotToken" "123456789:AA..."
dotnet user-secrets set "Mongo:ConnectionString" "mongodb://localhost:27017"
dotnet user-secrets set "Bot:AdminUserId" "123456789"
```

**Docker / server: environment variables** (ใช้ `__` แทน `:`)
```bash
cp .env.example .env   # แล้วกรอกค่า .env ถูก ignore ไว้แล้ว ไม่ถูก commit
```

ถ้าค่าที่จำเป็นหายหรือผิด (เช่น ไม่มี token หรือใช้โหมด Webhook แต่ URL ไม่ใช่ https หรือ secret ผิดรูปแบบ) แอปจะหยุดตั้งแต่ตอน startup พร้อมบอกว่าค่าไหนผิด

## 3. รัน

**แบบ dev: MongoDB ใน Docker + API บนเครื่อง (polling)**
```bash
docker compose up -d mongo
dotnet run --project src/ExpenseBot.Api
```
แล้วทักบอทในแชทส่วนตัว ด้วยบัญชี admin พิมพ์ `/help` แล้วลอง `ข้าวกลางวัน 60`

**แบบ container ทั้งหมด**
```bash
docker compose up -d --build        # API ที่ :8080 + MongoDB อ่านค่าจาก .env
docker compose logs -f api
```
ใน compose บรรทัด `Mongo__ConnectionString: mongodb://mongo:27017` จะทับค่าจาก `.env` ถ้าจะใช้ Atlas ให้ลบบรรทัดนั้น

**Test**
```bash
dotnet test
```

**Health check:** `GET /health` → `{"status":"ok"}`

## 4. สลับ Polling / Webhook

| | Polling | Webhook |
| --- | --- | --- |
| ใช้ตอน | dev บนเครื่อง | production |
| ต้องมี | ไม่ต้องมี URL สาธารณะ | HTTPS สาธารณะ พอร์ต 443, 80, 88 หรือ 8443 |
| ตอน startup | **ลบ** webhook ที่เคยตั้งไว้ แล้วเริ่ม `getUpdates` | **ตั้ง** webhook (URL + secret token) ลองใหม่จนสำเร็จ |

ทั้งสองแบบส่ง update เข้า `UpdateHandler` ตัวเดียวกัน

**เปลี่ยนเป็น Webhook**
```bash
Telegram__Mode=Webhook
Telegram__WebhookUrl=https://bot.example.com/telegram/webhook
Telegram__WebhookSecret=<สตริงสุ่ม เช่น จาก: openssl rand -hex 32>
```
- container ฟังที่พอร์ต **8080** แบบ HTTP ให้ HTTPS มาจาก platform (Railway, Render, Azure App Service) หรือ reverse proxy เช่น Caddy/nginx ที่ส่งต่อมาที่ `:8080`
- endpoint `POST /telegram/webhook` ตรวจ header `X-Telegram-Bot-Api-Secret-Token` ทุกครั้ง ถ้าไม่ตรงจะตอบ 401 ถ้าตรงจะเข้าคิวแล้วตอบ 200 ทันที โดยมี background worker ประมวลผลทีละรายการตามลำดับ
- ตรวจสถานะ webhook: `curl https://api.telegram.org/bot<TOKEN>/getWebhookInfo`

**กลับเป็น Polling:** ตั้ง `Telegram__Mode=Polling` แล้ว restart แอปจะลบ webhook ให้เอง

## 5. ดูข้อความที่ parse ไม่ได้

ข้อความที่หาจำนวนเงินไม่เจอถูก log ด้วย EventId `1001` (`UnparsableMessage`) ใช้ดูเพื่อปรับ parser และ keyword

ใน container (ตั้งไว้ใน `Dockerfile`) log เป็น JSON บรรทัดละหนึ่ง object และเขียนภาษาไทยตรงๆ:
```json
{"Timestamp":"2026-11-21T14:28:17.766Z","EventId":1001,"LogLevel":"Information","Category":"ExpenseBot.Application.Updates.UpdateHandler","Message":"Unparsable message from 42: สวัสดี","State":{"UserId":42,"Text":"สวัสดี"}}
```
```bash
docker compose logs --no-log-prefix api | grep '"EventId":1001'
# เอาเฉพาะข้อความ (ต้องมี jq)
docker compose logs --no-log-prefix api | grep '"EventId":1001' | jq -r '.State.Text'
```
บน platform (Railway, Render, Azure) ค้นด้วย field `EventId = 1001` ได้โดยตรง ถ้าอยากได้ log แบบอ่านง่ายตอน debug ให้ตั้ง `Logging__Console__FormatterName=simple` ส่วนตอน `dotnet run` บนเครื่องยังเป็นแบบ simple อยู่
จากนั้นเพิ่ม keyword ให้หมวดได้จากในแชท: `/addkeyword อาหาร ข้าวมันไก่` (เฉพาะ admin)

## 6. Backup ด้วย `mongodump`

Atlas M0 ไม่มี automated backup ให้ตั้ง `mongodump` รันเป็นระยะ ตัวอย่าง `backup.sh` (ใช้ image `mongo` จึงไม่ต้องติดตั้ง tools):

```bash
#!/usr/bin/env bash
# backup.sh: dump the expensebot database to ./backups and keep the last 30 days.
set -euo pipefail

MONGO_URI="${MONGO_URI:?set MONGO_URI, e.g. mongodb+srv://user:pass@cluster.mongodb.net}"
DB="${MONGO_DB:-expensebot}"
DIR="${BACKUP_DIR:-$(pwd)/backups}"
STAMP="$(date +%Y%m%d-%H%M%S)"

mkdir -p "$DIR"
docker run --rm -v "$DIR:/backups" mongo:8.0 \
  mongodump --uri="$MONGO_URI" --db="$DB" --gzip --archive="/backups/$DB-$STAMP.gz"

find "$DIR" -name "$DB-*.gz" -mtime +30 -delete
echo "Backup written to $DIR/$DB-$STAMP.gz"
```

ตั้ง cron ให้รันทุกวันตี 3:
```cron
0 3 * * * MONGO_URI='mongodb+srv://...' /opt/expensebot/backup.sh >> /var/log/expensebot-backup.log 2>&1
```

กู้คืน:
```bash
docker run --rm -v "$(pwd)/backups:/backups" mongo:8.0 \
  mongorestore --uri="$MONGO_URI" --gzip --archive=/backups/expensebot-20261121-030000.gz --nsInclude='expensebot.*'
```
ถ้าใช้ MongoDB จาก compose ให้ใช้ `MONGO_URI=mongodb://localhost:27017` (compose เปิดพอร์ตไว้เฉพาะ `127.0.0.1`)