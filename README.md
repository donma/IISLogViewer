# IIS Log Explorer

[English](README.en.md)

## 專案目的

IIS Log Explorer 是一套 **Windows x64 專用、免安裝** 的 IIS W3C Log 查詢與分析工具。工程師、維運與資安人員不需要安裝 IIS、.NET Runtime、ELK、SQL Server 或任何 Agent，就能直接開啟 IIS 日誌進行搜尋與分析。

原始 IIS Log **永遠以唯讀方式開啟**，不會被修改、重新命名或刪除；SQLite 索引資料庫會建立在程式所在目錄（`IISLogExplorer.db`）。

### 設計原則

- 啟動後**不做任何掃描**，所有重工作皆由使用者操作才開始
- 第一次搜尋先以串流掃描原始 Log，**第一筆結果立即顯示**，不等索引完成
- 同一時間背景建立 SQLite 增量索引，之後的搜尋自動改走索引
- 不將大量資料一次載入記憶體，採串流 + DataGrid 虛擬化
- 全程可取消（搜尋、索引、分析）

## 主要功能

| 功能 | 說明 |
| --- | --- |
| Log 來源 | IIS Site（自動偵測）、資料夾、單一 `.log` 檔案 |
| W3C Parser | 依每個檔案的 `#Fields:` 動態建立欄位對應，不假設固定欄位順序 |
| 搜尋 | Google 式關鍵字 + 篩選；關鍵字可為 IP、狀態碼、Method、URL 或一般文字 |
| 混合搜尋 | 未索引檔案走 Raw Scan，已索引檔案走 SQLite，最後合併並去除重複 |
| 增量索引 | 每個檔案記錄檔案大小、最後修改時間、已索引偏移與指紋，中斷後可續跑 |
| IP 分析 | IP 時間線、URL 統計、狀態碼分佈、404/500 計數 |
| 錯誤分析 | 4xx / 5xx 統計、Top Error URLs / IPs、狀態碼分佈 |
| 慢請求分析 | 自訂 Threshold，計算平均、P95、P99、最大值與 Top Slow URLs |
| 流量分析 | 總請求數、唯一 IP、每分鐘請求、Top URL/IP/UA、趨勢 |
| 資安分析 | 啟發式規則引擎 + 風險評分（Low/Medium/High/Critical）、Scanner 偵測 |
| Realtime | 手動開啟後以輪詢方式追蹤目前來源的新增內容，支援日誌輪替 |
| 匯出 | CSV / JSON（串流匯出） |
| 資料庫維護 | Optimize / VACUUM / Clear Index / Rebuild / Retention 清理 |
| 介面 | 經典 Win32 風格、深色可讀配色 |

## 系統需求

- Windows x64（Windows 10 / 11 / Server 2016 以上）
- **不需安裝 .NET Runtime**（Self-contained）
- 建議記憶體：512 MB 以上；大資料索引期間建議 2 GB

## 快速開始

