# Genera instalador\salida\Espejo-Setup-<versión>.exe: publica Espejo (con .NET incluido) y lo empaqueta con Inno Setup.
# Necesita: SDK de .NET 10, herramientas de C++ de Visual Studio (menú del arrastre) e Inno Setup 6
# (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Falta Inno Setup 6: winget install JRSoftware.InnoSetup' }

Remove-Item "$raiz\publicar" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$raiz\src\Comparador.App" -c Release -r win-x64 --self-contained -o "$raiz\publicar"
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de Espejo.' }

& $iscc "$PSScriptRoot\Espejo.iss"
if ($LASTEXITCODE -ne 0) { throw 'Falló Inno Setup.' }
Get-ChildItem "$PSScriptRoot\salida" -Filter 'Espejo-Setup-*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { "Instalador: $($_.FullName)" }
