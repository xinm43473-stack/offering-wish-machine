<#
  构建脚本：用 .NET Framework 的 csc 编译 上贡许愿机.exe

  用法：
    .\build.ps1                       # 编译为 上贡许愿机.exe（正式产物）
    .\build.ps1 -Output _test.exe     # 编译到指定文件名（回归测试用，不影响正式产物）
    .\build.ps1 -Manifest app.manifest # 附带 DPI 等清单
    .\build.ps1 -Icon other.ico       # 换一个图标（默认 icon.ico）
    .\build.ps1 -NoIcon               # 不带图标（调试用）
    .\build.ps1 -NoAssets             # 不内嵌初始素材（仅调试用）

  说明：把原来手工敲的 csc 命令固化下来，保证每次编译参数一致。

  图标：默认把根目录的 icon.ico 用 /win32icon 设成 exe 文件图标，
  同时以 "AppIcon.ico" 资源名编进去，供程序设置窗口/任务栏图标用
  （两者用同一个 ico，标题栏与资源管理器里看到的是同一张图）。

  初始素材：默认把 Assets 下 Deities / Offerings / background 三个目录里的素材
  以 "init/<相对路径>" 的资源名编进 exe。程序首次运行（或换了新素材包后）
  会把它们铺到 exe 同级的 Assets 下，用户之后手动删除的素材不会自己长回来。
#>

[CmdletBinding()]
param(
    [string]$Output = '上贡许愿机.exe',
    [string]$Manifest = '',
    [string]$Icon = 'icon.ico',
    [switch]$NoIcon,
    [switch]$NoAssets
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# 优先用 64 位框架的 csc，退回到 32 位
$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '找不到 .NET Framework 4.x 的 csc.exe，请先安装 .NET Framework 4.x。' }

$outPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    "/out:$outPath"
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
)
if ($Manifest) { $cscArgs += "/win32manifest:$Manifest" }

# ---- 图标：exe 文件图标 + 程序内窗口图标（共用同一个 ico） ----
if (-not $NoIcon) {
    $iconPath = if ([System.IO.Path]::IsPathRooted($Icon)) { $Icon } else { Join-Path $root $Icon }
    if (Test-Path $iconPath) {
        $cscArgs += "/win32icon:$iconPath"
        # 资源名固定为 AppIcon.ico，Form1 构造时按这个名字取出来设窗口/任务栏图标
        $cscArgs += "/resource:$iconPath,AppIcon.ico"
    }
    else {
        Write-Warning "找不到图标文件，已跳过：$iconPath"
    }
}

# ---- 初始素材：Assets\Deities、Assets\Offerings、Assets\background ----
# 只收素材目录，运行期产生的 Assets\logs、Assets\ui.ini、Assets\.initpack 与
# 人工整理的 Assets\暂时素材 都不进包。
$assetDirs = @('Deities', 'Offerings', 'background')
$embedded = 0
$embeddedBytes = 0
if (-not $NoAssets) {
    foreach ($sub in $assetDirs) {
        $dir = Join-Path $root (Join-Path 'Assets' $sub)
        if (-not (Test-Path $dir)) { continue }

        foreach ($f in Get-ChildItem -Path $dir -Recurse -File) {
            # csc 的 /resource 用逗号分隔「路径,资源名」，文件名含逗号会打乱参数
            if ($f.Name.Contains(',')) {
                Write-Warning "跳过（文件名含逗号，无法作为嵌入资源）：$($f.FullName)"
                continue
            }
            # 素材释放时可能残留的临时文件不进包
            if ($f.Extension -eq '.tmp') { continue }
            # 心愿（<名字>.wish）是使用者自己写的内容，不属于素材包，
            # 打进 exe 就等于把某个人的心愿变成所有人的初始素材
            if ($f.Extension -eq '.wish') { continue }

            $rel = $f.FullName.Substring((Join-Path $root 'Assets').Length + 1).Replace('\', '/')
            $logicalName = 'init/' + $rel
            $cscArgs += "/resource:$($f.FullName),$logicalName"
            $embedded++
            $embeddedBytes += $f.Length
        }
    }
}

$cscArgs += (Join-Path $root 'Form1.cs')

Write-Host "csc: $csc"
if (-not $NoAssets) {
    Write-Host ("初始素材: {0} 个文件, {1:F1} MB（嵌入 exe）" -f $embedded, ($embeddedBytes / 1MB))
}
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$info = Get-Item $outPath
Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $info.Name, $info.Length)
