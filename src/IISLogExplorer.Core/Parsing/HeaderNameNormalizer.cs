namespace IISLogExplorer.Core.Parsing;

/// <summary>
/// 統一 Header 欄位名的正規化規則。
/// W3cFieldMap 與 ClientIpResolver 共用，避免 cs(...)/sc(...) 包裝形式對不上設定值。
/// </summary>
internal static class HeaderNameNormalizer
{
    public static string Normalize(string value)
    {
        return value
            .Replace("cs(", "", StringComparison.OrdinalIgnoreCase)
            .Replace("sc(", "", StringComparison.OrdinalIgnoreCase)
            .Replace(")", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}