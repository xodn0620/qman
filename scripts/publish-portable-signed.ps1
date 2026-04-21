# 포터블 Release publish + (선택) 코드 서명 + dist ZIP
# - 공인 PFX 없으면: CurrentUser\My 에 QMan 전용 자체 서명 인증서를 한 번 만들고 서명합니다.
#   (배포 시 사용자에게는 여전히 "알 수 없는 게시자"일 수 있음 — 공인 인증서는 -SignPfxPath 등으로 전달)
param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string] $Rid = "win-x64",

    [string] $PfxPath = "",
    [SecureString] $PfxPassword = $null,
    [string] $CertThumbprint = "",

    [string] $DevCertSubject = "CN=QMan Development Code Signing"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outName = "QMan-portable-$Rid"
$outDir = Join-Path $root "dist\$outName"

function Get-OrCreate-DevCodeSignCert {
    param([string] $Subject)
    $found = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey })
    if ($found.Count -gt 0) {
        Write-Host "기존 개발용 코드 서명 인증서 사용: $Subject"
        return $found[0]
    }
    Write-Host "개발용 코드 서명 인증서 생성: $Subject"
    return New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -HashAlgorithm SHA256 `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(3)
}

Get-Process -Name "QMan" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
if (Test-Path -LiteralPath $outDir) {
    Remove-Item -LiteralPath $outDir -Recurse -Force -ErrorAction Stop
}

Push-Location $root
try {
    dotnet publish "QMan.App\QMan.App.csproj" -c Release -r $Rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishReadyToRun=false `
        -p:PublishTrimmed=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:CopyOutputSymbolsToPublishDirectory=false `
        -p:SatelliteResourceLanguages=ko `
        -o $outDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 실패 (exit $LASTEXITCODE)"
    }

    Get-ChildItem $outDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem $outDir -Directory -ErrorAction SilentlyContinue | Where-Object {
        -not (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1)
    } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}

$signScript = Join-Path $root "scripts\sign-with-signtool.ps1"
if ($PfxPath) {
    if ($PfxPassword) {
        & $signScript -PublishDir $outDir -PfxPath $PfxPath -PfxPassword $PfxPassword
    }
    else {
        & $signScript -PublishDir $outDir -PfxPath $PfxPath
    }
}
elseif ($CertThumbprint) {
    & $signScript -PublishDir $outDir -CertThumbprint $CertThumbprint
}
else {
    $cert = Get-OrCreate-DevCodeSignCert -Subject $DevCertSubject
    $thumb = [string]$cert.Thumbprint
    & $signScript -PublishDir $outDir -CertThumbprint $thumb
}

$exePath = Join-Path $outDir "QMan.exe"
$zipPath = Join-Path $root "dist\$outName.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -Force

$exeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "=== 배포 결과 ==="
Write-Host "폴더: $outDir"
Write-Host "ZIP:  $zipPath"
Write-Host "QMan.exe SHA256: $exeHash"
Write-Host "ZIP     SHA256: $zipHash"
Write-Host ""
Write-Host "서명 확인: Get-AuthenticodeSignature -FilePath '$exePath'"
