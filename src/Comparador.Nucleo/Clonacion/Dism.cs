using System.Globalization;
using System.Text.RegularExpressions;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Copia el contenido de un volumen NTFS con DISM, la herramienta con la que Microsoft instala Windows en fábrica:
/// conserva permisos, enlaces duros, puntos de reanálisis y flujos alternos, y deja fuera pagefile, hiberfil y la
/// carpeta de instantáneas. Se captura a un .wim temporal y se aplica en la partición nueva, del tamaño que sea.
/// </summary>
internal static partial class Dism
{
    public static void Capturar(string carpeta, string imagen, Action<double> avance, CancellationToken cancelacion) =>
        HerramientaWindows.Ejecutar("dism.exe",
            $"/Capture-Image /ImageFile:\"{imagen}\" /CaptureDir:\"{carpeta.TrimEnd('\\')}\" /Name:Espejo /Compress:fast /CheckIntegrity",
            linea => Leer(linea, avance), cancelacion);

    /// <param name="raiz">"X:\" sin comillas: una barra antes de la comilla final la escaparía.</param>
    public static void Aplicar(string imagen, string raiz, Action<double> avance, CancellationToken cancelacion) =>
        HerramientaWindows.Ejecutar("dism.exe",
            $"/Apply-Image /ImageFile:\"{imagen}\" /Index:1 /ApplyDir:{raiz} /CheckIntegrity",
            linea => Leer(linea, avance), cancelacion);

    /// <summary>La barra de DISM: "[====       45.0%            ]" (con coma decimal en Windows en español).</summary>
    private static void Leer(string linea, Action<double> avance)
    {
        var porcentaje = Porcentaje().Match(linea);
        if (porcentaje.Success)
        {
            avance(double.Parse(porcentaje.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) / 100);
        }
    }

    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d)?)%")]
    private static partial Regex Porcentaje();
}

/// <summary>El arranque del disco clonado, rehecho con bcdboot para que apunte a sus particiones y no a las del origen.</summary>
internal static class Arranque
{
    public static bool TieneWindows(string raiz) => File.Exists(Path.Combine(raiz, @"Windows\System32\config\SYSTEM"));

    /// <param name="sistema">La partición de arranque: la EFI en GPT, la activa en MBR.</param>
    public static void Crear(string raizWindows, string sistema, bool uefi) =>
        HerramientaWindows.Ejecutar("bcdboot.exe", $"{Path.Combine(raizWindows, "Windows")} /s {sistema.TrimEnd('\\')} /f {(uefi ? "UEFI" : "BIOS")}");
}
