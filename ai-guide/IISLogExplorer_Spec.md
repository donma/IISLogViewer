# IIS Log Explorer - .NET 10 Portable GUI 完整施工規格書

## 1. 專案定位

IIS Log Explorer 是一套 Windows 專用、以 .NET 10 製作的 Portable GUI 工具，目標是讓工程師、維運人員、資安人員在不安裝 ELK、Splunk、SQL Server、Agent 或額外 Runtime 的情況下，直接查詢與分析 IIS W3C Log。

核心原則：

- Windows only
- .NET 10
- WPF Desktop GUI
- self-contained portable 發佈
- 使用者電腦不需要安裝 .NET 10 Runtime
- 程式啟動後不得主動掃描 IIS Log
- 程式啟動後不得主動建立索引
- 程式啟動後不得主動執行資安分析
- 所有重工作均由使用者操作後才開始
- 第一次查詢時優先快速顯示原始 Log 搜尋結果
- 同時間背景建立 SQLite 索引
- 後續搜尋優先使用 SQLite
- SQLite Database 必須與 exe 位於同一層級
- 支援 IIS 自動偵測與手動選擇 Log Folder / 單一 Log File
- 不要求使用者學 SQL、Lucene、KQL 或自訂查詢語言
- 搜尋體驗採 Google 式搜尋 + GUI Filter
- 不假設 Log 總量上限
- 不可一次將所有 Log 載入記憶體

---

# 2. 專案暫定名稱

專案名稱：

```text
IISLogExplorer
```

Assembly：

```text
IISLogExplorer.exe
```

預設資料庫：

```text
IISLogExplorer.db
```

Portable 目錄範例：

```text
IISLogExplorer/
├─ IISLogExplorer.exe
├─ IISLogExplorer.db
├─ appsettings.json
├─ Logs/
└─ Temp/
```

禁止將資料預設寫到：

```text
%AppData%
%LocalAppData%
ProgramData
Registry
```

除非 Windows 本身必要，應盡可能維持單一 Portable Folder 可搬移。

---

# 3. 技術選型

## 3.1 Runtime

```text
.NET 10
TargetFramework: net10.0-windows
Architecture: x64
```

## 3.2 GUI

採用：

```text
WPF
```

原因：

- Windows only 符合 IIS 使用場景
- DataGrid 虛擬化成熟
- 適合大量資料呈現
- Background Task 與 MVVM 整合成熟
- 比 WebView / Blazor Hybrid 更適合大量資料列
- 比 WinForms 更容易建立現代化 GUI

## 3.3 Database

```text
SQLite
Microsoft.Data.Sqlite
```

禁止預設引入 ORM 執行大型 Log 寫入。

大量 Log Import 應優先直接使用：

```text
Microsoft.Data.Sqlite
DbCommand
Transaction
Prepared Statement
```

EF Core 可完全不使用。

## 3.4 架構

採用：

```text
MVVM + Service Layer
```

不需要過度 DDD。

重點是：

```text
UI
 ↓
ViewModel
 ↓
Application Service
 ↓
Parser / Search / Index / Analyzer
 ↓
SQLite / Raw Log
```

---

# 4. 發佈規格

必須支援 Portable Self-contained。

推薦 publish：

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

第一版不強制 Single File。

理由：

- SQLite native dependency 較穩定
- 啟動速度較穩定
- 降低單檔解壓與 Native Library 問題
- Portable Folder 對此工具已足夠

未來可額外提供：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

但不作為第一版硬性需求。

---

# 5. UX 核心原則

## 5.1 啟動時絕對禁止的行為

程式啟動後不得：

- 自動遞迴搜尋 C:\inetpub\logs
- 自動載入全部 W3SVC
- 自動讀取所有 Log
- 自動建立 SQLite Index
- 自動分析 IP
- 自動分析攻擊行為
- 自動產生 Dashboard
- 自動監聽 FileSystemWatcher
- 自動大量寫入 SQLite

啟動目標：

```text
快速開啟 GUI
低 CPU
低 Memory
低 Disk IO
```

## 5.2 第一次畫面

主畫面應顯示：

```text
尚未選擇 IIS Log

[選擇 IIS 站台]
[選擇 Log 資料夾]
[選擇 Log 檔案]
```

若本機存在 IIS，可提供：

```text
偵測 IIS 站台
```

但只有使用者點擊後才執行。

---

# 6. Log 來源

必須支援三種來源。

## 6.1 IIS Site

使用者點擊：

```text
選擇 IIS 站台
```

才讀取本機 IIS Configuration。

列出：

```text
Site Name
Site ID
Bindings
Log Directory
Enabled
```

例如：

```text
Default Web Site
Site ID: 1
Log: C:\inetpub\logs\LogFiles\W3SVC1
```

## 6.2 Folder

使用者可直接選：

```text
C:\inetpub\logs\LogFiles\W3SVC1
D:\Backup\IIS\2026-08
```

只掃描：

```text
*.log
```

是否包含子資料夾預設：

```text
false
```

UI 提供 checkbox：

```text
包含子資料夾
```

## 6.3 Single File

使用者可直接開啟：

```text
u_ex260828.log
```

這對分析客戶提供的 IIS Log 非常重要。

---

# 7. IIS W3C Log Parser

## 7.1 不可假設固定欄位順序

IIS W3C Log 格式包含：

```text
#Fields:
```

例如：

```text
#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken
```

Parser 必須根據每個檔案的 `#Fields:` 建立 Mapping。

禁止使用固定 Index：

```csharp
parts[8]
parts[9]
```

必須採：

```csharp
FieldDefinition
FieldIndexMap
```

## 7.2 Comment Line

以下全部視為 Header / Metadata：

```text
#Software:
#Version:
#Date:
#Fields:
```

不可當 Request Record。

## 7.3 Encoding

第一版支援：

```text
UTF-8
ANSI / Windows Default
```

若 UTF-8 decode 失敗，可 fallback。

## 7.4 Parser 必須使用 Streaming

禁止：

```csharp
File.ReadAllLines()
File.ReadAllText()
```

