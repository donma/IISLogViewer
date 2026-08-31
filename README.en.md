# IIS Log Explorer

[繁體中文](README.md)

## Purpose

IIS Log Explorer is a Windows x64 portable tool for querying and analyzing IIS W3C logs. It allows operations and security teams to inspect IIS logs without installing IIS, the .NET Runtime, ELK, or a separate database.

Original IIS logs are opened read-only. The SQLite index database is stored next to the application.

## Main Features

- IIS site, folder, and single `.log` file sources
- Streaming W3C parser with dynamic `#Fields` mapping
- SQLite incremental indexing and raw/indexed hybrid search
- IP, error, slow request, traffic, and heuristic security analysis
- Realtime log monitor
- CSV / JSON export
- Portable, self-contained Windows x64 application
- Classic Win32-style interface

## Running

Run directly:

```text
RELEASE\IISLogExplorer-v.1.xxxxxxxx.exe
```

You can also run `publish\IISLogExplorer.exe`.

Sample data is included in `publish\sample-logs\`. It can be used to test the application on a computer without IIS.

## Test Summary

- Release tests: 51/51 passed
- Verified against real public IIS W3C samples
- Verified custom fields, missing timestamps, malformed timestamps, short lines, and large byte values
- Verified real-format samples containing SQL injection, XSS, and path traversal indicators
- Verified Raw Search, Index, Indexed Search, and security analysis with 20,000 and 100,000 IIS-format records
- Portable self-contained x64 GUI startup verified

## Technology

- .NET 10
- WPF
- SQLite / Microsoft.Data.Sqlite
- MVVM + Service Layer
