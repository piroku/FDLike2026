# DocumentText の抽出を実ファイルで確認する（FDLike2026.exe --extract 経由）
param([string]$Root = "$env:USERPROFILE\Documents", [int]$PerExt = 1)
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = Resolve-Path 'bin\Debug\net8.0-windows\FDLike2026.exe'
$out = Join-Path $env:TEMP 'fdlike_extract.txt'
foreach ($ext in '.docx', '.xlsx', '.pptx', '.pdf', '.doc', '.xls', '.ppt') {
    $files = Get-ChildItem -Path $Root -Recurse -File -Filter "*$ext" -ErrorAction SilentlyContinue | Where-Object { $_.Length -lt 20MB } | Select-Object -First $PerExt
    if (-not $files) { Write-Output "[$ext] なし"; continue }
    foreach ($f in $files) {
        if (Test-Path $out) { Remove-Item $out }
        Start-Process -FilePath $exe -ArgumentList @('--extract', ('"' + $f.FullName + '"'), ('"' + $out + '"')) -Wait
        if (-not (Test-Path $out)) { Write-Output "[$ext] $($f.Name): 出力なし"; continue }
        $t = Get-Content $out -Raw -Encoding UTF8
        $lines = ($t -split "`n")
        $head = ($lines | Select-Object -Skip 1 | Where-Object { $_.Trim() } | Select-Object -First 3) -join ' / '
        if ($head.Length -gt 140) { $head = $head.Substring(0, 140) + '…' }
        Write-Output "[$ext] $($f.Name): $($lines.Count) 行 $($lines[0])  $head"
    }
}