應使用：

```csharp
FileStream
StreamReader
ReadLineAsync()
```

或更高效的自訂 Buffered Reader。

## 7.5 File Sharing

IIS 正在寫入 Log 時仍必須可以讀取。

FileStream：

```text
FileShare.ReadWrite | FileShare.Delete
```

避免 IIS log rotation 時鎖住。

---

# 8. 標準 Log Model

建立：

```csharp
LogEntry
```

至少包含：

```text
Id
SourceId
FileId
LineNumber
TimestampUtc
TimestampLocal
ServerIp
Method
UriStem
UriQuery
ServerPort
Username
ClientIp
UserAgent
Referer
StatusCode
SubStatusCode
Win32Status
TimeTakenMs
BytesSent
BytesReceived
Host
ProtocolVersion
Cookie
ForwardedFor
RealClientIp
RawLine
```

所有 IIS 欄位不一定存在，因此大部分欄位必須 nullable。

例如：

```csharp
public int? StatusCode { get; init; }
```

不要使用假值：

```text
0.0.0.0
UNKNOWN
-
```

缺值就是 NULL。

---

# 9. Client IP Resolution

建立：

```csharp
ClientIpResolver
```

如果 Log 有 Custom Fields，依設定優先級判斷真正來源 IP。

預設候選：

```text
CF-Connecting-IP
True-Client-IP
X-Forwarded-For
X-Real-IP
cnd-src-ip
c-ip
```

注意：

`X-Forwarded-For` 可能是：

```text
1.2.3.4, 10.0.0.1
```

預設取第一個合法 IP。

最終產生：

```text
ResolvedClientIp
```

但必須保留原始：

```text
ClientIp
ForwardedFor
```

不可覆蓋原始資料。

---

# 10. 搜尋設計

搜尋包含兩種操作模式：

```text
Google 式 Keyword Search
+
GUI Filters
```

不提供進階 DSL。

## 10.1 Search Box

使用者可以輸入：

```text
192.168.1.1
```

可能匹配：

```text
ClientIp
ResolvedClientIp
RawLine
```

輸入：

```text
web.config
```

可能匹配：

```text
UriStem
UriQuery
RawLine
```

輸入：

```text
404
```

優先判斷為 StatusCode。

輸入：

```text
Chrome
```

搜尋：

```text
UserAgent
RawLine
```

## 10.2 Keyword Intent Detection

建立：

```csharp
SearchIntentDetector
```

判斷順序：

```text
IP Address
HTTP Status
HTTP Method
URL-like
General Keyword
```

例如：

```text
1.2.3.4 => IP
404 => Status
POST => Method
/api/order => URI
Mozilla => General Keyword
```

但任何判斷都不能阻止 Raw Text Match。

---

# 11. GUI Filters

右側或搜尋列下方提供 Filters。

至少：

```text
Date From
Date To
Time From
Time To
HTTP Method
HTTP Status
Client IP
URL Contains
User Agent Contains
Minimum Time Taken
Maximum Time Taken
Username
```

快捷日期：

```text
最近 15 分鐘
最近 1 小時
今天
昨天
最近 24 小時
最近 7 天
自訂
```

注意：

如果分析的是歷史 Log，不可把「今天」硬套成 Log 日期。

---

# 12. Hybrid Search 核心流程

這是本專案最重要的設計之一。

## 12.1 第一次 Search

假設：

```text
Folder 有 50 GB Logs
SQLite 尚未 Index
```

使用者按 Search 後：

```text
Search Request
      ↓
確認索引狀態
      ↓
沒有完整 Index
      ↓
RawLogSearchService 開始 Streaming Scan
      ↓
找到資料立刻分批回傳 UI
      ↓
同時 Background Indexer 建立 SQLite Index
```

使用者不需要等待 Index 完成才看到資料。

## 12.2 Index 完成後

後續查詢：

```text
SQLite Search
```

不再掃所有原始檔。

## 12.3 Partial Index

如果只完成 40%：

```text
Indexed Files => SQLite
Unindexed Files => Raw Scan
```

最後 Merge Result。

必須避免 Duplicate。

Unique Key 建議：

```text
FileId + LineNumber
```

---

# 13. Background Indexer

Indexer 僅在使用者做 Search / Analyze 後才允許啟動。

## 13.1 Cancellation

必須支援：

```csharp
CancellationToken
```

使用者可按：

```text
停止
```

停止後：

- 已完成 Batch 必須保留
- 不得 rollback 全部 index
- DB 必須維持正常狀態

## 13.2 Batch Insert

不可每一筆 Commit。

推薦：

```text
1000 - 5000 rows / transaction
```

實際值做 Constants / Settings。

## 13.3 Index Priority

第一優先 Index：

```text
目前 Search 時間區間內的 Log
```

例如使用者搜尋：

```text
2026-08-28
```

不要從 2024 年開始 Index。

## 13.4 File Order

如果沒有 Date Filter：

```text
Newest File First
```

讓近期資料最快可查。

---

# 14. SQLite Schema

## 14.1 Sources

```sql
CREATE TABLE Sources (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceType INTEGER NOT NULL,
    DisplayName TEXT NOT NULL,
    Path TEXT NOT NULL,
    IncludeSubfolders INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    LastUsedAt TEXT NULL
);
```

SourceType：

```text
1 IIS Site
2 Folder
3 File
```

## 14.2 LogFiles

```sql
CREATE TABLE LogFiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceId INTEGER NOT NULL,
    FullPath TEXT NOT NULL,
    FileName TEXT NOT NULL,
    FileSize INTEGER NOT NULL,
    LastWriteUtc TEXT NOT NULL,
    IndexedLength INTEGER NOT NULL DEFAULT 0,
    IndexedLineCount INTEGER NOT NULL DEFAULT 0,
    IsFullyIndexed INTEGER NOT NULL DEFAULT 0,
    HeaderHash TEXT NULL,
    FileFingerprint TEXT NULL,
    LastIndexedAt TEXT NULL,
    UNIQUE(SourceId, FullPath)
);
```

