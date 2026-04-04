# QMan 포터블(오프라인) 빌드 — dist 폴더에 self-contained 출력 생성
# 사용: .\scripts\publish-portable.ps1
#       .\scripts\publish-portable.ps1 -Rid win-arm64   # ARM64 PC용

param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string] $Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outName = "QMan-portable-$Rid"
$outDir = Join-Path $root "dist\$outName"

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

    Get-ChildItem $outDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem $outDir -Directory -ErrorAction SilentlyContinue | Where-Object {
        -not (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1)
    } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "출력: $outDir"
Write-Host "배포: 위 폴더를 ZIP으로 압축해 반입한 뒤, 압축 해제 폴더의 QMan.exe 를 실행하면 됩니다."
Write-Host "벡터검색: sqlite-vec DLL은 빌드 전 QMan.App\native\ 에 두면 출력에 포함됩니다."
