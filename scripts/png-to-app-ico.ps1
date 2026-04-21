# PNG -> ICO (256px, exe ApplicationIcon 용). Windows PowerShell + GDI+ (System.Drawing)
param(
    [Parameter(Mandatory = $true)]
    [string] $InputPng,
    [Parameter(Mandatory = $true)]
    [string] $OutputIco
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$inPath = (Resolve-Path -LiteralPath $InputPng).Path
$outPath = $OutputIco
if (-not [System.IO.Path]::IsPathRooted($outPath)) {
    $outPath = Join-Path (Get-Location) $outPath
}
$outPath = [System.IO.Path]::GetFullPath($outPath)
$outDir = Split-Path -Parent $outPath
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$img = [System.Drawing.Image]::FromFile($inPath)
try {
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($img, 0, 0, 256, 256)
        }
        finally { $g.Dispose() }

        $hIcon = $bmp.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        try {
            $fs = [System.IO.File]::Create($outPath)
            try {
                $icon.Save($fs)
            }
            finally { $fs.Dispose() }
        }
        finally { $icon.Dispose() }
    }
    finally { $bmp.Dispose() }
}
finally { $img.Dispose() }

Write-Host "작성: $outPath"
