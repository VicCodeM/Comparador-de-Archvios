using System.IO;
using Microsoft.Win32;

namespace Comparador.App.Servicios;

/// <summary>
/// Las opciones del clic derecho del Explorador, solo para este usuario (no pide administrador). En Windows 11
/// aparecen en "Mostrar más opciones"; para salir en el primer menú haría falta empaquetar la app con firma.
/// También el menú del arrastre con el botón derecho ("Copiar / Mover aquí con Espejo"), que Windows solo deja
/// añadir con un componente nativo: EspejoExplorador.dll, junto a Espejo.exe.
/// </summary>
public static class IntegracionExplorador
{
    public const string RaizWindows = @"Software\Classes";
    private const string Prefijo = "Comparador.";
    private const string ClaseMenuArrastre = "{7B1C5E2A-3F4D-4E8B-9A61-2C0D5E7F8A93}";
    private const string ComponenteArrastre = "EspejoExplorador.dll";
    private static readonly string[] DestinosArrastre = ["Directory", "Drive"];

    private sealed record Verbo(string Clase, string Clave, string Texto, string Argumentos);

    private static IEnumerable<Verbo> Verbos() =>
    [
        new(@"Directory\Background", "Pegar", "Pegar aquí con Espejo", "pegar \"%V\""),
        new("Directory", "Pegar", "Pegar dentro con Espejo", "pegar \"%1\""),
        new("*", "CopiarA", "Copiar con Espejo a...", "copiar \"%1\""),
        new("Directory", "CopiarA", "Copiar con Espejo a...", "copiar \"%1\""),
        new("*", "MoverA", "Mover con Espejo a...", "mover \"%1\""),
        new("Directory", "MoverA", "Mover con Espejo a...", "mover \"%1\""),
    ];

    private static string RutaVerbo(string raiz, Verbo verbo) => $@"{raiz}\{verbo.Clase}\shell\{Prefijo}{verbo.Clave}";

    /// <param name="raiz">Solo las pruebas cambian la raíz, para no tocar el menú real del usuario.</param>
    private static IEnumerable<string> RutasArrastre(string raiz) =>
        DestinosArrastre.Select(destino => $@"{raiz}\{destino}\shellex\DragDropHandlers\Espejo");

    private static string RutaClase(string raiz) => $@"{raiz}\CLSID\{ClaseMenuArrastre}";

    public static bool EstaInstalada(string raiz = RaizWindows) =>
        Verbos().Select(verbo => RutaVerbo(raiz, verbo)).Concat(RutasArrastre(raiz)).Append(RutaClase(raiz))
            .All(ruta => Registry.CurrentUser.OpenSubKey(ruta) is { } clave && Cerrar(clave));

    /// <summary>Escribe las opciones apuntando a este ejecutable (si la app se movió de carpeta, se corrige solo).</summary>
    public static void Instalar() => Instalar(RaizWindows, Environment.ProcessPath!);

    public static void Instalar(string raiz, string ejecutable)
    {
        foreach (var verbo in Verbos())
        {
            using var clave = Registry.CurrentUser.CreateSubKey(RutaVerbo(raiz, verbo));
            clave.SetValue("MUIVerb", verbo.Texto);
            clave.SetValue("Icon", $"\"{ejecutable}\",0");
            // Con muchos archivos elegidos, Windows igual ofrece la opción (por defecto la esconde pasados 15).
            clave.SetValue("MultiSelectModel", "Player");
            using var comando = clave.CreateSubKey("command");
            comando.SetValue(string.Empty, $"\"{ejecutable}\" {verbo.Argumentos}");
        }

        InstalarMenuArrastre(raiz, Path.Combine(Path.GetDirectoryName(ejecutable)!, ComponenteArrastre));
    }

    private static void InstalarMenuArrastre(string raiz, string componente)
    {
        using (var clase = Registry.CurrentUser.CreateSubKey(RutaClase(raiz)))
        {
            clase.SetValue(string.Empty, "Espejo: menú del arrastre con el botón derecho");
            using var servidor = clase.CreateSubKey("InProcServer32");
            servidor.SetValue(string.Empty, componente);
            servidor.SetValue("ThreadingModel", "Apartment");
        }

        foreach (var ruta in RutasArrastre(raiz))
        {
            using var manejador = Registry.CurrentUser.CreateSubKey(ruta);
            manejador.SetValue(string.Empty, ClaseMenuArrastre);
        }
    }

    public static void Quitar(string raiz = RaizWindows)
    {
        foreach (var verbo in Verbos())
        {
            Registry.CurrentUser.DeleteSubKeyTree(RutaVerbo(raiz, verbo), throwOnMissingSubKey: false);
        }

        foreach (var ruta in RutasArrastre(raiz).Append(RutaClase(raiz)))
        {
            Registry.CurrentUser.DeleteSubKeyTree(ruta, throwOnMissingSubKey: false);
        }
    }

    private static bool Cerrar(RegistryKey clave)
    {
        clave.Dispose();

        return true;
    }
}
