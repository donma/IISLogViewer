namespace IISLogExplorer.Core.Models;

public enum LogSourceType
{
    IisSite = 1,
    Folder = 2,
    File = 3
}

public enum IndexState
{
    NotIndexed,
    Partial,
    Indexed,
    Outdated
}

public enum SecuritySeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum SearchIntent
{
    IpAddress,
    HttpStatus,
    HttpMethod,
    Uri,
    GeneralKeyword
}

public enum ThemeMode
{
    Dark,
    Light,
    System
}
