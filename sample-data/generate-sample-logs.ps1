# 產生「真實 IIS W3C 格式」的大型測試 Log。
# 欄位集、UA 字串、狀態碼分佈與掃描器路徑均以 sample-data 內的真實樣本為基準。
# 用法：
#   .\sample-data\generate-sample-logs.ps1 -OutDir "D:\temp\iis-logs" -RecordsPerFile 100000 -Files 3
param(
    [string]$OutDir = "D:\AI_PROJECTS\IISLogViewer\sample-data\large",
    [int]$RecordsPerFile = 100000,
    [int]$Files = 1
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$header = @(
    '#Software: Microsoft Internet Information Services 10.0',
    '#Version: 1.0',
    '#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken'
)

$ips = @('10.0.0.5','10.0.0.6','10.0.0.7','10.0.0.8','10.0.0.9','10.0.0.10','192.168.1.60','192.168.1.61')
$clients = @('198.51.100.10','198.51.100.12','203.0.113.5','203.0.113.9','45.66.230.10','45.66.230.11','185.220.101.2','104.248.5.60','66.249.72.231','66.249.72.16')
$agents = @('Mozilla/5.0+(Windows+NT+10.0;+Win64;+x64)+AppleWebKit/537.36','Mozilla/5.0+(compatible;+Googlebot/2.1;++http://www.google.com/bot.html)','Mozilla/5.0+(compatible;+bingbot/2.0;++http://www.bing.com/bingbot.htm)','curl/7.0','sqlmap/1.5#stable+(http://sqlmap.org)','nikto/2.1.6','Mozilla/5.0+(X11;+Linux+x86_64)+AppleWebKit/537.36+(KHTML,+like+Gecko)+Chrome/120.0','python-requests/2.31.0')
$paths = @('/','/index.aspx','/home.aspx','/style.css','/script.js','/api/order','/api/login','/api/status','/favicon.ico','/robots.txt','/admin','/slow.aspx','/web.config','/.env','/.git/config','/wp-login.php','/phpinfo.php','/actuator/env','/TesterURL/api/serial/1000039E/')
$queries = @('-','id=1','page=2','category=electronics','name=a%20b','id=1''+OR+''1''=''1','q=<script>alert(1)</script>','path=../../../../windows/win.ini','user=admin&pass=1234','debug=true')
$statuses = @(200,200,200,200,200,301,302,400,404,404,500,500)
$rnd = [System.Random]::new(20260828)

for ($f = 0; $f -lt $Files; $f++) {
    $fileName = [string]::Format('u_ex{0:yyyyMMdd}.log', (Get-Date).AddDays(-$f))
    $outFile = Join-Path $OutDir $fileName
    $sb = [System.Text.StringBuilder]::new()
    foreach ($line in $header) { [void]$sb.AppendLine($line) }
    $base = [DateTime]::Parse('2026-08-28 00:00:00').AddDays(-$f)
    for ($i = 0; $i -lt $RecordsPerFile; $i++) {
        $ts = $base.AddSeconds($i)
        $ip = $ips[$rnd.Next($ips.Length)]
        $client = $clients[$rnd.Next($clients.Length)]
        $agent = $agents[$rnd.Next($agents.Length)]
        $stem = $paths[$rnd.Next($paths.Length)]
        $query = $queries[$rnd.Next($queries.Length)]
        $status = $statuses[$rnd.Next($statuses.Length)]
        $taken = $rnd.Next(0, 3000)
        [void]$sb.AppendLine(('{0:yyyy-MM-dd HH:mm:ss} {1} GET {2} {3} 443 - {4} "{5}" - {6} 0 0 {7}' -f $ts, $ip, $stem, $query, $client, $agent, $status, $taken))
    }
    [System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
    Write-Host "Generated $outFile ($RecordsPerFile records, $((Get-Item $outFile).Length / 1MB) MB)"
}