## 14.3 LogEntries

```sql
CREATE TABLE LogEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceId INTEGER NOT NULL,
    FileId INTEGER NOT NULL,
    LineNumber INTEGER NOT NULL,
    TimestampUtc TEXT NULL,
    TimestampLocal TEXT NULL,
    ServerIp TEXT NULL,
    Method TEXT NULL,
    UriStem TEXT NULL,
    UriQuery TEXT NULL,
    ServerPort INTEGER NULL,
    Username TEXT NULL,
    ClientIp TEXT NULL,
    ResolvedClientIp TEXT NULL,
    UserAgent TEXT NULL,
    Referer TEXT NULL,
    StatusCode INTEGER NULL,
    SubStatusCode INTEGER NULL,
    Win32Status INTEGER NULL,
    TimeTakenMs INTEGER NULL,
    Host TEXT NULL,
    ForwardedFor TEXT NULL,
    RawLine TEXT NULL,
    UNIQUE(FileId, LineNumber)
);
```

---

# 15. SQLite Indexes

必要：

```sql
CREATE INDEX IX_LogEntries_TimestampUtc
ON LogEntries(TimestampUtc);

CREATE INDEX IX_LogEntries_ClientIp
ON LogEntries(ClientIp);

CREATE INDEX IX_LogEntries_ResolvedClientIp
ON LogEntries(ResolvedClientIp);

CREATE INDEX IX_LogEntries_StatusCode
ON LogEntries(StatusCode);

CREATE INDEX IX_LogEntries_UriStem
ON LogEntries(UriStem);

CREATE INDEX IX_LogEntries_TimeTaken
ON LogEntries(TimeTakenMs);

CREATE INDEX IX_LogEntries_Source_Timestamp
ON LogEntries(SourceId, TimestampUtc);

CREATE INDEX IX_LogEntries_File_Line
ON LogEntries(FileId, LineNumber);
```

不應該無腦替所有欄位建立 Index。

---

# 16. Full Text Search

可以使用 SQLite FTS5。

建立：

```sql
CREATE VIRTUAL TABLE LogEntriesFts USING fts5(
    UriStem,
    UriQuery,
    UserAgent,
    Referer,
    RawLine,
    content='LogEntries',
    content_rowid='Id'
);
```

若 FTS5 在某環境不可用，必須 fallback 到普通 LIKE 搜尋。

程式不可直接 crash。

---

# 17. Search Result Streaming

搜尋不得等全部結果完成才顯示。

流程：

```text
Search
↓
拿到前 100 筆
↓
UI 顯示
↓
繼續 Search
↓
Batch Append
```

預設 Page Size：

```text
200
```

GUI 可提供：

```text
100
200
500
1000
```

不提供：

```text
全部
```

避免 UI Freeze。

---

# 18. WPF DataGrid

必須開啟：

```xml
EnableRowVirtualization="True"
EnableColumnVirtualization="True"
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
```

禁止將數十萬筆直接放 ObservableCollection。

建立：

```text
PagedResultCollection
VirtualizedCollection
```

或同等方案。

---

# 19. 主畫面 UI

建議布局：

```text
┌─────────────────────────────────────────────────────────────────┐
│ IIS Log Explorer                              Source: W3SVC1     │
├─────────────────────────────────────────────────────────────────┤
│ [Search................................] [搜尋] [停止]           │
│ [日期▼] [Status▼] [Method▼] [IP] [URL] [更多篩選]              │
├──────────────┬──────────────────────────────────────────────────┤
│ Search       │ Time       Status Method IP        URL          │
│ IP Analysis  │ ...                                             │
│ Errors       │                                                 │
│ Security     │                                                 │
│ Slow Request │                                                 │
│ Traffic      │                                                 │
├──────────────┴──────────────────────────────────────────────────┤
│ 搜尋 12,438 筆 | 已索引 61% | Background indexing... [停止]    │
└─────────────────────────────────────────────────────────────────┘
```

---

# 20. Navigation 功能

第一版包含：

```text
Search
IP Analysis
Errors
Security
Slow Requests
Traffic
Settings
```

但上述頁面全部為 User-triggered。

切到頁面不代表立刻分析。

例如 Security 顯示：

```text
尚未執行分析
[開始分析]
```

---

# 21. Search Result Columns

預設顯示：

```text
Time
Status
Method
Resolved Client IP
URL
Time Taken
User Agent
```

可以自訂顯示欄位。

更多欄位：

```text
Server IP
Client IP
Username
QueryString
Referer
SubStatus
Win32Status
Source File
Line Number
```

---

# 22. Request Detail

雙擊 Row 或右側 Detail Panel。

顯示：

```text
Timestamp
Method
URL
Query String
Status
SubStatus
Win32Status
Time Taken
Client IP
Resolved IP
Server IP
Username
User Agent
Referer
Source File
Line Number
Raw Log
```

提供 Copy Button：

```text
Copy URL
Copy IP
Copy Raw Line
Copy JSON
```

---

# 23. IP Analysis

使用者選 IP 後點擊：

```text
分析 IP
```

才開始分析。

結果：

```text
IP
First Seen
Last Seen
Request Count
Unique URLs
404 Count
500 Count
Average Time
User Agents
Top URLs
Methods
Status Distribution
```

## 23.1 IP Timeline

必須提供時間線：

```text
17:31:01 GET /                     200
17:31:02 GET /robots.txt           200
17:31:03 GET /.env                 404
17:31:03 GET /.git/config          404
17:31:04 GET /web.config           404
```

這是核心功能。

---

# 24. Error Analysis

支援：

```text
4xx
5xx
```

快捷：

```text
404
401
403
500
502
503
```

統計：

```text
Top Error URLs
Top Error IPs
Error Timeline
Status Distribution
```

必須能點統計直接 Drill Down 到 Requests。

---

# 25. Slow Request Analysis

使用者自行設定 Threshold。

預設：

```text
1000 ms
```

顯示：

```text
Top Slow URLs
Average Duration
P95
P99
Max
Request Count
```

