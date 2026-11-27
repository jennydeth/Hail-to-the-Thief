# Builds the launcher (launcher\Launcher.cs) into target\launcher\Foxhound.exe with the C# compiler of the
# .NET Framework that Windows 10 and 11 have; nothing to install.
#   .\scripts\build-launcher.ps1
#   .\scripts\build-launcher.ps1 -Package     also builds victory.exe (release, without this PC's paths in it)
#                                             and puts both, a README.txt and the licence into
#                                             target\foxhound-windows-x64 and its .zip
param([switch]$Package)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "No $csc (the .NET Framework 4 compiler)." }
$out = Join-Path $repo 'target\launcher'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'Foxhound.exe'
& $csc -nologo -target:winexe -platform:x64 -optimize+ "-out:$exe" -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll (Join-Path $repo 'launcher\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'the launcher did not compile' }
"built $exe"
if ($Package) {
    # The layer, built here so that no path of this PC is left in the program (the compiler writes source paths
    # into it for its error messages): into a target folder of its own, so the usual one keeps its build.
    $env:RUSTFLAGS = "--remap-path-prefix=$($env:USERPROFILE)=~ --remap-path-prefix=$repo=."
    Push-Location $repo
    # (cargo writes its progress to the error stream: that must not end this script)
    $ErrorActionPreference = 'Continue'
    try { & cargo build --release --locked -p victory --target-dir (Join-Path $repo 'target\dist'); $built = $LASTEXITCODE }
    finally { Pop-Location; Remove-Item Env:RUSTFLAGS; $ErrorActionPreference = 'Stop' }
    if ($built -ne 0) { throw 'the layer did not build' }
    $victory = Join-Path $repo 'target\dist\release\victory.exe'
    $dir = Join-Path $repo 'target\foxhound-windows-x64'
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item $exe, $victory $dir
    Copy-Item (Join-Path $repo 'launcher\README.txt') $dir
    Copy-Item (Join-Path $repo 'LICENSE') $dir
    $zip = "$dir.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $zip
    Get-ChildItem $dir, $zip | ForEach-Object { '{0,12:N0}  {1}  {2}' -f $_.Length, (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name }
}
