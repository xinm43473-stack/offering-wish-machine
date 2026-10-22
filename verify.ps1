<#
  回归脚本：编译 verify_ui.cs（与 Form1.cs 一同编译）并运行，检查关键行为没有被改坏。

  用法：
    .\verify.ps1

  说明：verify_ui.cs 通过反射读取 Form1 的私有成员，
  因此改动 Form1 时若重命名了这些成员，验证程序会直接报错，
  这也是「不影响功能」这一约束的回归网。
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

$exe = Join-Path $root '_verify_ui.exe'
Push-Location $root
try {
    & $csc /nologo /target:exe /main:VerifyUi "/out:$exe" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        verify_ui.cs Form1.cs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

    # 验证程序要在能读到 Assets 的目录下运行
    & $exe
    $uiCode = $LASTEXITCODE
    Write-Host "验证程序退出码: $uiCode"
}
finally {
    Pop-Location
    Remove-Item $exe -ErrorAction SilentlyContinue
}

# 第二段：功能回归（速度条 / 多飘字 / 快捷键 / 设置持久化）
$featureScript = Join-Path $root 'verify_features.ps1'
if (Test-Path $featureScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $featureScript
    if ($LASTEXITCODE -ne 0) { throw "功能验证失败（exit $LASTEXITCODE）" }
}
else {
    Write-Host '（跳过功能验证：未找到 verify_features.ps1）'
}

if ($uiCode -ne 0) { throw "界面验证失败（exit $uiCode）" }

# 第三段：语言回归（中/英双语、系统语言检测、language 持久化）
$i18nScript = Join-Path $root 'verify_i18n.ps1'
if (Test-Path $i18nScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $i18nScript
    if ($LASTEXITCODE -ne 0) { throw "语言验证失败（exit $LASTEXITCODE）" }
}
else {
    Write-Host '（跳过语言验证：未找到 verify_i18n.ps1）'
}
