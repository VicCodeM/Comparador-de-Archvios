using Microsoft.Win32;

namespace Comparador.App.Servicios;

/// <summary>
/// Las opciones del clic derecho del Explorador, solo para este usuario (no pide administrador). En Windows 11
/// aparecen en "Mostrar más opciones"; para salir en el primer menú haría falta empaquetar la app con firma.
/// </summary>
public static class IntegracionExplorador
{
    public const string RaizWindows = @"Software\Classes";
    private const string Prefijo = "Comparador.";

    private sealed record Verbo(string Clase, string Clave, string Texto, string Argumentos);

    private static IEnumerable<Verbo> Verbos() =>
    [
        new(@"Directory\Background", "Pegar", "Pegar aquí con Comparador", "pegar \"%V\""),
        new("Directory", "Pegar", "Pegar dentro con Comparador", "pegar \"%1\""),
        new("*", "CopiarA", "Copiar con Comparador a...", "copiar \"%1\""),
        new("Directory", "CopiarA", "Copiar con Comparador a...", "copiar \"%1\""),
        new("*", "MoverA", "Mover con Comparador a...", "mover \"%1\""),
        new("Directory", "MoverA", "Mover con Comparador a...", "mover \"%1\""),
    ];

    private static string RutaVerbo(string raiz, Verbo verbo) => $@"{raiz}\{verbo.Clase}\shell\{Prefijo}{verbo.Clave}";

    /// <param name="raiz">Solo las pruebas cambian la raíz, para no tocar el menú real del usuario.</param>
    public static bool EstaInstalada(string raiz = RaizWindows) =>
        Verbos().All(verbo => Registry.CurrentUser.OpenSubKey(RutaVerbo(raiz, verbo)) is { } clave && Cerrar(clave));

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
    }

    public static void Quitar(string raiz = RaizWindows)
    {
        foreach (var verbo in Verbos())
        {
            Registry.CurrentUser.DeleteSubKeyTree(RutaVerbo(raiz, verbo), throwOnMissingSubKey: false);
        }
    }

    private static bool Cerrar(RegistryKey clave)
    {
        clave.Dispose();

        return true;
    }
}
