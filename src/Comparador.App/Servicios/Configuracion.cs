using System.IO;
using System.Text.Json;
using Comparador.Nucleo.Modelos;

namespace Comparador.App.Servicios;

public enum TemaApp
{
    Sistema,
    Claro,
    Oscuro,
}

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

    public TemaApp Tema { get; init; } = TemaApp.Sistema;

    /// <summary>Si es true, los hilos se deciden según el dispositivo (USB, red, disco, teléfono).</summary>
    public bool HilosAutomaticos { get; init; } = true;

    public int Hilos { get; init; } = 4;

    /// <summary>Que Windows no se suspenda mientras se compara o se copia (se vuelve a permitir al terminar).</summary>
    public bool EvitarSuspension { get; init; } = true;

    /// <summary>Qué hacer si el archivo ya existe en el destino y es distinto.</summary>
    public ReglaConflicto SiYaExiste { get; init; } = ReglaConflicto.Reemplazar;

    /// <summary>Espejo en segundo plano pega lo que se copia o corta en el Explorador (Ctrl+V), en lugar de Windows.</summary>
    public bool PegarConEspejo { get; init; }

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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            File.WriteAllText(Archivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // No poder guardar las preferencias no debe tumbar la app: se usan las de esta sesión.
        }
    }

    public Configuracion ConRecientes(IEnumerable<ParRutas> usados) => this with
    {
        Recientes = usados.Concat(Recientes).Distinct().Take(MaximoRecientes).ToList(),
    };
}
