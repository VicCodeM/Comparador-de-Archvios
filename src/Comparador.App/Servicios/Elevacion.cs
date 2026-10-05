using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Comparador.App.Servicios;

/// <summary>Saber si la app corre como administrador y volver a abrirla así cuando el usuario lo pide.</summary>
public static class Elevacion
{
    private const int CanceladoPorUsuario = 1223;

    public static bool EsAdministrador()
    {
        using var identidad = WindowsIdentity.GetCurrent();

        return new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Abre otra copia como administrador (Windows pregunta). False si el usuario dijo que no.</summary>
    public static bool ReiniciarElevado()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });

            return true;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == CanceladoPorUsuario)
        {
            return false;
        }
    }
}
