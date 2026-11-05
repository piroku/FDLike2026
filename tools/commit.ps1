# ビルド（Debug / Release）が通ったときだけ git commit して Release 版を起動する。
#   .\tools\commit.ps1 -Message "..." -Check @{ 'file' = 'pattern' }
param(
    [Parameter(Mandatory = $true)][string]$Message,
    [hashtable]$Check = @{},
    [switch]$Amend
)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
& "$PSScriptRoot\build_all.ps1" -NoRun -Check $Check | Select-String 'OK|FAILED|error|STALE'
if ($LASTEXITCODE -ne 0) { Write-Output 'ABORT: ビルド失敗または古いソースのためコミットしません'; exit 1 }
Set-Location (Split-Path $PSScriptRoot -Parent)
git add -A
if ($Amend) { git commit -q --amend --no-edit } else { git commit -q -m $Message }
git log --oneline -1
Start-Process -FilePath (Resolve-Path 'bin\Release\net8.0-windows\FDLike2026.exe')