第一版 P95 / P99 可以在選定範圍 Query 後程式計算。

不得每次程式啟動都預算。

---

# 26. Security Analyzer

只有使用者點擊：

```text
開始分析
```

才執行。

## 26.1 Scanner Path Rules

內建：

```text
/.env
/.git/config
/.git/HEAD
/web.config
/appsettings.json
/appsettings.Development.json
/phpinfo.php
/wp-admin
/wp-login.php
/server-status
/actuator
/actuator/env
/swagger
/swagger/index.html
```

## 26.2 Path Traversal

Pattern：

```text
../
..\
%2e%2e
%252e
%2f
%5c
```

## 26.3 SQL Injection Indicators

僅作 Indicator，不宣稱一定是攻擊。

例如：

```text
union select
information_schema
sleep(
benchmark(
waitfor delay
xp_cmdshell
```

## 26.4 XSS Indicators

```text
<script
javascript:
onerror=
onload=
%3cscript
```

## 26.5 Suspicious HTTP Methods

例如：

```text
TRACE
TRACK
CONNECT
PROPFIND
PUT
DELETE
OPTIONS
```

不能單純因 OPTIONS 就判攻擊。

只提高 Risk Score。

---

# 27. Security Risk Score

不能單一 Pattern 就直接 HIGH。

建立：

```text
SecurityScore
0 - 100
```

建議：

```text
Sensitive Path Hit       +15
Multiple Sensitive Paths +20
404 Ratio > 80%          +15
> 100 URLs / 5 min       +20
Traversal Pattern        +25
SQLi Pattern             +20
XSS Pattern              +20
Known Browser UA         -5
Successful Normal Pages  -10
```

Risk：

```text
0-24   Low
25-49  Medium
50-74  High
75-100 Critical
```

此分數必須標註：

```text
Heuristic
```

不可宣稱是百分之百攻擊判定。

---

# 28. Scanner Detection

例：

```text
IP: 45.x.x.x
Duration: 4m21s
Requests: 312
Unique URLs: 278
404: 96%
Sensitive Paths: 12
```

顯示：

```text
Likely Scanner
Risk: High
```

理由必須可讀：

```text
大量不同 URL
極高 404 比例
短時間集中請求
命中多個敏感檔案路徑
```

不能只顯示 AI 黑箱結果。

---

# 29. Traffic Analysis

User-triggered。

顯示：

```text
Total Requests
Unique IPs
Requests / Minute
Top URLs
Top IPs
Top User Agents
Status Distribution
```

Trend：

```text
minute
hour
Day
```

依時間範圍自動選粒度。

---

# 30. Realtime Monitor

預設：

```text
OFF
```

使用者開啟後：

```text
Realtime Monitor ON
```

才啟動 FileSystemWatcher / Polling。

## 30.1 行為

只追蹤：

```text
目前選擇的 Source
```

不得掃全部 IIS。

## 30.2 Incremental Read

記住：

```text
FilePath
LastPosition
LastLine
```

只讀新增 bytes。

不得整個重新讀。

## 30.3 Rotation

如果 IIS 建立新的：

```text
u_ex260829.log
```

Realtime Mode 應切換/追加追蹤新檔。

---

# 31. SQLite 增量索引

每個 LogFile 記錄：

```text
FileSize
LastWriteUtc
IndexedLength
IndexedLineCount
Fingerprint
```

重新開程式後：

如果：

```text
FileSize > IndexedLength
```

代表可能追加。

只從 IndexedLength 後繼續。

如果：

```text
FileSize < IndexedLength
```

視為：

```text
File replaced / truncated
```

需要重新判定 fingerprint。

---

# 32. File Fingerprint

不可只靠 FileName。

Fingerprint 建議：

```text
FullPath
FileSize
LastWriteUtc
First 4KB Hash
```

不需要 Hash 全檔，避免大型檔成本。

---

# 33. SQLite Location

資料庫路徑：

```csharp
Path.Combine(AppContext.BaseDirectory, "IISLogExplorer.db")
```

不得使用 CurrentDirectory。

因為 CurrentDirectory 可能被啟動方式改變。

---

# 34. Database Cleanup

Settings：

```text
保留全部
30 天
60 天
90 天
180 天
自訂
```

但清理不得在程式 Startup 自動執行大型 DELETE。

使用者可設定：

```text
允許閒置時清理
```

預設：

```text
OFF
```

也提供：

```text
立即清理
```

---

# 35. Database Maintenance

提供：

```text
Database Size
Indexed Records
Indexed Files
Last Indexed
```

Buttons：

```text
Optimize
VACUUM
Clear Index
Rebuild Index
```

所有 destructive 操作需 Confirm。

---

# 36. SQLite Pragmas

建議：

```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA temp_store=MEMORY;
PRAGMA cache_size=-32768;
PRAGMA busy_timeout=5000;
```

實際 cache size 可設定。

不要把 cache 設得過大。

工具可能跑在 IIS Server 本機，不能搶光 Memory。

---

# 37. Concurrency

建立全域 Job 控制：

```text
最多 1 個大型 Index Job
最多 1 個 Search Job
最多 1 個 Analyzer Job
```

不要讓使用者連點 Search 產生 10 個全 Disk Scan。

新搜尋時：

```text
取消舊搜尋
```

Indexer 可以選擇繼續或降低 Priority。

---

# 38. Process Priority

背景 Indexing：

```text
Low / Below Normal Priority
```

至少在應用層面限制：

```text
Batch
Yield
Cancellation
```

不要讓 IIS Server 因分析工具造成 IO Spike。

---

# 39. Memory Budget

設計目標：

空閒：

```text
< 150 MB
```

一般搜尋：

```text
< 300 MB
```

超大量搜尋仍不得與 Log Size 成正比成長。

禁止保留整份原始 Log 在 Memory。

---

# 40. Performance 目標

非硬體保證，但作為開發驗收目標。

## 啟動

```text
Cold Start < 3 sec
```

## 第一次 Raw Search

對大型檔：

```text
必須在找到第一批 Match 後立刻顯示
```

不能等整份掃完。

