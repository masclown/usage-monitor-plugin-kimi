<#
.SYNOPSIS
    UsageMonitor 一键启动脚本：环境检查 → 清理占用 → 构建 → 运行。

.DESCRIPTION
    本脚本封装了 UsageMonitor 的标准启动流程：
      1. 校验 .NET 8 SDK 是否安装
      2. 终止已运行的 UsageMonitor 进程，避免 exe 被占用导致构建失败
      3. 调用 dotnet build 编译整个解决方案
      4. 用 Start-Process 启动编译产物 exe，让 WPF 应用作为独立进程运行
         （避免「在 PowerShell 中运行」菜单关闭窗口时把 WPF 进程一起带走）

.NOTES
    - 适用系统：Windows 10 / Windows 11
    - 适用 Shell：PowerShell 5.1+（不支持 '&&'，本脚本使用 ';' 分隔）
    - 使用方式：在项目根目录下执行  .\Start-UsageMonitor.ps1
#>

[CmdletBinding()]
param(
    # 构建配置：Debug 或 Release，默认 Release
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    # 是否跳过构建直接运行（调试用）。仅当已存在编译产物时使用
    [switch]$SkipBuild,

    # 是否在运行前强制结束已存在的 UsageMonitor 进程
    [switch]$ForceKill
)

# 脚本所在目录即项目根目录（脚本必须放在 UsageMonitor/ 根目录）
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionFile = Join-Path $ProjectRoot "UsageMonitor.sln"
$AppProject = Join-Path $ProjectRoot "src\UsageMonitor.App\UsageMonitor.App.csproj"

#region 工具函数

<#
.SYNOPSIS
    输出一行带颜色的步骤日志，便于用户在终端区分阶段。
.PARAMETER Message
    要输出的文本内容。
.PARAMETER Color
    控制台颜色，默认青色。
#>
function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [System.ConsoleColor]$Color = [System.ConsoleColor]::Cyan
    )
    Write-Host "==> $Message" -ForegroundColor $Color
}

<#
.SYNOPSIS
    检测本机是否已安装指定主版本的 .NET SDK。
.PARAMETER RequiredMajorVersion
    必需的主版本号，本项目固定为 8。
#>
function Test-DotNetSdk {
    param([int]$RequiredMajorVersion = 8)

    # dotnet --version 在未安装 SDK 时返回非零并输出空字符串
    $versionOutput = (& dotnet --version) 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($versionOutput)) {
        throw "未检测到 dotnet 命令。请先安装 .NET $RequiredMajorVersion SDK，下载地址：https://dotnet.microsoft.com/download"
    }

    # 版本号形如 "8.0.100"，主版本号是第一个分段
    $major = ($versionOutput.Trim() -split '\.')[0]
    if ([int]$major -lt $RequiredMajorVersion) {
        throw "检测到 .NET SDK 主版本为 $major，但本项目需要 $RequiredMajorVersion 或更高版本。"
    }

    Write-Step ".NET SDK 版本：$versionOutput（满足要求）" -Color Green
}

<#
.SYNOPSIS
    结束已存在的 UsageMonitor 进程，避免构建时出现 "exe is locked" 错误。
.DESCRIPTION
    实际进程名是 UsageMonitor.App（exe 为 UsageMonitor.App.exe），早期版本用精确名
    "UsageMonitor" 匹配不到实际进程，导致误报"无进程"后构建被 DLL 锁定失败；
    现改用通配符 UsageMonitor*，同时覆盖 UsageMonitor.App 与 UsageMonitor.LoginHelper。
.PARAMETER ProcessName
    要结束的进程名（支持通配符），默认 UsageMonitor*。
#>
function Stop-UsageMonitorProcess {
    param([string]$ProcessName = "UsageMonitor*")

    $running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($null -eq $running -or $running.Count -eq 0) {
        Write-Step "未发现运行中的 $ProcessName 进程，跳过清理。" -Color DarkGray
        return
    }

    Write-Step "检测到 $($running.Count) 个匹配 $ProcessName 的进程，正在结束..." -Color Yellow
    foreach ($proc in $running) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            Write-Host "    - 已结束 $($proc.ProcessName) PID=$($proc.Id)" -ForegroundColor DarkYellow
        }
        catch {
            Write-Warning "结束进程 PID=$($proc.Id) 失败：$($_.Exception.Message)"
        }
    }

    # 等待操作系统释放文件句柄
    Start-Sleep -Milliseconds 500

    # 清理后复查：若进程仍存活（典型原因：旧实例以管理员权限运行而本脚本未提权，
    # Stop-Process 拒绝访问），立即报错给出指引，避免继续构建刷出大量 DLL 锁定重试日志。
    $survivors = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($null -ne $survivors -and $survivors.Count -gt 0) {
        $pids = ($survivors | ForEach-Object { "$($_.ProcessName)(PID=$($_.Id))" }) -join '、'
        throw "无法结束以下进程：$pids。若提示'拒绝访问'，请以管理员身份重新运行本脚本，或手动退出托盘中的 UsageMonitor 后重试。"
    }
}

