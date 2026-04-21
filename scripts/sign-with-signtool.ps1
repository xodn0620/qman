# QMan publish 산출물에 Authenticode 서명
# - 우선 signtool.exe (Windows SDK). 없으면 Set-AuthenticodeSignature 로 동일 목적 서명(타임스탬프 포함).
#
# 예시 (PFX):
#   .\scripts\sign-with-signtool.ps1 -PublishDir "dist\QMan-portable-win-x64" `
#     -PfxPath "C:\certs\codesign.pfx" -PfxPassword (Read-Host -AsSecureString)
#
# 예시 (인증서 저장소 — 지문):
#   .\scripts\sign-with-signtool.ps1 -PublishDir "dist\QMan-portable-win-x64" -CertThumbprint "A1B2..."
#
# 환경변수 (CI 등): QMAN_CODESIGN_PFX, QMAN_CODESIGN_PFX_PASSWORD (평문, 주의)

param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir,
    [string] $PfxPath = "",
    [SecureString] $PfxPassword = $null,
    [string] $CertThumbprint = "",
    [string] $SigntoolPath = "",
    [string] $TimestampUrl = "http://timestamp.digicert.com",
    [switch] $SignDlls
)

$ErrorActionPreference = "Stop"

function Resolve-SigntoolPath {
    param([string] $Explicit)
    if ($Explicit -and (Test-Path -LiteralPath $Explicit)) { return (Resolve-Path -LiteralPath $Explicit).Path }

    $candidates = @(
        $env:SIGNTOOL_PATH
        $(if ($env:WindowsSdkVerBinPath) { Join-Path $env:WindowsSdkVerBinPath "x64\signtool.exe" })
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($c in $candidates) {
        return (Resolve-Path -LiteralPath $c).Path
    }

    $kitsBinRoots = @()
    foreach ($base in @(
            (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
            (Join-Path $env:ProgramFiles "Windows Kits\10\bin")
        )) {
        if (Test-Path -LiteralPath $base) { $kitsBinRoots += $base }
    }
    try {
        $reg = Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots" -ErrorAction Stop
        if ($reg.KitsRoot10) {
            $b = Join-Path $reg.KitsRoot10.TrimEnd('\') "bin"
            if (Test-Path -LiteralPath $b) { $kitsBinRoots += $b }
        }
    }
    catch { }

    foreach ($kitsRoot in ($kitsBinRoots | Select-Object -Unique)) {
        $found = Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    $fromPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }

    return $null
}

function Get-SigningCertificate {
    param([hashtable] $SignParams)

    if ($SignParams.Pfx) {
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $SignParams.PfxPath, $SignParams.PfxPlainPassword, $flags)
    }

    if ($SignParams.Thumbprint) {
        $c = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $SignParams.Thumbprint })
        if ($c.Count -eq 0) { throw "지문에 해당하는 인증서를 CurrentUser\\My 에서 찾을 수 없습니다." }
        return $c[0]
    }

    $codeSigningOid = New-Object System.Security.Cryptography.Oid "1.3.6.1.5.5.7.3.3"
    foreach ($c in (Get-ChildItem Cert:\CurrentUser\My)) {
        if (-not $c.HasPrivateKey) { continue }
        foreach ($eku in $c.EnhancedKeyUsageList) {
            if ($eku.ObjectId.Value -eq $codeSigningOid.Value) { return $c }
        }
    }
    throw "저장소에서 코드 서명용 인증서를 찾지 못했습니다. -CertThumbprint 또는 PFX 를 지정하세요."
}

