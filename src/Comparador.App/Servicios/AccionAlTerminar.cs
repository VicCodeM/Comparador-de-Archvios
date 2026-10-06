using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Comparador.App.Servicios;

public enum AlTerminar
{
    Nada,
    CerrarVentana,
    Suspender,
    Apagar,
}

/// <summary>Lo que hace el equipo cuando termina una copia larga (por ejemplo, de noche).</summary>
public static class AccionAlTerminar
{
    /// <summary>Margen para arrepentirse: Windows avisa y "shutdown /a" lo cancela.</summary>
    private const int SegundosAntesDeApagar = 60;

    public static void Ejecutar(AlTerminar accion)
    {
        switch (accion)
        {
            case AlTerminar.Suspender:
                SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                break;
            case AlTerminar.Apagar:
                Process.Start(new ProcessStartInfo("shutdown", $"/s /t {SegundosAntesDeApagar} /c \"La copia terminó. El equipo se apagará en un minuto.\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                break;
        }
    }

    public static string Nombre(AlTerminar accion) => accion switch
    {
        AlTerminar.CerrarVentana => "Cerrar esta ventana",
        AlTerminar.Suspender => "Suspender el equipo",
        AlTerminar.Apagar => "Apagar el equipo",
        _ => "No hacer nada",
    };

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate, [MarshalAs(UnmanagedType.Bool)] bool forceCritical, [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
}