## Indexed Search

常見 Query：

```text
IP
Status
Date Range
URI
```

目標：

```text
一般情況 < 1 sec 看到第一頁
```

---

# 41. Cancellation UI

任何超過 1 秒可能持續的操作，都應有：

```text
Cancel / Stop
```

包含：

```text
Raw Search
Index
Security Analysis
Traffic Analysis
IP Analysis
Export
```

---

# 42. Progress UI

Status Bar 顯示：

```text
Searching u_ex260828.log...
Found 1,280 records
Index 34%
```

如果總進度未知：

```text
Indeterminate Progress
```

禁止 UI 無反應。

---

# 43. Export

搜尋結果可以 Export：

```text
CSV
JSON
```

第一版不需要 Excel dependency。

Export 預設：

```text
Current Filter Result
```

但大量結果要再次確認：

```text
將匯出 2,351,233 筆資料，是否繼續？
```

Streaming Export，不可一次載入全部。

---

# 44. Settings

需要：

```text
Default Page Size
Index Batch Size
Database Retention
Max Search Results
Realtime Refresh Interval
Slow Request Threshold
Theme
Client IP Header Priority
```

Config 儲存：

```text
settings.json
```

同樣位於 exe folder。

---

# 45. Theme

第一版至少：

```text
Dark
Light
System
```

預設建議：

```text
Dark
```

介面定位：

```text
工程工具
乾淨
高資訊密度
不要過度動畫
```

---

# 46. Exception Handling

全域：

```text
DispatcherUnhandledException
TaskScheduler.UnobservedTaskException
AppDomain.UnhandledException
```

但不可只 Catch 然後吞掉。

記錄：

```text
Logs/app-yyyyMMdd.log
```

---

# 47. App Log

自己的程式 Log 不要與 IIS Log 混淆。

例如：

```text
Logs/IISLogExplorer-20260828.log
```

Retention：

```text
7 days
```

---

# 48. Read-only 原則

對 IIS Logs：

```text
永遠 Read Only
```

工具不得：

- 修改 IIS Log
- Rename IIS Log
- Delete IIS Log
- Lock IIS Log

SQLite Index 可以清除，但原始 Log 絕對不可動。

---

# 49. Permission Handling

如果 IIS Log Folder 需要 Administrator：

不要強制整個程式 always-admin。

顯示：

```text
沒有讀取此資料夾的權限
```

再提示：

```text
請以系統管理員身分重新啟動
```

第一版不用自動 Elevation。

---

# 50. 專案資料夾結構

推薦：

```text
IISLogExplorer.sln

src/
  IISLogExplorer.App/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs

    Views/
      SearchView.xaml
      IpAnalysisView.xaml
      ErrorAnalysisView.xaml
      SecurityView.xaml
      SlowRequestView.xaml
      TrafficView.xaml
      SettingsView.xaml

    ViewModels/
      MainViewModel.cs
      SearchViewModel.cs
      IpAnalysisViewModel.cs
      ErrorAnalysisViewModel.cs
      SecurityViewModel.cs
      SlowRequestViewModel.cs
      TrafficViewModel.cs
      SettingsViewModel.cs

  IISLogExplorer.Core/
    Models/
      LogEntry.cs
      LogSource.cs
      LogFileInfo.cs
      SearchRequest.cs
      SearchResult.cs
      SearchFilter.cs
      SecurityFinding.cs
      IpAnalysisResult.cs

    Parsing/
      IIisLogParser.cs
      IisW3cLogParser.cs
      FieldsHeaderParser.cs
      LogEntryMapper.cs

    Searching/
      ISearchService.cs
      HybridSearchService.cs
      RawLogSearchService.cs
      SqliteSearchService.cs
      SearchIntentDetector.cs

    Indexing/
      IIndexService.cs
      SqliteIndexService.cs
      IndexPlanner.cs
      FileFingerprintService.cs

    Analysis/
      IpAnalyzer.cs
      ErrorAnalyzer.cs
      SlowRequestAnalyzer.cs
      TrafficAnalyzer.cs

    Security/
      SecurityAnalyzer.cs
      SecurityRule.cs
      SecurityRuleEngine.cs
      SecurityScoreCalculator.cs
      Rules/

    IIS/
      IIisDiscoveryService.cs
      IisDiscoveryService.cs

    Networking/
      ClientIpResolver.cs

  IISLogExplorer.Infrastructure/
    Database/
      SqliteConnectionFactory.cs
      DatabaseInitializer.cs
      DatabaseMigrator.cs
      LogEntryRepository.cs
      LogFileRepository.cs
      SourceRepository.cs

    Files/
      LogFileScanner.cs
      IncrementalLogReader.cs
      RealtimeLogWatcher.cs

    Configuration/
      SettingsService.cs

    Logging/
      AppLogger.cs

tests/
  IISLogExplorer.Tests/
```

---

# 51. MVVM 規格

View Code Behind 只允許：

```text
UI-specific event
Window lifecycle
Focus
Drag / resize
```

禁止把 Search / SQLite / Parser 邏輯寫在 MainWindow.xaml.cs。

所有 command：

```text
AsyncCommand
```

至少：

```text
SearchCommand
CancelSearchCommand
SelectFolderCommand
SelectFileCommand
DetectIisCommand
AnalyzeIpCommand
StartSecurityAnalysisCommand
StartRealtimeCommand
StopRealtimeCommand
```

---

# 52. Async 規範

所有 IO：

```csharp
async / await
```

禁止 UI thread 上：

```text
File.ReadAllLines
SQLite 大 Query
大量 Regex
Log Parsing
```

CPU heavy 可：

```csharp
Task.Run
```

但要受 Job Coordinator 管理。

---

# 53. CancellationToken 規範

以下 public async 方法必須接受：

```csharp
CancellationToken cancellationToken
```

包含：

```text
SearchAsync
IndexAsync
AnalyzeAsync
ExportAsync
ScanFilesAsync
```

禁止自行 new CancellationTokenSource 深藏在 Service 中。

Cancellation 由 ViewModel / JobCoordinator 控制。

