# Builds bin\MathKeyboard.exe with the C# compiler that ships with Windows
# (.NET Framework 4), so no SDK or extra tooling is needed.
#
#   powershell -ExecutionPolicy Bypass -File .\build.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { Write-Error "csc.exe not found: $csc"; exit 1 }

$bin = Join-Path $root "bin"
$exe = Join-Path $bin "MathKeyboard.exe"
$ico = Join-Path $bin "MathKeyboard.ico"
$logo = Join-Path $root "assets\logo.png"
$src = Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName }
New-Item -ItemType Directory -Force $bin | Out-Null

function Build($extra) {
    & $csc /nologo /target:winexe /codepage:65001 /optimize+ $extra `
        /out:$exe `
        /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
        $src
}

# pass 1: build without an icon, then let the app draw its own icon (.ico) and logo (.png)
Build @()
if ($LASTEXITCODE -ne 0) { exit 1 }
Start-Process -FilePath $exe -ArgumentList "--icon", "`"$ico`"", "`"$logo`"" -Wait

# pass 2: rebuild with the icon embedded in the executable
Build @("/win32icon:$ico")
if ($LASTEXITCODE -eq 0) { Write-Host "Built $exe" }