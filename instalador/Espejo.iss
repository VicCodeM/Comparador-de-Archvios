; Instalador de Espejo (Inno Setup 6). Se genera con instalador\compilar.ps1, que antes publica la app.
; Creado por Ing. Victor Maldonado - VMSofts

#define Nombre "Espejo"
#define Version GetVersionNumbersString("..\publicar\Espejo.exe")
#define Ejecutable "Espejo.exe"

[Setup]
AppId={{6B8E2F4A-1C3D-4E5F-9A7B-2D4C6E8F0A1B}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher=VMSofts - Ing. Victor Maldonado
AppPublisherURL=https://vmsofts.com
AppCopyright=VMSofts © 2026
VersionInfoVersion={#Version}
VersionInfoDescription={#Nombre} - Comparador y clonador
DefaultDirName={autopf}\{#Nombre}
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#Ejecutable}
UninstallDisplayName={#Nombre} - Comparador y clonador
SetupIconFile=..\src\Comparador.App\app.ico
OutputDir=salida
OutputBaseFilename=Espejo-Setup-{#Version}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; Espejo puede estar junto al reloj (Pegar con Espejo): se cierra para actualizarlo y se vuelve a abrir.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Acceso directo en el escritorio"; GroupDescription: "Accesos:"
Name: "menu"; Description: "Clic derecho del Explorador: ""Copiar / Mover con Espejo a..."", ""Pegar aquí con Espejo"" y el menú al arrastrar con el botón derecho"; GroupDescription: "Explorador de Windows:"
Name: "pegar"; Description: "Pegar con Espejo: Ctrl+V del Explorador lo pega Espejo (queda junto al reloj y arranca con Windows)"; GroupDescription: "Explorador de Windows:"; Flags: unchecked

[Files]
; El menú del arrastre lo tiene cargado el Explorador: si está en uso, se reemplaza al reiniciar.
Source: "..\publicar\EspejoExplorador.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "..\publicar\*"; Excludes: "EspejoExplorador.dll,*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"
Name: "{group}\Desinstalar {#Nombre}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"; Tasks: escritorio

[Run]
; Como el usuario que instala, no como administrador: las opciones van en su registro (HKCU).
Filename: "{app}\{#Ejecutable}"; Parameters: "--instalar menu"; Tasks: menu; Flags: runasoriginaluser waituntilterminated
Filename: "{app}\{#Ejecutable}"; Parameters: "--instalar pegar"; Tasks: pegar; Flags: runasoriginaluser waituntilterminated
Filename: "{app}\{#Ejecutable}"; Parameters: "--residente"; Tasks: pegar; Flags: runasoriginaluser nowait
Filename: "{app}\{#Ejecutable}"; Description: "Abrir {#Nombre}"; Flags: runasoriginaluser nowait postinstall skipifsilent

[UninstallRun]
; Nada de Espejo queda en el Explorador ni en el arranque de Windows.
Filename: "{app}\{#Ejecutable}"; Parameters: "--desinstalar"; RunOnceId: "QuitarIntegracion"; Flags: waituntilterminated
