# FDLike2026 の配布 zip を作る。
#   .\tools\publish.ps1                 … .NET 8 Desktop Runtime が必要な軽量版（既定）
#   .\tools\publish.ps1 -SelfContained  … ランタイム同梱版（.NET が無い PC でも動く。150MB 前後）
# 出来上がり: publish\FDLike2026-v<版>-win-x64[-selfcontained].zip
param(
    [switch]$SelfContained,
    [switch]$NoZip
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# 版は csproj の <Version> から
$ver = ([xml](Get-Content FDLike2026.csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $ver) { $ver = '0.0.0' }
$suffix = if ($SelfContained) { '-selfcontained' } else { '' }
$name = "FDLike2026-v$ver-win-x64$suffix"
$stage = Join-Path $root "publish\$name"
$zip = Join-Path $root "publish\$name.zip"

Get-Process FDLike2026 -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Write-Output "=== publish v$ver ($(if ($SelfContained) { 'self-contained' } else { 'framework-dependent' })) ==="
$args = @('publish', '-c', 'Release', '-r', 'win-x64', '-o', $stage,
          '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none', '-p:DebugSymbols=false')
$args += if ($SelfContained) { '--self-contained', 'true' } else { '--self-contained', 'false' }
$out = & dotnet @args 2>&1
$err = $out | Select-String ' error '
if ($err) { $err; Write-Output 'PUBLISH FAILED'; exit 1 }

# 同梱する文書
Copy-Item README.md, CHANGELOG.md, LICENSE, THIRD-PARTY-NOTICES.md -Destination $stage
Copy-Item tools\dist-readme.txt -Destination (Join-Path $stage 'はじめにお読みください.txt')
# 不要物（XML ドキュメント・pdb など）を除く
Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $stage -Filter *.xml | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Output '--- 内容 ---'
Get-ChildItem $stage | ForEach-Object { '{0,12:N0}  {1}' -f $_.Length, $_.Name }

if (-not $NoZip) {
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Output ("=== 完成: {0}  ({1:N1} MB) ===" -f $zip, ((Get-Item $zip).Length / 1MB))
}
