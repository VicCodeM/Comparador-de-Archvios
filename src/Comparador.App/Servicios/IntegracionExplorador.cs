using Microsoft.Win32;

namespace Comparador.App.Servicios;

/// <summary>
/// Las opciones del clic derecho del Explorador, solo para este usuario (no pide administrador). En Windows 11
/// aparecen en "Mostrar más opciones"; para salir en el primer menú haría falta empaquetar la app con firma.
/// </summary>
public static class IntegracionExplorador
{
    private const string Raiz = @"Software\Classes";
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

    private static string RutaVerbo(Verbo verbo) => $@"{Raiz}\{verbo.Clase}\shell\{Prefijo}{verbo.Clave}";

    public static bool EstaInstalada() => Verbos().All(verbo => Registry.CurrentUser.OpenSubKey(RutaVerbo(verbo)) is { } clave && Cerrar(clave));

    /// <summary>Escribe las opciones apuntando a este ejecutable (si la app se movió de carpeta, se corrige solo).</summary>
    public static void Instalar()
    {
        var ejecutable = Environment.ProcessPath!;
        foreach (var verbo in Verbos())
        {
            using var clave = Registry.CurrentUser.CreateSubKey(RutaVerbo(verbo));
            clave.SetValue("MUIVerb", verbo.Texto);
            clave.SetValue("Icon", $"\"{ejecutable}\",0");
            // Con muchos archivos elegidos, Windows igual ofrece la opción (por defecto la esconde pasados 15).
            clave.SetValue("MultiSelectModel", "Player");
            using var comando = clave.CreateSubKey("command");
            comando.SetValue(string.Empty, $"\"{ejecutable}\" {verbo.Argumentos}");
        }
    }

    public static void Quitar()
    {
        foreach (var verbo in Verbos())
        {
            Registry.CurrentUser.DeleteSubKeyTree(RutaVerbo(verbo), throwOnMissingSubKey: false);
        }
    }

    private static bool Cerrar(RegistryKey clave)
    {
        clave.Dispose();

        return true;
    }
}