1. 從 `RELEASE\` 取得最新的 `IISLogExplorer-v.1.xxxxxx.exe`
2. 雙擊執行（可放在任何資料夾；資料庫會建立在 exe 同層）
3. 選擇來源：
   - **選擇資料夾**：選一個含 `*.log` 的資料夾（可勾選「包含子資料夾」）
   - **選擇 Log 檔案**：選單一 `u_ex*.log`
   - **偵測 IIS**：讀取本機 `applicationHost.config` 列出站台
4. 在搜尋列輸入關鍵字（例如 `404`、`1.2.3.4`、`/api/order`）按下「搜尋」

> 沒有 IIS 的電腦也可以測試：`sample-logs\` 內含真實公開 IIS W3C 樣本與大型測試資料，直接「選擇資料夾」指向它即可。

## 搜尋與篩選

- **關鍵字**：Google 式全文比對（Client IP、URL、User-Agent、Method、Raw Line…）。純數字 100–599 視為狀態碼。
- **更多篩選**：Method、Status、Client IP、URL contains、User Agent、Username、Min/Max ms、Page size、Quick range、Date from/to、Time from/to (UTC)

### 快捷日期

最近 15 分鐘、最近 1 小時、今天、昨天、最近 24 小時、最近 7 天、自訂。

> 手動修改 Date from/to 會自動切回「自訂」，避免快捷範圍覆蓋你的設定。

## 資安分析說明

- 依 `security-rules.json` 的規則比對敏感路徑、Path Traversal、SQL Injection、XSS 與可疑 Method
- 風險分數為 **0–100 的啟發式指標**：`0-24 Low`、`25-49 Medium`、`50-74 High`、`75-100 Critical`
- 分數只是「Log 中的可疑指標」，**不代表攻擊成功**；請人工判斷
- 提供「分析目前篩選」與「分析整個來源」兩種範圍

## 設定

`settings.json` 位於 exe 同層，可設定：

- Default Page Size、Index Batch Size、Max Search Results
- Database Retention（保留全部 / 30 / 60 / 90 / 180 天）
- Realtime Refresh Interval
- Slow Request Threshold
- Client IP Header Priority（例如 `CF-Connecting-IP, X-Forwarded-For, c-ip`）

## 測試報告

### 自動化測試（Release）：80 / 80 通過

| 測試類別 | 內容 |
| --- | --- |
| Parser | 標準 W3C、不同欄位順序、缺欄位、Header 中途改變、Malformed line、Unicode URL、超長 User-Agent、Quoted 欄位 |
| Client IP Resolver | X-Forwarded-For 多 IP 取第一個、自訂優先級 |
| Repository | INSERT OR IGNORE 去重、狀態碼篩選 |
| 分析器 | IP / Errors / Slow Request / Traffic 聚合與百分位 |
| 資安 | 敏感路徑加分、正常流量低分、Scanner 啟發式偵測 |
| 整合 | Raw Search → Index → Indexed Search 一致性、中斷續跑、時間範圍、Partially indexed 去重與覆蓋 |
| 真實樣本 | 公開 IIS W3C 樣本（自訂欄位、缺失/錯誤時間、短行、超大 bytes） |
| 真實 Pipeline | 含 `sqlmap/1.5`、500、15000ms 慢請求的真實 `u_ex` 樣本 |
| 資安真實樣本 | 含 SQL Injection、XSS、Path Traversal 的真實攻擊樣本，以正式 34 條規則命中 |
| 大量資料 | 20,000 與 100,000 筆真實 IIS 格式資料的搜尋、索引、篩選與資安分析 |
| 效能 | 100,000 筆解析與索引在時間限制內完成 |
| v2 修正 | Retention、Incremental Header 持久化、Realtime partial line / line number / truncate、Hybrid 全域排序與 MaxResults、Progress 單調、Migration、並發讀取、全過濾器 parity |
| 最終修正 | 無 date/time Header 相容、Hybrid/Realtime 使用 persisted Header、自訂 Client IP header（`cs()` wrapper）normalize 統一、AdditionalFields 預設關閉、HeaderCache bounded、Parser exception 收窄 |

### v2 修正重點

- **Retention**：清理只刪過期資料，不再重設 checkpoint，避免被刪舊資料重新索引復活
- **Parser 效能**：W3cFieldMap 依 index 取值、span tokenizer、ArrayPool 讀行，一般流程不再為每筆建立欄位字典
- **Incremental Index**：`#Fields` 持久化到 SQLite，重啟後直接從 checkpoint 續跑，不重掃大檔；舊資料庫自動 migration
- **Hybrid Search**：移除全域鎖、全域 `TimestampUtc DESC` 排序（有界記憶體）、MaxResults 套用於排序後
- **Realtime**：初次定位到最後完整換行並接續行號，truncate / rotation 安全重置
- **架構**：IndexCoordinator 單一 writer、MainViewModel 拆分 Search / Index / Realtime、集中錯誤處理與診斷日誌

### 最終修正重點

- **Client IP Header Normalize 統一**：`CF-Connecting-IP` / `cs(CF-Connecting-IP)` / `cf_connecting_ip` 視為同一欄位（CDN / Proxy / Cloudflare / ARR 情境正確解析）
- **Hybrid / Realtime**：重啟後直接使用 DB 持久化的 `FieldsHeader`，未索引大檔不 full scan（tail backward scan）
- **AdditionalFields**：Index / Search / Realtime 預設不建立完整欄位字典，只 materialize Client IP resolver 需要的自訂 header
- **穩健性**：HeaderCache 上限 1024、Parser Map 只捕捉格式相關例外、Realtime DB lookup 失敗有 diagnostics

### 以真實資料發現並修正的問題

1. `date` 為 `-` 的行原本被整體丟棄，違反「缺值就是 NULL」；已修正為保留記錄、時間為空。
2. 正式資安規則缺少 boolean-based SQLi（`OR '1'='1`）樣式；已新增 `SQL_BOOLEAN` 規則。

### 手動驗證

- Portable Self-contained x64 GUI 啟動驗證通過
- `RELEASE\` 單一 exe（約 74 MB，內含 .NET 10 Runtime 與 SQLite native library）可獨立執行

## 技術架構

```
UI (WPF, MainWindow)
   ↓
MainViewModel (Commands, MVVM)
   ↓
Application Services
   ├─ HybridSearchService   (Raw + SQLite merge)
   ├─ SqliteIndexService    (背景增量索引)
   ├─ Analyzers             (IP / Errors / Slow / Traffic / Security)
   ├─ RealtimeLogWatcher    (輪詢 + rotation)
   └─ ExportService         (CSV / JSON)
   ↓
SQLite (Microsoft.Data.Sqlite, WAL) / Raw Log (唯讀)
```

### 專案結構

```
src/
  IISLogExplorer.Core/        領域模型、介面、Parser、Analyzer 介面
  IISLogExplorer.Infrastructure/  SQLite、搜尋、索引、檔案、IIS 偵測、設定
  IISLogExplorer.App/         WPF 介面、ViewModel、規則檔
tests/
  IISLogExplorer.Tests/       xUnit 測試（含真實樣本 fixtures）
```

## 從原始碼建置

```bash
dotnet build IISLogExplorer.slnx -c Release
dotnet test IISLogExplorer.slnx -c Release
```

發佈 Portable（多檔）：

```bash
dotnet publish src/IISLogExplorer.App/IISLogExplorer.App.csproj -c Release -r win-x64 --self-contained true
```

發佈單一 exe（含 native library 解壓）：

```bash
dotnet publish src/IISLogExplorer.App/IISLogExplorer.App.csproj -c Release -r win-x64 --self-contained true -o RELEASE -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## 授權

本專案目前未附正式授權檔案，保留所有權利。
