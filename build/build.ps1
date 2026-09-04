# ===========================================
# FinanceTracker 打包上传脚本 (Windows)
# 支持 Android/iOS 打包并上传到蒲公英
# ===========================================

param(
    [switch]$Android,
    [switch]$iOS,
    [switch]$All,
    [switch]$NoUpload,
    [switch]$Help
)

# 显示帮助信息
if ($Help) {
    Write-Host @"
FinanceTracker 打包上传脚本

用法:
    .\build.ps1 [-Android] [-iOS] [-All] [-NoUpload] [-Help]

参数:
    -Android    打包 Android APK
    -iOS        打包 iOS IPA (仅 macOS)
    -All        打包所有平台
    -NoUpload   仅打包，不上传到蒲公英
    -Help       显示帮助信息

示例:
    .\build.ps1 -Android              # 打包 Android 并上传
    .\build.ps1 -Android -NoUpload    # 仅打包 Android
    .\build.ps1 -All                  # 打包所有平台并上传
"@
    exit 0
}

# 设置错误时停止
$ErrorActionPreference = "Stop"

# 项目路径
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$PROJECT_ROOT = Split-Path -Parent $SCRIPT_DIR
$CONFIG_FILE = Join-Path $SCRIPT_DIR "config.env"

# 打印带颜色的消息
function Write-Info { param($msg) Write-Host "[INFO] $msg" -ForegroundColor Blue }
function Write-Success { param($msg) Write-Host "[SUCCESS] $msg" -ForegroundColor Green }
function Write-Warning { param($msg) Write-Host "[WARNING] $msg" -ForegroundColor Yellow }
function Write-Error { param($msg) Write-Host "[ERROR] $msg" -ForegroundColor Red }
function Write-Separator { Write-Host "=" * 50 }

# 加载配置文件
function Load-Config {
    if (-not (Test-Path $CONFIG_FILE)) {
        Write-Error "配置文件不存在: $CONFIG_FILE"
        Write-Info "请复制 config.example.env 为 config.env 并填写配置"
        exit 1
    }

    # 读取配置文件
    Get-Content $CONFIG_FILE | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
            $key = $matches[1].Trim()
            $value = $matches[2].Trim().Trim('"')
            Set-Variable -Name $key -Value $value -Scope Script
        }
    }
    Write-Success "配置文件加载成功"
}

# 检查依赖
function Test-Dependencies {
    Write-Info "检查依赖..."

    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        Write-Error "dotnet SDK 未安装"
        exit 1
    }

    if (-not (Get-Command "curl" -ErrorAction SilentlyContinue)) {
        Write-Error "curl 未安装"
        exit 1
    }

    # Android 打包检查
    if ($Android -or $All) {
        $keystorePath = Join-Path $PROJECT_ROOT $ANDROID_KEYSTORE_PATH
        if (-not (Test-Path $keystorePath)) {
            Write-Error "Android Keystore 文件不存在: $keystorePath"
            exit 1
        }
    }

    Write-Success "依赖检查通过"
}

# 获取版本信息
function Get-VersionInfo {
    $csprojPath = Join-Path $PROJECT_ROOT "src\FinanceTracker.App\FinanceTracker.App.csproj"

    # 从 csproj 读取版本号
    [xml]$csproj = Get-Content $csprojPath
    $currentVersion = $csproj.Project.PropertyGroup.ApplicationDisplayVersion
    $currentBuild = $csproj.Project.PropertyGroup.ApplicationVersion

    if (-not $currentVersion) { $currentVersion = "1.0.0" }
    if (-not $currentBuild) { $currentBuild = "1" }

    Write-Info "当前版本: $currentVersion (Build: $currentBuild)"

    # 自定义版本号
    if ($CUSTOM_VERSION) {
        $script:APP_VERSION = $CUSTOM_VERSION
    } else {
        $script:APP_VERSION = $currentVersion
    }

    # 自定义构建号
    if ($CUSTOM_BUILD_NUMBER) {
        $script:BUILD_NUMBER = $CUSTOM_BUILD_NUMBER
    } elseif ($AUTO_INCREMENT_VERSION -eq "true") {
        $script:BUILD_NUMBER = [int]$currentBuild + 1
    } else {
        $script:BUILD_NUMBER = $currentBuild
    }

    Write-Info "打包版本: $APP_VERSION (Build: $BUILD_NUMBER)"
}

# 更新 csproj 版本号
function Update-Version {
    if ($AUTO_INCREMENT_VERSION -eq "true" -or $CUSTOM_VERSION -or $CUSTOM_BUILD_NUMBER) {
        Write-Info "更新版本号..."
        $csprojPath = Join-Path $PROJECT_ROOT "src\FinanceTracker.App\FinanceTracker.App.csproj"

        [xml]$csproj = Get-Content $csprojPath
        $csproj.Project.PropertyGroup.ApplicationDisplayVersion = $APP_VERSION
        $csproj.Project.PropertyGroup.ApplicationVersion = $BUILD_NUMBER
        $csproj.Save($csprojPath)

        Write-Success "版本号已更新: $APP_VERSION ($BUILD_NUMBER)"
    }
}

