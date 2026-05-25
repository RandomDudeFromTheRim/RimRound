# RimRound build script for Windows (PowerShell)
$ErrorActionPreference = "Stop"

$ModDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RwDir  = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"

Write-Host "RimRound build script"
Write-Host "Mod dir: $ModDir"
Write-Host "RimWorld dir: $RwDir"

# Verify this is the RimRound mod root
if (-not (Test-Path "$ModDir\About\About.xml")) {
    Write-Host "ERROR: build.ps1 must be in RimWorld\Mods\RimRound\"
    Pause; exit 1
}

# Find csc.exe — REJECT old C#5 .NET Framework csc entirely
$csc = $null
# 1) Check VS Build Tools paths first
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -products * -latest -property installationPath
    if ($vsPath) {
        $try = "$vsPath\MSBuild\Current\Bin\Roslyn\csc.exe"
        if (Test-Path $try) { $csc = $try }
    }
}
# 2) If VS not found via vswhere, try known paths
if (-not $csc) {
    $common = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
    )
    foreach ($p in $common) { if (Test-Path $p) { $csc = $p; break } }
}
# 3) Last resort: csc from PATH, but only if it's NOT the old Framework one
if (-not $csc) {
    $pathCsc = Get-Command csc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ($pathCsc -and $pathCsc -notmatch "Microsoft\.NET\\Framework\\v4\.0") {
        $csc = $pathCsc
    }
}
if (-not $csc) {
    Write-Host "ERROR: Roslyn compiler not found."
    Write-Host "Install Visual Studio Build Tools: https://aka.ms/vs/17/release/vs_BuildTools.exe"
    Write-Host "Select workload: '.NET desktop build tools'"
    Pause; exit 1
}

# Check RimWorld DLLs
if (-not (Test-Path "$RwDir\Assembly-CSharp.dll")) {
    Write-Host "ERROR: Can't find RimWorld DLLs at $RwDir"
    Pause; exit 1
}

# Find netstandard.dll
$netstdPath = $null
$netstdCandidates = @(
    "$RwDir\netstandard.dll",
    "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\netstandard.dll",
    "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\netstandard.dll",
    "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6.2\netstandard.dll",
    "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5\netstandard.dll",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
)
foreach ($p in $netstdCandidates) {
    if (Test-Path $p) { $netstdPath = $p; break }
}
if (-not $netstdPath) {
    Write-Host "WARNING: netstandard.dll not found. Trying search..."
    $found = Get-ChildItem -Path "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework" -Filter "netstandard.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $netstdPath = $found.FullName }
}
if (-not $netstdPath) {
    $found = Get-ChildItem -Path "C:\Windows\Microsoft.NET\Framework" -Filter "netstandard.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $netstdPath = $found.FullName }
}
if ($netstdPath) {
    Write-Host "netstandard.dll: $netstdPath"
} else {
    Write-Host "ERROR: netstandard.dll not found anywhere. Install .NET Framework SDK."
    Pause; exit 1
}

# Gather source files
$src1 = "$ModDir\Source\RimRound\RimRound"
$src2 = "$ModDir\Source\RimRound\FeedingTube"
$srcFiles = @()
if (Test-Path $src1) { $srcFiles += Get-ChildItem $src1 -Filter *.cs -Recurse | Where { $_.Name -notmatch "AssemblyInfo" } }
if (Test-Path $src2) { $srcFiles += Get-ChildItem $src2 -Filter *.cs -Recurse }
Write-Host "Found $($srcFiles.Count) source files."

# Build response file (in TEMP to avoid spaces in path)
$rspFile = "$env:TEMP\_rimround_build.rsp"
$outDll  = "$ModDir\Assemblies\RimRound.dll"
New-Item -Force -ItemType Directory "$ModDir\Assemblies" | Out-Null

@"
-target:library
-out:"$outDll"
-noconfig
-define:DEBUG;TRACE
-optimize+
/reference:"$RwDir\Assembly-CSharp.dll"
/reference:"$RwDir\Assembly-CSharp-firstpass.dll"
/reference:"$RwDir\UnityEngine.dll"
/reference:"$RwDir\UnityEngine.CoreModule.dll"
/reference:"$RwDir\UnityEngine.IMGUIModule.dll"
/reference:"$RwDir\UnityEngine.TextRenderingModule.dll"
/reference:"$RwDir\UnityEngine.InputLegacyModule.dll"
/reference:"$netstdPath"
/reference:"$ModDir\Dev\harmony16\Current\Assemblies\0Harmony.dll"
/reference:"$ModDir\Dev\har\1.6\Assemblies\AlienRace.dll"
/reference:"$ModDir\Dev\PipeSystem\1.6\Assemblies\PipeSystem.dll"
/reference:"$ModDir\Dev\VEF\1.6\Assemblies\VEF.dll"
"@ | Out-File -FilePath $rspFile -Encoding utf8

foreach ($f in $srcFiles) {
    "`"$($f.FullName)`"" | Out-File -FilePath $rspFile -Append -Encoding utf8
}

Write-Host "Building..."

$proc = Start-Process -FilePath $csc -ArgumentList "@$rspFile" -Wait -NoNewWindow -PassThru

Remove-Item $rspFile -ErrorAction SilentlyContinue

if ($proc.ExitCode -eq 0) {
    Copy-Item "$outDll" "$ModDir\1.6\Assemblies\RimRound.dll" -Force
    Write-Host "============================"
    Write-Host " BUILD SUCCESSFUL"
    Write-Host "============================"
    Get-Item $outDll | Select-Object Name, Length | Format-List
} else {
    Write-Host "============================"
    Write-Host " BUILD FAILED  (code: $($proc.ExitCode))"
    Write-Host "============================"
}

Pause