<#
.SYNOPSIS
    调用 dotnet build 编译整个解决方案。
.PARAMETER Configuration
    Debug 或 Release。
#>
function Invoke-DotNetBuild {
    param([string]$Configuration)

    Write-Step "开始构建解决方案（Configuration=$Configuration）..." -Color Cyan
    & dotnet build $SolutionFile -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 执行失败，退出码 $LASTEXITCODE。请根据上方日志修复编译错误后重试。"
    }
    Write-Step "构建完成。" -Color Green
}

<#
.SYNOPSIS
    定位编译产物 UsageMonitor.App.exe 的完整路径。
.DESCRIPTION
    bin\<Configuration>\<TFM>\UsageMonitor.App.exe 中的 TFM 来自 csproj 的 TargetFramework
    （当前为 net8.0-windows）。为兼容未来 TFM 变化，采用递归搜索方式定位。
.PARAMETER AppProjectPath
    UsageMonitor.App.csproj 的完整路径。
.PARAMETER Configuration
    Debug 或 Release。
#>
function Get-AppExePath {
    param(
        [string]$AppProjectPath,
        [string]$Configuration
    )

    $appDir = Split-Path -Parent $AppProjectPath
    $binRoot = Join-Path $appDir "bin\$Configuration"
    if (-not (Test-Path $binRoot)) {
        throw "未找到编译产物目录：$binRoot。请先执行 dotnet build。"
    }

    # 递归查找 UsageMonitor.App.exe（避免 TFM 改名后失效）
    $exe = Get-ChildItem -Path $binRoot -Recurse -Filter "UsageMonitor.App.exe" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $exe) {
        throw "未找到编译产物 exe：$binRoot\**\UsageMonitor.App.exe。请先执行 dotnet build。"
    }
    return $exe.FullName
}

<#
.SYNOPSIS
    启动 UsageMonitor 主程序（编译后的 exe），作为独立进程运行。
.DESCRIPTION
    关键修复：之前用 `dotnet run` 同步启动 WPF 应用，PowerShell 进程会等待 dotnet 退出。
    当 PowerShell 窗口关闭时整个进程树会被结束，导致托盘图标消失。
    改为 `Start-Process` 启动编译产物 exe 后，WPF 应用是独立进程，PowerShell 退出不影响。
.PARAMETER AppProjectPath
    UsageMonitor.App.csproj 的完整路径。
.PARAMETER Configuration
    Debug 或 Release。
#>
function Invoke-DotNetRun {
    param(
        [string]$AppProjectPath,
        [string]$Configuration
    )

    $exePath = Get-AppExePath -AppProjectPath $AppProjectPath -Configuration $Configuration
    Write-Step "定位编译产物：$exePath" -Color DarkGray
    Write-Step "启动 UsageMonitor（独立进程模式）..." -Color Cyan

    # PassThru 返回进程对象；不传 -Wait，让 PowerShell 立即结束不影响 WPF 应用
    $proc = Start-Process -FilePath $exePath -PassThru
    if ($null -eq $proc) {
        throw "Start-Process 启动失败，请检查 exe 是否被杀毒软件拦截。"
    }
    Write-Step "已启动，PID=$($proc.Id)。PowerShell 退出不会影响 WPF 应用。" -Color Green
}

#endregion

#region 主流程

try {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "   UsageMonitor 一键启动脚本" -ForegroundColor Magenta
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "项目根目录：$ProjectRoot"
    Write-Host "解决方案：  $SolutionFile"
    Write-Host "构建配置：  $Configuration"
    Write-Host ""

    # 1. 文件存在性校验
    if (-not (Test-Path $SolutionFile)) {
        throw "未找到解决方案文件：$SolutionFile。请确认脚本位于项目根目录。"
    }
    if (-not (Test-Path $AppProject)) {
        throw "未找到 App 项目文件：$AppProject。"
    }

    # 2. .NET SDK 版本检查
    Test-DotNetSdk -RequiredMajorVersion 8

    # 3. 清理可能占用 exe/DLL 的旧进程（通配符覆盖 App 与 LoginHelper）
    if ($ForceKill -or -not $SkipBuild) {
        Stop-UsageMonitorProcess -ProcessName "UsageMonitor*"
    }

    # 4. 编译解决方案
    if (-not $SkipBuild) {
        Invoke-DotNetBuild -Configuration $Configuration
    }
    else {
        Write-Step "已跳过构建（-SkipBuild）。" -Color DarkGray
    }

    # 5. 启动主程序（作为独立进程，PowerShell 退出不影响 WPF 应用）
    Invoke-DotNetRun -AppProjectPath $AppProject -Configuration $Configuration
}
catch {
    Write-Host ""
    Write-Host "启动失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "请根据以上错误排查后重试。常见原因：.NET SDK 未安装、端口被占用、源代码存在编译错误。" -ForegroundColor DarkRed
    exit 1
}

#endregion