---

# 54. SearchRequest

建議：

```csharp
public sealed record SearchRequest
{
    public required LogSource Source { get; init; }
    public string? Keyword { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Method { get; init; }
    public int? StatusCode { get; init; }
    public string? ClientIp { get; init; }
    public string? UrlContains { get; init; }
    public string? UserAgentContains { get; init; }
    public int? MinTimeTakenMs { get; init; }
    public int? MaxTimeTakenMs { get; init; }
    public int PageSize { get; init; } = 200;
}
```

---

# 55. Search Result Batch

不要回傳：

```csharp
Task<List<LogEntry>>
```

大型搜尋推薦：

```csharp
IAsyncEnumerable<LogEntry>
```

或：

```text
Channel<LogEntry>
```

由 UI 分批接收。

---

# 56. Raw Search Optimization

Raw Search 在每行 Parse 前可做 Fast Reject。

例如 Keyword：

```text
web.config
```

先：

```csharp
line.Contains(keyword, StringComparison.OrdinalIgnoreCase)
```

不 Match 就不要完整 parse。

如果 Filter 是：

```text
Status=404
```

因 W3C 欄位位置已知，可快速抽取 Status。

目標：

```text
避免每一行建立大量 string/object
```

---

# 57. Allocation Optimization

大量解析路徑避免：

```text
string.Split(' ')
LINQ in hot loop
Regex for every line
```

可逐步優化使用：

```text
Span<char>
ReadOnlySpan<char>
ArrayPool<T>
```

第一版先確保正確性，但 Parser API 必須允許後續優化。

---

# 58. Regex 規範

Security Rules 可以 Regex，但：

```text
Compiled Regex
Timeout
```

例如：

```csharp
new Regex(pattern,
    RegexOptions.Compiled | RegexOptions.IgnoreCase,
    TimeSpan.FromMilliseconds(100));
```

避免 ReDoS。

---

# 59. IIS Discovery

可使用：

```text
Microsoft.Web.Administration
```

但必須封裝在：

```text
IisDiscoveryService
```

若機器沒有 IIS / DLL 不可用：

UI 仍可正常啟動。

只是：

```text
IIS 自動偵測不可用
```

手動 Folder / File 必須仍然可用。

---

# 60. Source History

可以保存最近使用來源：

```text
最近 10 個
```

例如：

```text
Default Web Site
D:\Logs\CustomerA
u_ex260825.log
```

但程式啟動只顯示 History，不自動打開與掃描。

---

# 61. Status Bar

永遠有狀態列：

```text
Source
Search State
Index State
DB Size
Result Count
```

例如：

```text
W3SVC1 | 12,431 Results | Index 68% | DB 1.8 GB
```

---

# 62. Index 狀態

每個 Source 顯示：

```text
Not Indexed
Partial
Indexed
Outdated
```

Outdated 定義：

```text
Log File changed after LastIndexedAt
```

---

# 63. 搜尋一致性

如果 Index 正在建立，而 Log 又持續增加：

Search Result 必須標示：

```text
Indexing in progress
Results may continue to increase
```

不要讓使用者誤以為結果是完整 Snapshot。

---

# 64. Security Analyzer 結果 Model

```csharp
public sealed record SecurityFinding
{
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required SecuritySeverity Severity { get; init; }
    public required string Reason { get; init; }
    public string? ClientIp { get; init; }
    public string? Uri { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public long? LogEntryId { get; init; }
}
```

---

# 65. Security Rules 必須 Data-driven

不要寫成：

```csharp
if (url.Contains("/.env")) ...
if (url.Contains("web.config")) ...
```

建立：

```text
security-rules.json
```

例如：

```json
{
  "id": "SENSITIVE_ENV",
  "category": "SensitiveFileProbe",
  "pattern": "/.env",
  "match": "contains",
  "score": 15,
  "enabled": true
}
```

讓未來可以擴充。

---

# 66. Security 頁面

至少分：

```text
Overview
Suspicious IPs
Sensitive Paths
Injection Indicators
Scanner Activity
```

每個 Finding 都可：

```text
View Requests
Analyze IP
Copy IP
```

---

# 67. False Positive 原則

UI 文案禁止：

```text
這個 IP 是駭客
已遭 SQL Injection
```

應顯示：

```text
疑似掃描行為
SQL Injection Indicator
Potential Path Traversal
```

Log 本身只能提供證據，不一定代表 exploit 成功。

---

# 68. Dashboard 設計修正

本工具不做 Startup Dashboard。

如果未搜尋：

```text
No analysis yet
```

Traffic / Security / Errors 都必須 User Trigger。

避免一開程式就：

```text
掃 300 GB Log 算圖表
```

---

# 69. 多來源設計

第一版一次只 Active 一個 Source。

不要一開始做：

```text
跨 20 個 IIS Server Aggregate Search
```

但 Database Schema 保留 SourceId，未來可擴展。

---

# 70. Duplicate Prevention

SQLite：

```text
UNIQUE(FileId, LineNumber)
```

Import 使用：

```sql
INSERT OR IGNORE
```

但如果檔案 Fingerprint 改變，必須重新建立 File identity。

---

# 71. Time Zone

IIS W3C 標準 log 通常使用 UTC。

系統必須：

```text
保留 UTC
顯示 Local Time
```

Settings 可以：

```text
Display Time Zone:
Local
UTC
```

不要直接把原值丟失。

---

# 72. URL Display

顯示 URL：

```text
UriStem + ? + UriQuery
```

但如果 Query：

```text
-
```

不要顯示：

```text
?-
```

---

# 73. Sensitive Data

Log 可能有：

```text
Cookie
Authorization Query Token
Email
Username
```

第一版不自動 Mask 原始資料，因為這是 Debug 工具。

但 Export 可以提供：

```text
Mask Query Values
Mask Client IP
```

作為 optional。

---

# 74. File Change Handling

如果 Search 過程中 File 被 IIS Rotation：

```text
Deleted/Renamed
```

Reader 不可 crash 整個 Search。

處理：