# 打包 Android
function Build-Android {
    Write-Separator
    Write-Info "开始打包 Android..."
    Write-Separator

    Push-Location $PROJECT_ROOT

    try {
        # 打包 APK
        Write-Info "打包 APK..."
        dotnet publish src\FinanceTracker.App\FinanceTracker.App.csproj `
            -f net10.0-android `
            -c Release `
            -o build\output\android

        # 查找生成的 APK 文件
        $apkFile = Get-ChildItem -Path "build\output\android" -Filter "*.apk" -Recurse | Select-Object -First 1

        if (-not $apkFile) {
            Write-Error "APK 文件未找到"
            exit 1
        }

        Write-Success "Android APK 打包成功: $($apkFile.FullName)"
        $apkFile.FullName | Out-File -FilePath "build\output\android_apk_path.txt" -Encoding UTF8

        return $apkFile.FullName
    } finally {
        Pop-Location
    }
}

# 打包 iOS
function Build-iOS {
    Write-Separator
    Write-Info "开始打包 iOS..."
    Write-Separator

    # 检查是否为 macOS
    if ($IsWindows) {
        Write-Error "iOS 打包仅支持 macOS 系统"
        return $null
    }

    Push-Location $PROJECT_ROOT

    try {
        # 打包 IPA
        Write-Info "打包 IPA..."
        dotnet publish src\FinanceTracker.App\FinanceTracker.App.csproj `
            -f net10.0-ios `
            -c Release `
            -o build\output\ios

        # 查找生成的 IPA 文件
        $ipaFile = Get-ChildItem -Path "build\output\ios" -Filter "*.ipa" -Recurse | Select-Object -First 1

        if (-not $ipaFile) {
            Write-Error "IPA 文件未找到"
            Write-Warning "iOS 打包可能需要在 Xcode 中手动归档"
            return $null
        }

        Write-Success "iOS IPA 打包成功: $($ipaFile.FullName)"
        $ipaFile.FullName | Out-File -FilePath "build\output\ios_ipa_path.txt" -Encoding UTF8

        return $ipaFile.FullName
    } finally {
        Pop-Location
    }
}

# 上传到蒲公英
function Upload-ToPgyer {
    param(
        [string]$FilePath,
        [string]$Platform
    )

    Write-Separator
    Write-Info "上传 $Platform 到蒲公英..."
    Write-Separator

    if (-not (Test-Path $FilePath)) {
        Write-Error "文件不存在: $FilePath"
        return $false
    }

    # 构建上传参数
    $uploadUrl = "https://www.pgyer.com/apiv2/app/upload"

    # 使用 curl 上传
    $curlArgs = @(
        "-F", "file=@$FilePath",
        "-F", "_api_key=$PGYER_API_KEY"
    )

    if ($PGYER_INSTALL_PASSWORD) {
        $curlArgs += "-F", "password=$PGYER_INSTALL_PASSWORD"
    }

    if ($PGYER_CHANNEL_PASSWORD) {
        $curlArgs += "-F", "channelPassword=$PGYER_CHANNEL_PASSWORD"
    }

    $curlArgs += $uploadUrl

    Write-Info "正在上传，请稍候..."
    try {
        $response = & curl @curlArgs 2>$null | ConvertFrom-Json

        if ($response.code -eq 0) {
            Write-Success "上传成功！"
            Write-Info "应用名称: $($response.data.appName)"
            Write-Info "版本: $($response.data.buildVersion)"
            Write-Info "下载地址: https://www.pgyer.com/$($response.data.buildKey)"
            return $true
        } else {
            Write-Error "上传失败: $($response.message)"
            return $false
        }
    } catch {
        Write-Error "上传失败: $_"
        return $false
    }
}

# 生成构建报告
function Show-Report {
    Write-Separator
    Write-Info "构建报告"
    Write-Separator

    Write-Host "应用名称: 记账本"
    Write-Host "版本号: $APP_VERSION"
    Write-Host "构建号: $BUILD_NUMBER"
    Write-Host "构建时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Host ""

    $androidPath = Join-Path $PROJECT_ROOT "build\output\android_apk_path.txt"
    if (Test-Path $androidPath) {
        Write-Host "Android APK: $(Get-Content $androidPath -Raw)"
    }

    $iosPath = Join-Path $PROJECT_ROOT "build\output\ios_ipa_path.txt"
    if (Test-Path $iosPath) {
        Write-Host "iOS IPA: $(Get-Content $iosPath -Raw)"
    }

    Write-Separator
}

# 主函数
function Main {
    Write-Separator
    Write-Info "FinanceTracker 打包上传脚本"
    Write-Separator

    # 如果没有指定平台，默认打包 Android
    if (-not ($Android -or $iOS -or $All)) {
        $Android = $true
    }

    # 执行流程
    Load-Config
    Test-Dependencies
    Get-VersionInfo
    Update-Version

    # 创建输出目录
    $outputDir = Join-Path $PROJECT_ROOT "build\output"
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    # Android 打包
    if ($Android -or $All) {
        $apkPath = Build-Android
        if ($apkPath -and -not $NoUpload) {
            Upload-ToPgyer -FilePath $apkPath -Platform "Android"
        }
    }

    # iOS 打包
    if ($iOS -or $All) {
        $ipaPath = Build-iOS
        if ($ipaPath -and -not $NoUpload) {
            Upload-ToPgyer -FilePath $ipaPath -Platform "iOS"
        }
    }

    # 生成报告
    Show-Report

    Write-Success "所有任务完成！"
}

# 执行主函数
Main
