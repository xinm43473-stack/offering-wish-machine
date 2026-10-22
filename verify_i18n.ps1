<#
  回归脚本（三）：验证中/英双语界面、系统语言检测与语言设置持久化。

  用法：
    .\verify_i18n.ps1

  说明：i18n_test.cs 与 Form1.cs 一起编译（需要 /main 指定入口），
  用临时资源目录运行，不会污染仓库里的 Assets\ui.ini。
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

$exe = Join-Path $root '_verify_i18n.exe'
Push-Location $root
try {
    & $csc /nologo /target:exe /main:I18nTest "/out:$exe" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        i18n_test.cs Form1.cs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

    & $exe
    $code = $LASTEXITCODE
    Write-Host "语言验证退出码: $code"
    if ($code -ne 0) { throw "语言验证失败（exit $code）" }
}
finally {
    Pop-Location
    Remove-Item $exe -ErrorAction SilentlyContinue
}
