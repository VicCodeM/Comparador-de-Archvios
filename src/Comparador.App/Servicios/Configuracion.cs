using System.IO;
using System.Text.Json;
using Comparador.Nucleo.Modelos;

namespace Comparador.App.Servicios;

/// <summary>
/// Lo que la app recuerda entre usos. Solo opciones que de verdad hacen algo: la versión anterior tenía 92
/// opciones y 81 no se usaban en ninguna parte.
/// </summary>
public sealed record Configuracion
{
    public const int MaximoRecientes = 10;

    public List<ParRutas> Recientes { get; init; } = [];

    public bool ModoExacto { get; init; }

    public bool DetectarSobrantes { get; init; } = true;

    public string Exclusiones { get; init; } = string.Empty;

    public bool VerificarCopias { get; init; } = true;

    public bool TemaOscuro { get; init; }

    private static readonly string Archivo = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VMSofts", "ComparadorArchivos", "configuracion.json");

    public static Configuracion Cargar()
    {
        try
        {
            return File.Exists(Archivo) ? JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(Archivo)) ?? new() : new();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Guardar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
        File.WriteAllText(Archivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public Configuracion ConRecientes(IEnumerable<ParRutas> usados) => this with
    {
        Recientes = usados.Concat(Recientes).Distinct().Take(MaximoRecientes).ToList(),
    };
}
