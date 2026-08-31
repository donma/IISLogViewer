# IIS Log Explorer

[English](README.en.md)

## 專案目的

IIS Log Explorer 是 Windows x64 的 Portable IIS W3C Log 查詢與分析工具，讓維運與資安人員不需要安裝 IIS、.NET Runtime、ELK 或資料庫，就能直接開啟 IIS Log 進行搜尋與分析。

原始 IIS Log 僅以唯讀方式開啟；SQLite 索引資料庫會建立在程式所在目錄。

## 主要功能

- 支援 IIS Site、資料夾與單一 `.log` 檔案
- W3C `#Fields` 動態欄位解析與串流搜尋
- SQLite 增量索引與 Raw + Indexed Hybrid Search
- IP、錯誤、慢請求、流量與資安啟發式分析
- Realtime Log Monitor
- CSV / JSON 匯出
- Portable、Self-contained、Windows x64
- 經典 Win32 風格介面

## 執行方式

直接執行：

```text
RELEASE\IISLogExplorer-v.1.xxxxxxxx.exe
```

也可以直接執行 `publish\IISLogExplorer.exe`。

專案附有測試資料於 `publish\sample-logs\`，可在沒有 IIS 的電腦上選擇該資料夾進行測試。

## 測試摘要

- Release 測試：51/51 通過
- 通過真實公開 IIS W3C 樣本測試
- 通過自訂欄位、缺少時間、錯誤時間、短行與超大 bytes 測試
- 通過包含 SQLi、XSS、Path Traversal 的真實格式樣本
- 通過 20,000 與 100,000 筆真實 IIS 格式資料的 Raw Search、Index、Indexed Search 與資安分析
- Portable Self-contained x64 GUI 啟動驗證通過

## 技術

- .NET 10
- WPF
- SQLite / Microsoft.Data.Sqlite
- MVVM + Service Layer
