using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Comparador.App.Servicios;

/// <summary>Lo que la app le pide a Windows: elegir carpetas, guardar archivos, abrir y mostrar en el Explorador.</summary>
public static class Escritorio
{
    /// <summary>El diálogo de Windows ya deja elegir memorias USB, unidades de red y rutas \\servidor\carpeta.</summary>
    public static string? ElegirCarpeta(string titulo, string? inicial = null)
    {
        var dialogo = new OpenFolderDialog { Title = titulo, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(inicial))
        {
            dialogo.InitialDirectory = inicial;
        }

        return dialogo.ShowDialog() == true ? dialogo.FolderName : null;
    }

    /// <summary>Archivos sueltos (varios a la vez). Vacío si se canceló.</summary>
    public static IReadOnlyList<string> ElegirArchivos(string titulo)
    {
        var dialogo = new OpenFileDialog { Title = titulo, Multiselect = true };

        return dialogo.ShowDialog() == true ? dialogo.FileNames : [];
    }

    /// <summary>Carpetas (varias a la vez). Vacío si se canceló.</summary>
    public static IReadOnlyList<string> ElegirCarpetas(string titulo)
    {
        var dialogo = new OpenFolderDialog { Title = titulo, Multiselect = true };

        return dialogo.ShowDialog() == true ? dialogo.FolderNames : [];
    }

    public static string? ElegirDondeGuardar(string titulo, string nombre, string filtro)
    {
        var dialogo = new SaveFileDialog { Title = titulo, FileName = nombre, Filter = filtro };

        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    }

    public static void Abrir(string ruta) => Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });

    /// <summary>Abre el Explorador con el archivo marcado; si no existe, abre la carpeta más cercana que sí exista.</summary>
    public static void MostrarEnCarpeta(string ruta)
    {
        if (File.Exists(ruta) || Directory.Exists(ruta))
        {
            Process.Start("explorer.exe", $"/select,\"{ruta}\"");
            return;
        }

        var carpeta = Path.GetDirectoryName(ruta);
        while (carpeta is not null && !Directory.Exists(carpeta))
        {
            carpeta = Path.GetDirectoryName(carpeta);
        }

        if (carpeta is not null)
        {
            Process.Start("explorer.exe", $"\"{carpeta}\"");
        }
    }
}