function Invoke-AuthenticodeSignFile {
    param(
        [string] $FilePath,
        [System.Security.Cryptography.X509Certificates.X509Certificate2] $Certificate,
        [string] $TimestampServer
    )
    Write-Host "서명(Authenticode): $FilePath"
    $r = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $Certificate `
        -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    if (-not $r.SignerCertificate) {
        throw "Set-AuthenticodeSignature 실패 ($($r.Status)): $($r.StatusMessage)"
    }
}

function Invoke-SignFile {
    param(
        [string] $Signtool,
        [string] $FilePath,
        [hashtable] $SignArgs
    )

    $argList = @(
        "sign",
        "/fd", "sha256",
        "/tr", $TimestampUrl,
        "/td", "sha256"
    )

    if ($SignArgs.Pfx) {
        $argList += @("/f", $SignArgs.PfxPath)
        if ($SignArgs.PfxPlainPassword) {
            $argList += @("/p", $SignArgs.PfxPlainPassword)
        }
    }
    elseif ($SignArgs.Thumbprint) {
        $argList += @("/sha1", $SignArgs.Thumbprint)
    }
    else {
        $argList += "/a"
    }

    $argList += $FilePath

    Write-Host "서명: $FilePath"
    & $Signtool @argList
    if ($LASTEXITCODE -ne 0) { throw "signtool sign 실패 (exit $LASTEXITCODE): $FilePath" }
}

$publishFull = if ([System.IO.Path]::IsPathRooted($PublishDir)) { $PublishDir } else { Join-Path (Split-Path -Parent $PSScriptRoot) $PublishDir }
if (-not (Test-Path -LiteralPath $publishFull)) {
    throw "폴더가 없습니다: $publishFull"
}

$signtoolExe = Resolve-SigntoolPath -Explicit $SigntoolPath

$pfxFromEnv = $env:QMAN_CODESIGN_PFX
if (-not $PfxPath -and $pfxFromEnv) { $PfxPath = $pfxFromEnv }

$signParams = @{}
if ($PfxPath) {
    if (-not (Test-Path -LiteralPath $PfxPath)) { throw "PFX 없음: $PfxPath" }
    $plain = $null
    if ($PfxPassword) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
        try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) | Out-Null }
    }
    elseif ($env:QMAN_CODESIGN_PFX_PASSWORD) {
        $plain = $env:QMAN_CODESIGN_PFX_PASSWORD
    }
    else {
        throw "PFX 사용 시 -PfxPassword 또는 환경변수 QMAN_CODESIGN_PFX_PASSWORD 가 필요합니다."
    }
    $signParams.Pfx = $true
    $signParams.PfxPath = (Resolve-Path -LiteralPath $PfxPath).Path
    $signParams.PfxPlainPassword = $plain
}
elseif ($CertThumbprint) {
    $signParams.Thumbprint = $CertThumbprint.Trim()
}
else {
    Write-Warning "PFX/Thumbprint 없음 — 저장소에서 자동 선택(/a)합니다. 잘못된 인증서로 서명될 수 있어 권장하지 않습니다."
    $signParams = @{}
}

$exe = Join-Path $publishFull "QMan.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "QMan.exe 가 없습니다: $exe (publish 먼저 실행했는지 확인)"
}

if ($signtoolExe) {
    Invoke-SignFile -Signtool $signtoolExe -FilePath $exe -SignArgs $signParams
    if ($SignDlls) {
        Get-ChildItem -LiteralPath $publishFull -Filter "*.dll" -File | ForEach-Object {
            Invoke-SignFile -Signtool $signtoolExe -FilePath $_.FullName -SignArgs $signParams
        }
    }
    Write-Host "검증: signtool verify /pa"
    & $signtoolExe verify /pa $exe
    if ($LASTEXITCODE -ne 0) { throw "signtool verify 실패 (exit $LASTEXITCODE)" }
}
else {
    Write-Warning "signtool.exe 없음 — Set-AuthenticodeSignature 로 서명합니다."
    $certObj = Get-SigningCertificate -SignParams $signParams
    try {
        Invoke-AuthenticodeSignFile -FilePath $exe -Certificate $certObj -TimestampServer $TimestampUrl
        if ($SignDlls) {
            Get-ChildItem -LiteralPath $publishFull -Filter "*.dll" -File | ForEach-Object {
                Invoke-AuthenticodeSignFile -FilePath $_.FullName -Certificate $certObj -TimestampServer $TimestampUrl
            }
        }
        $sig = Get-AuthenticodeSignature -FilePath $exe
        $subj = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { "(없음)" }
        Write-Host "검증: $($sig.Status) — $subj"
        if ($sig.Status -eq "NotSigned") { throw "서명이 적용되지 않았습니다." }
    }
    finally {
        if ($signParams.Pfx -and ($certObj -is [System.Security.Cryptography.X509Certificates.X509Certificate2])) {
            $certObj.Dispose()
        }
    }
}

Write-Host "완료: $exe"