```text
完成目前可讀內容
記錄 warning
繼續下一個 file
```

---

# 75. Corrupt Line Handling

遇到無法解析的單行：

不要終止檔案。

記錄：

```text
Parse Error Count
File
LineNumber
RawLine Preview
```

並繼續下一行。

---

# 76. Header Change Handling

同一檔案如果再次出現：

```text
#Fields:
```

Parser 必須更新 Field Mapping。

不能假設 Header 只出現一次。

---

# 77. Testing Strategy

至少建立：

```text
Parser Tests
Search Tests
Index Tests
Security Rule Tests
IP Resolver Tests
Incremental Reader Tests
```

---

# 78. Parser Test Cases

至少：

```text
標準 W3C
缺 Query
缺 Username
自訂欄位
不同 #Fields 順序
Header 中途改變
Malformed line
Unicode URL
超長 User-Agent
正在追加中的 File
```

---

# 79. Hybrid Search Test

情境：

```text
File A indexed
File B 50% indexed
File C not indexed
```

Search 必須：

```text
A => SQLite
B indexed portion => SQLite
B remaining => Raw
C => Raw
```

最後結果：

```text
No duplicates
Correct count
```

---

# 80. Incremental Index Test

第一次：

```text
1000 lines
```

Index。

之後 file append：

```text
500 lines
```

第二次 Index：

```text
只能新增 500
```

不可重新 insert 前 1000。

---

# 81. Performance Test Fixture

建立 synthetic IIS log generator。

能生成：

```text
100K
1M
10M
```

records。

用來測：

```text
Parser throughput
Index throughput
Search latency
Memory
```

Generator 放在 test project，不放 production exe。

---

# 82. 第一階段施工順序

OSS120B 必須按照此順序施工。

## Phase 1 - Solution Skeleton

完成：

```text
Solution
Projects
DI
Main Window
Navigation
Settings
Logging
```

驗收：

```text
可 compile
可 launch
portable publish 成功
```

## Phase 2 - Parser

完成：

```text
W3C Header Parser
Streaming Reader
LogEntry Mapper
Parser Tests
```

驗收：

```text
可解析不同欄位順序
```

## Phase 3 - Source Selection

完成：

```text
Select File
Select Folder
Detect IIS
Recent Sources
```

驗收：

```text
不選來源時不掃描
```

## Phase 4 - Raw Search

完成：

```text
Keyword
Filters
Streaming Results
Cancel
Virtualized Grid
```

驗收：

```text
未建立 DB 也能搜尋
第一批結果不用等完整掃描
```

## Phase 5 - SQLite

完成：

```text
DB schema
Repository
Batch insert
File index state
Indexes
```

## Phase 6 - Hybrid Search

完成：

```text
Partial index strategy
Raw + SQLite merge
Duplicate prevention
```

## Phase 7 - Realtime

完成：

```text
Start / Stop
Incremental tail
Rotation
```

## Phase 8 - Analysis

完成：

```text
IP
Errors
Slow Request
Traffic
```

## Phase 9 - Security

完成：

```text
Rule Engine
Scoring
Scanner Detection
UI
```

## Phase 10 - Export / Maintenance

完成：

```text
CSV
JSON
DB retention
Rebuild
Optimize
```

---

# 83. AI 施工規則

給 OSS120B / Coding Agent 時必須遵守。

## 禁止一次生成整套

每次 Phase 完成：

```text
dotnet build
```

必須 0 error 才進下一 Phase。

## 每個 Phase

AI 必須：

1. 先閱讀此規格
2. 檢查目前 existing code
3. 不重建已完成功能
4. 不任意改 public contract
5. 實作
6. build
7. tests
8. 修正
9. 回報完成檔案

---

# 84. Coding Style

```text
C# latest available for .NET 10
Nullable enabled
ImplicitUsings enabled
File-scoped namespace
Async suffix
CancellationToken
Dependency Injection
```

禁止：

```text
大量 static global state
God Class
MainWindow 2000 lines
Service Locator
async void
```

除 WPF event handler 外禁止 async void。

---

# 85. Class Size 原則

建議單一 class：

```text
< 500 lines
```

超過需評估拆分。

Parser / Analyzer 不可全部塞進：

```text
LogService.cs
```

---

# 86. Dependency Injection

使用：

```text
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Hosting
```

App startup 建立 Host。

例如：

```text
Services
ViewModels
Repositories
Settings
Logging
```

全部由 DI 管理。

---

# 87. NuGet 原則

盡量少 dependency。

建議：

```text
Microsoft.Data.Sqlite
Microsoft.Extensions.Hosting
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Configuration.Json
Microsoft.Extensions.Logging
```

MVVM 可：

```text
CommunityToolkit.Mvvm
```

避免引入巨大 UI Framework，除非有實際必要。

---

# 88. UI Responsiveness 驗收

在：

```text
Indexing
Raw scanning
Exporting
Analyzing
```

期間：

```text
Window 可以拖動
Filter 可以操作
Cancel 可點擊
Progress 有更新
```

如果 UI Freeze 超過 500ms 視為問題。

---

# 89. Empty State

任何頁面沒資料都要有 Empty State。

例如 Search：

```text
尚未搜尋
```

Security：

```text
尚未執行資安分析
```

不要顯示空白表格讓使用者猜。

---

# 90. Search Zero Result

顯示：

```text
找不到符合條件的 Request
```

同時顯示：

```text
搜尋過 14 個 Log File
共掃描 3,241,302 行
```

讓使用者知道真的有執行。

---

# 91. Source Missing

如果使用者按 Search 但沒有 Source：

顯示 dialog / inline：

```text
尚未選擇 IIS Log 來源

[選擇 IIS 站台]
[選擇資料夾]
[選擇 Log 檔案]
```

不可自動替使用者掃 C:。

---

# 92. First Search Experience

這是最高優先 UX 驗收項目。

假設：

```text
使用者第一次開一個 20GB Log Folder
搜尋 web.config
```

正確體驗：

