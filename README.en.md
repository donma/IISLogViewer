# IIS Log Explorer

[繁體中文](README.md)

## Purpose

IIS Log Explorer is a **Windows x64, no-install** tool for querying and analyzing IIS W3C logs. Engineers, operations, and security teams can open IIS logs for search and analysis without installing IIS, the .NET Runtime, ELK, SQL Server, or any agent.

Original IIS logs are **always opened read-only** — never modified, renamed, or deleted. The SQLite index database is stored next to the application (`IISLogExplorer.db`).

### Design Principles

- Performs **no scanning at startup**; all heavy work starts only on user action
- First search streams the raw logs — **the first batch of results appears immediately**, without waiting for indexing
- Background SQLite incremental indexing runs concurrently; subsequent searches automatically use the index
- Streaming + DataGrid virtualization; large data is never loaded fully into memory
- Cancellable search, indexing, and analysis

## Main Features

| Feature | Description |
| --- | --- |
| Log sources | IIS sites (auto-detect), folders, or a single `.log` file |
| W3C parser | Dynamic field mapping from each file's `#Fields:` — no fixed field order assumptions |
| Search | Google-style keyword + filters; keywords can be IPs, status codes, methods, URLs, or free text |
| Hybrid search | Unindexed files use raw scan, indexed files use SQLite, then merged with dedup |
| Incremental indexing | Per-file size/last-write/indexed-offset/fingerprint tracking; resumable after interruption |
| IP analysis | IP timeline, URL stats, status distribution, 404/500 counts |
| Error analysis | 4xx/5xx stats, top error URLs/IPs, status distribution |
| Slow request analysis | Custom threshold; average, P95, P99, max, top slow URLs |
| Traffic analysis | Total requests, unique IPs, requests/minute, top URL/IP/UA, trend |
| Security analysis | Heuristic rule engine + risk score (Low/Medium/High/Critical), scanner detection |
| Realtime | Polling-based tail for the current source, rotation-aware |
| Export | CSV / JSON (streaming) |
| DB maintenance | Optimize / VACUUM / Clear Index / Rebuild / retention cleanup |
| UI | Classic Win32 style with readable colors |

## Requirements

- Windows x64 (Windows 10 / 11 / Server 2016+)
- **No .NET Runtime required** (self-contained)
- Recommended memory: 512 MB+; 2 GB+ during large-scale indexing

## Quick Start

1. Grab the latest `IISLogExplorer-v.1.xxxxxx.exe` from `RELEASE\`
2. Double-click to run (place it anywhere; the database is created next to the exe)
3. Pick a source:
   - **Select Folder**: a folder containing `*.log` (optionally including subfolders)
   - **Select Log File**: a single `u_ex*.log`
   - **Detect IIS**: reads the local `applicationHost.config` to list sites
4. Type a keyword (e.g. `404`, `1.2.3.4`, `/api/order`) and press **Search**

> No IIS machine? Use `sample-logs\` — real public IIS W3C samples plus large test data. Just "Select Folder" and point to it.

## Search & Filters

- **Keyword**: Google-style full-text match (Client IP, URL, User-Agent, Method, Raw Line…). A pure number 100–599 is treated as a status code.
- **More filters**: Method, Status, Client IP, URL contains, User Agent, Username, Min/Max ms, Page size, Quick range, Date from/to, Time from/to (UTC)

### Quick Date Ranges

Last 15 minutes, last 1 hour, today, yesterday, last 24 hours, last 7 days, custom.

> Manually editing Date from/to automatically switches back to "Custom" so quick ranges don't override your settings.

## Security Analysis Notes

- Rules in `security-rules.json` match sensitive paths, path traversal, SQL injection, XSS, and suspicious methods
- Risk score is a **0–100 heuristic indicator**: `0-24 Low`, `25-49 Medium`, `50-74 High`, `75-100 Critical`
- The score only reflects suspicious indicators in the logs — **it does not prove an attack**; please verify manually
- Two scopes available: "Analyze Current Filter" and "Analyze Entire Source"

## Settings

`settings.json` sits next to the exe. Options include:

- Default Page Size, Index Batch Size, Max Search Results
- Database Retention (keep all / 30 / 60 / 90 / 180 days)
- Realtime Refresh Interval
- Slow Request Threshold
- Client IP Header Priority (e.g. `CF-Connecting-IP, X-Forwarded-For, c-ip`)

## Test Report

### Automated tests (Release): 51 / 51 passed

| Category | Contents |
| --- | --- |
| Parser | Standard W3C, different field order, missing fields, mid-file header change, malformed lines, Unicode URLs, long user agents, quoted fields |
| Client IP Resolver | X-Forwarded-For multi-IP (first valid), custom priority |
| Repository | INSERT OR IGNORE dedup, status filter |
| Analyzers | IP / Errors / Slow Request / Traffic aggregation and percentiles |
| Security | Sensitive path scoring, normal traffic stays low, scanner heuristic |
| Integration | Raw → index → indexed search consistency, cancel & resume, time range, partial-index dedup/coverage |
| Real samples | Public IIS W3C samples (custom fields, missing/malformed timestamps, short lines, large byte values) |
| Real pipeline | Real `u_ex` sample with `sqlmap/1.5`, 500, and a 15000ms slow request |
| Real security sample | Real attack sample with SQL injection, XSS, path traversal — matched by the production 34 rules |
| Large volume | Search, index, filter, and security analysis on 20,000 and 100,000 IIS-format records |
| Performance | Parsing and indexing 100,000 records within the time budget |

### Issues found with real data (now fixed)

1. Lines whose `date` is `-` were dropped entirely, violating "missing value = NULL"; fixed to keep the record with an empty timestamp.
2. The production security rules lacked a boolean-based SQLi pattern (`OR '1'='1`); a `SQL_BOOLEAN` rule was added.

### Manual verification

- Portable self-contained x64 GUI startup verified
- `RELEASE\` single exe (~74 MB, bundles .NET 10 Runtime and the SQLite native library) runs standalone

## Architecture

```
UI (WPF, MainWindow)
   ↓
MainViewModel (Commands, MVVM)
   ↓
Application Services
   ├─ HybridSearchService   (raw + SQLite merge)
   ├─ SqliteIndexService    (background incremental indexing)
   ├─ Analyzers             (IP / Errors / Slow / Traffic / Security)
   ├─ RealtimeLogWatcher    (polling + rotation)
   └─ ExportService         (CSV / JSON)
   ↓
SQLite (Microsoft.Data.Sqlite, WAL) / Raw Log (read-only)
```

### Project Layout

```
src/
  IISLogExplorer.Core/        domain models, interfaces, parser, analyzer contracts
  IISLogExplorer.Infrastructure/  SQLite, search, indexing, files, IIS discovery, settings
  IISLogExplorer.App/         WPF UI, view models, rule files
tests/
  IISLogExplorer.Tests/       xUnit tests (including real sample fixtures)
```

## Building from Source

```bash
dotnet build IISLogExplorer.slnx -c Release
dotnet test IISLogExplorer.slnx -c Release
```

Portable multi-file publish:

```bash
dotnet publish src/IISLogExplorer.App/IISLogExplorer.App.csproj -c Release -r win-x64 --self-contained true
```

Single-file publish (extracts native libraries at startup):

```bash
dotnet publish src/IISLogExplorer.App/IISLogExplorer.App.csproj -c Release -r win-x64 --self-contained true -o RELEASE -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## License

No formal license file is provided; all rights reserved.
