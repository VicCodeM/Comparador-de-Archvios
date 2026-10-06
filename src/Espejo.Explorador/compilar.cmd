@echo off
rem Compila EspejoExplorador.dll (x64) en la carpeta indicada. Lo llama Comparador.App.csproj al compilar.
rem Necesita las herramientas de C++ de Visual Studio (Build Tools 2022 o Visual Studio con "Desarrollo de escritorio con C++").
setlocal
set "SALIDA=%~1"
set "VS="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`call "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%i"
if not defined VS (
    echo Faltan las herramientas de C++ de Visual Studio para compilar EspejoExplorador.dll 1>&2
    exit /b 1
)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1 || exit /b 1
set "TEMPORAL=%~dp0obj"
if not exist "%TEMPORAL%" mkdir "%TEMPORAL%"
cl /nologo /utf-8 /LD /O2 /EHsc /std:c++20 /W4 /WX /DUNICODE /D_UNICODE /MT "%~dp0MenuArrastre.cpp" ^
   /Fo"%TEMPORAL%\\" /Fe"%SALIDA%EspejoExplorador.dll" ^
   /link /DEF:"%~dp0EspejoExplorador.def" /IMPLIB:"%TEMPORAL%\EspejoExplorador.lib" shell32.lib ole32.lib user32.lib
