# FDLike2026: 両構成（Debug / Release）をビルドする補助スクリプト。
# 引数にパターンを渡すと、その文字列が指定ファイルにあるか（= 最新ソースが届いているか）を先に確認する。
#   例: .\tools\build_all.ps1 -Check @{ 'Views\MacroWindow.xaml.cs' = 'MacroEditDialog.Show' }
param(
    [hashtable]$Check = @{},
    [switch]$NoRun
)
$ErrorActionPreference = 'Continue'
Set-Location (Split-Path $PSScriptRoot -Parent)

foreach ($k in $Check.Keys) {
    if (-not (Test-Path $k) -or -not (Select-String -Path $k -Pattern $Check[$k] -SimpleMatch -Quiet)) {
        Write-Output "STALE: $k に '$($Check[$k])' がありません"
        exit 1
    }
}

Get-Process FDLike2026 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

foreach ($c in 'Debug', 'Release') {
    Write-Output "=== $c ==="
    $out = dotnet build -c $c 2>&1
    $err = $out | Select-String ' error ' | Select-Object -Unique -First 10
    if ($err) { $err; Write-Output "BUILD FAILED ($c)"; exit 1 }
    $out | Select-String 'warning CS' | Select-Object -Unique -First 5
    Write-Output "OK ($c)"
}
if (-not $NoRun) {
    Start-Process -FilePath (Resolve-Path 'bin\Release\net8.0-windows\FDLike2026.exe')
}
