using System.Text;

namespace IISLogExplorer.Tests;

public static class TestHelpers
{
    private static readonly string[] ClientIps = ["198.51.100.10", "198.51.100.12", "203.0.113.5", "203.0.113.9", "45.66.230.10", "45.66.230.11", "185.220.101.2", "104.248.5.60", "66.249.72.231", "66.249.72.16"];
    private static readonly string[] UserAgents = ["Mozilla/5.0+(Windows+NT+10.0;+Win64;+x64)+AppleWebKit/537.36", "Mozilla/5.0+(compatible;+Googlebot/2.1;++http://www.google.com/bot.html)", "curl/7.0", "sqlmap/1.5#stable+(http://sqlmap.org)", "nikto/2.1.6", "python-requests/2.31.0"];
    private static readonly string[] Paths = ["/", "/index.aspx", "/api/order", "/api/login", "/web.config", "/.env", "/.git/config", "/wp-login.php", "/phpinfo.php", "/actuator/env", "/slow.aspx"];
    private static readonly string[] Queries = ["-", "id=1", "q=<script>alert(1)</script>", "path=../../../../windows/win.ini", "id=1'+OR+'1'='1"];
    private static readonly int[] Statuses = [200, 200, 200, 301, 404, 404, 500];

    public static string WriteSampleLog(string directory, string content, string fileName = "u_ex260828.log")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public static string SampleW3CLog(int records, string keyword = "/api/order")
    {
        var builder = new StringBuilder();
        builder.AppendLine("#Software: Microsoft Internet Information Services 10.0");
        builder.AppendLine("#Version: 1.0");
        builder.AppendLine("#Date: 2026-08-28 00:00:00");
        builder.AppendLine("#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken sc-bytes cs-bytes");
        for (var index = 0; index < records; index++)
        {
            var uri = index % 10 == 0 ? keyword : $"/page/{index}";
            builder.AppendLine($"2026-08-28 10:{(index / 60) % 60:00}:{index % 60:00} 10.0.0.1 {(index % 2 == 0 ? "GET" : "POST")} {uri} {(index % 3 == 0 ? "id=1" : "-")} 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - {(index % 100 == 0 ? 404 : 200)} 0 0 {index % 5000} 100 200");
        }

        return builder.ToString();
    }

    public static string RealisticW3CLog(int records, int seed = 20260828)
    {
        var random = new Random(seed);
        var builder = new StringBuilder();
        builder.AppendLine("#Software: Microsoft Internet Information Services 10.0");
        builder.AppendLine("#Version: 1.0");
        builder.AppendLine("#Date: 2026-08-28 00:00:00");
        builder.AppendLine("#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken");
        var baseTime = new DateTime(2026, 8, 28);
        for (var index = 0; index < records; index++)
        {
            var ts = baseTime.AddSeconds(index);
            var path = Paths[random.Next(Paths.Length)];
            var query = Queries[random.Next(Queries.Length)];
            var agent = UserAgents[random.Next(UserAgents.Length)];
            var client = ClientIps[random.Next(ClientIps.Length)];
            var status = Statuses[random.Next(Statuses.Length)];
            var taken = random.Next(0, 3000);
            builder.AppendLine($"{ts:yyyy-MM-dd HH:mm:ss} 10.0.0.1 GET {path} {query} 443 - {client} \"{agent}\" - {status} 0 0 {taken}");
        }

        return builder.ToString();
    }
}