```text
1. 按搜尋
2. UI 顯示 Searching...
3. 從最新檔開始 Streaming Scan
4. 找到第一筆立刻出現在 Grid
5. 搜尋持續
6. Background Index 啟動
7. Status bar 顯示 Index Progress
8. 使用者可以停止 Search 或 Index
```

錯誤體驗：

```text
按搜尋
Loading 10 分鐘
Index 完才顯示第一筆
```

此行為禁止。

---

# 93. Index Job 不得阻塞搜尋

Search Priority > Index Priority。

如果 Disk IO 壓力大：

```text
降低 Index batch
```

而不是拖慢 Search。

---

# 94. Security Analysis Scope

Security Analysis 必須套用目前 Filter Scope。

例如：

```text
Date = Today
```

按開始分析只分析 Today。

提供：

```text
Analyze Current Filter
Analyze Entire Source
```

Entire Source 需要明確按鈕，不能預設。

---

# 95. IP Analysis Scope

從 Search Result 點 IP：

```text
Analyze this IP
```

預設使用目前 Date Filter。

避免不小心掃十年歷史資料。

---

# 96. Request Context View

非常建議第一版加入。

在某筆 Request 上：

```text
View Context
```

顯示：

```text
前 30 秒
此 Request
後 30 秒
```

或：

```text
前後 50 Requests
```

這對 production debug 非常有用。

---

# 97. Status/SubStatus 顯示

不要只顯示：

```text
404
```

如果有 substatus：

```text
404.2
```

Detail 再顯示：

```text
sc-status = 404
sc-substatus = 2
```

---

# 98. Win32 Status

如果：

```text
sc-win32-status != 0
```

Detail 顯示並可提供 Windows Error Description。

Description lookup 可以 lazy 執行。

不要對每筆預計算。

---

# 99. URI Decode

Raw Data 必須保留。

UI 可切換：

```text
Raw URL
Decoded URL
```

Decoder 發生 malformed escape 時：

```text
顯示 raw
```

不可 crash。

---

# 100. User Agent

可提供簡單分類：

```text
Browser
Bot
CLI
Unknown
```

第一版不要引入巨大 UA database。

可以 Rule-based lazy 判斷。

---

# 101. Future Extension Interfaces

預留：

```text
ILogParser
ISearchProvider
IAnalyzer
ISecurityRule
ILogSourceProvider
```

未來可以支援：

```text
Nginx
Apache
ASP.NET Core stdout
Cloudflare Log
```

但第一版只做 IIS W3C。

---

# 102. 第一版不做的功能

明確排除：

```text
ELK integration
Splunk integration
Remote Agent
Server Service
Windows Service
Cloud Sync
User Account
Multi-user
Web Server
AI Chat
Automatic Blocking
Firewall Rule Modification
Automatic IP Ban
IIS Configuration Modification
```

此工具是：

```text
Read + Search + Analyze
```

不是主動防禦系統。

---

# 103. AI 功能未來版

未來可以加：

```text
Analyze selected IP with AI
Explain error cluster
Summarize suspicious behavior
```

但 AI 不直接吃完整 Log。

應先由程式產生 structured summary：

```json
{
  "ip": "1.2.3.4",
  "requests": 312,
  "uniqueUrls": 278,
  "404Rate": 0.96,
  "topPaths": [],
  "findings": []
}
```

再交給模型。

第一版完全不需要 AI。

---

# 104. Definition of Done

第一版完成必須同時符合：

- .NET 10 WPF
- Windows x64
- self-contained portable
- Target PC 不需安裝 .NET
- SQLite 與 exe 同層
- Startup 無主動 Log Scan
- Startup 無主動 Index
- 支援 IIS Site 選擇
- 支援 Folder
- 支援 Single Log File
- W3C Dynamic Fields Parser
- Streaming Raw Search
- 搜尋第一批結果立即顯示
- Background Incremental Index
- Hybrid Raw + SQLite Search
- 搜尋可 Cancel
- Index 可 Cancel
- Realtime 可手動開關
- DataGrid Virtualization
- IP Timeline
- Error Analysis
- Slow Request Analysis
- Traffic Analysis
- Security Analyzer
- Security Heuristic Score
- CSV / JSON Export
- Database retention
- Database rebuild
- 不修改原始 IIS Log
- malformed log 不造成程式 crash
- build 0 errors
- tests pass

---

# 105. 最終產品使用流程

正常使用：

```text
開啟 IISLogExplorer.exe
        ↓
程式什麼都不掃
        ↓
使用者選 IIS Site / Folder / File
        ↓
輸入：web.config
        ↓
按 Search
        ↓
Raw Log Streaming Search
        ↓
第一批結果立即出現
        ↓
Background SQLite Index
        ↓
使用者繼續查看結果
        ↓
未來 Search 自動走 SQLite
```

資安情境：

```text
搜尋 404
↓
看到某個 IP 很異常
↓
Analyze IP
↓
IP Timeline
↓
Security Analysis
↓
看到 /.env / web.config / .git/config 掃描
↓
判斷 Likely Scanner
```

維運情境：

```text
Filter Status = 500
↓
Filter Last 1 Hour
↓
Top Error URLs
↓
點 /api/order
↓
View Requests
↓
View Context
↓
找出異常發生時間與 Client IP
```

效能情境：

```text
Slow Requests
↓
Threshold = 1000ms
↓
找 Top Slow URLs
↓
查看 P95/P99
↓
Drill Down
```

---

# 106. 給 OSS120B 的最重要施工提示

這個專案最容易施工失敗的地方不是 UI，而是以下五點：

```text
1. 不要一次 ReadAllLines
2. 不要等 SQLite Index 完成才顯示 Search
3. 不要把幾十萬筆 Result 一次放進 ObservableCollection
4. 不要假設 IIS #Fields 固定
5. 不要在 Startup 自動做大量工作
```

任何施工結果只要違反其中一項，都應視為架構錯誤，而不是小 Bug。

最重要的核心體驗只有一句話：

```text
打開工具很輕，按下搜尋才開始工作，而且即使第一次面對超大 IIS Log，也要盡快把第一批答案顯示給使用者。
```

