<#
.SYNOPSIS
    启动 / 停止 / 重启 / 查看 FinanceTracker.Web.Server（E2E 与人工验收共用，监听 http://localhost:5999）。

.DESCRIPTION
    - start   ：先 dotnet build（可用 -NoBuild 跳过），再以 http 启动配置后台启动，轮询到端口就绪才返回
    - stop    ：按端口结束监听进程（dotnet run 会派生子进程，只杀父进程杀不干净）
    - restart ：stop + start
    - status  ：打印是否在监听
    日志写到 %TEMP%\ft_webserver.log。

    云端地址：启动时把 Web.Server 的 ApiSettings__BaseUrl 指向 http://127.0.0.1:$CloudPort（默认 5271）——
    E2E 的「假云端」（tests/e2e/fake_cloud.py）监听这个端口。这样 E2E 登录时向云端换令牌、共享账本成员操作都只会打到假云端，
    不会碰本机真实的 FinanceTracker.Api / 数据库（真实 Api 默认监听 5270）。假云端没起来时，云端相关操作表现为「云端不可用」。
    这里写 127.0.0.1 而不是 localhost：假云端只监听 IPv4，.NET 解析 localhost 时先连 ::1；Windows 11 上这类被拒绝的回环连接
    要 2 秒才返回（实测 ::1 与 127.0.0.1 都一样），每个云端请求都白白多等 2 秒，还会把读路径 3 秒的期限吃掉大半。

    自动识别假实现：启动时设置 E2E__FakeCapture=true，Web.Server 把短信 / 通知使用权服务换成 E2ECapture.cs 里的假实现，
    E2E 经 POST /__e2e/capture 设置权限状态与读到的短信（浏览器里没有这两样东西）。不设置时与空实现一致：都没开启、没有短信。

    错误日志目录：启动时设置 E2E__LogDirectory=%TEMP%\ft_e2e_logs，「错误日志」页读这个专用目录（E2ELogs.cs），
    E2E 往里写日志行造数据、验证「清除日志」——清除只会删这个目录里的 app-*.log，不会碰开发机上真实的日志。
    E2E 经 GET /__e2e/log-directory 取得该目录。不设置时日志页读平台默认目录，行为与改造前一致。

.EXAMPLE
    powershell tests/e2e/server.ps1 -Action start
    powershell tests/e2e/server.ps1 -Action restart -NoBuild
#>
param(
    [ValidateSet('start', 'stop', 'restart', 'status')]
    [string]$Action = 'start',
    [int]$Port = 5999,
    [int]$CloudPort = 5271,
    [switch]$NoBuild,
    [int]$TimeoutSec = 90
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repoRoot 'server\FinanceTracker.Web.Server\FinanceTracker.Web.Server.csproj'
$log = Join-Path $env:TEMP 'ft_webserver.log'
$errLog = Join-Path $env:TEMP 'ft_webserver.err.log'

function Get-ListeningPids {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique
}

function Stop-Server {
    $pids = @(Get-ListeningPids)
    if ($pids.Count -eq 0) { Write-Host "端口 $Port 没有监听进程"; return }
    foreach ($procId in $pids) {
        Write-Host "结束进程 $procId"
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
    }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-ListeningPids).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
}

function Start-Server {
    if ((Get-ListeningPids).Count -gt 0) { Write-Host "端口 $Port 已在监听，跳过启动（需要重启请用 -Action restart）"; return }

    if (-not $NoBuild) {
        Write-Host "dotnet build Web.Server ..."
        dotnet build $project -v q --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败，未启动服务" }
    }

    Write-Host "启动 Web.Server（http 启动配置，端口 $Port，云端地址 http://127.0.0.1:$CloudPort）..."
    # 子进程继承环境变量；双下划线对应配置键 ApiSettings:BaseUrl（Program.cs 据此创建云端 HttpClient）。
    # 用 127.0.0.1 不用 localhost：见文件头说明（避开被拒绝的 ::1 连接带来的 2 秒延迟）
    $env:ApiSettings__BaseUrl = "http://127.0.0.1:$CloudPort"
    # 打开「自动识别」假实现（E2ECapture.cs）：E2E 经 POST /__e2e/capture 设置通知使用权 / 短信权限与读到的短信；不设置时状态就是「都没开启、没有短信」，与空实现一致
    $env:E2E__FakeCapture = 'true'
    # 「错误日志」页读的专用目录（见文件头说明）；目录不存在时页面显示为空，E2E 写日志时自己会创建
    $env:E2E__LogDirectory = Join-Path $env:TEMP 'ft_e2e_logs'
    Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--no-build', '--project', "`"$project`"", '--launch-profile', 'http') `
        -WorkingDirectory $repoRoot -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError $errLog | Out-Null

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ((Get-ListeningPids).Count -gt 0) {
            try {
                $null = Invoke-WebRequest -Uri "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 5
                Write-Host "Web.Server 已就绪：http://localhost:$Port/"
                return
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "---- 日志尾部 ----"
    if (Test-Path $log) { Get-Content $log -Tail 30 }
    if (Test-Path $errLog) { Get-Content $errLog -Tail 30 }
    throw "等待 Web.Server 就绪超时（${TimeoutSec}s）"
}

switch ($Action) {
    'start'   { Start-Server }
    'stop'    { Stop-Server }
    'restart' { Stop-Server; Start-Server }
    'status'  {
        $pids = @(Get-ListeningPids)
        if ($pids.Count -gt 0) { Write-Host "监听中：端口 $Port，进程 $($pids -join ', ')" } else { Write-Host "未运行" }
    }
}
