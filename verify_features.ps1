<#
  回归脚本（二）：验证自动上贡速度条 / 多飘字伪加速 / 可自定义快捷键 / 设置持久化。

  用法：
    .\verify_features.ps1

  说明：feature_test.cs 与 Form1.cs 一起编译（需要 /main 指定入口），
  用临时资源目录运行，不会污染仓库里的 Assets\logs 与 Assets\ui.ini。
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '找不到 .NET Framework 4.x 的 csc.exe。' }

$exe = Join-Path $root '_verify_features.exe'
Push-Location $root
try {
    & $csc /nologo /target:exe /main:FeatureTest "/out:$exe" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        feature_test.cs Form1.cs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

    & $exe
    $code = $LASTEXITCODE
    Write-Host "功能验证退出码: $code"
    if ($code -ne 0) { throw "功能验证失败（exit $code）" }
}
finally {
    Pop-Location
    Remove-Item $exe -ErrorAction SilentlyContinue
}
