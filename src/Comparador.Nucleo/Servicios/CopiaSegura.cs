using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>El archivo está abierto por otro programa; dice cuál.</summary>
public sealed class ArchivoEnUsoException(string programas, Exception interna)
    : IOException($"Está abierto en {programas}", interna);

/// <summary>La copia no es idéntica al origen según SHA-256.</summary>
public sealed class CopiaNoIdenticaException() : IOException("La verificación SHA-256 falló: la copia no es idéntica al origen");

/// <summary>
/// Copia un archivo sin dejarlo a medias: primero a un temporal junto al destino, calculando el SHA-256 del origen en
/// esa misma lectura; si se pide, relee el temporal y compara huellas; y solo entonces reemplaza el destino de una vez.
/// Funciona entre cualquier par de ubicaciones (disco, USB, red, teléfono).
/// </summary>
public static class CopiaSegura
{
    public const string ExtensionTemporal = ".comparador-tmp";
    private const int Reintentos = 3;
    private const int HResultEnUso = unchecked((int)0x80070020);
    private const int HResultBloqueoParcial = unchecked((int)0x80070021);

    public static async Task CopiarAsync(
        IUbicacion origen, IUbicacion destino, string relativa, bool verificar,
        ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                await IntentarCopiarAsync(origen, destino, relativa, verificar, archivo, progreso, cancelacion);
                return;
            }
            catch (Exception error) when (EstaEnUso(error, origen, destino, relativa) && intento < Reintentos)
            {
                archivo.Reiniciar($"En uso, reintento {intento + 1} de {Reintentos}");
                await Task.Delay(TimeSpan.FromMilliseconds(500 * intento), cancelacion);
            }
            catch (Exception error) when (EstaEnUso(error, origen, destino, relativa))
            {
                throw new ArchivoEnUsoException(DetectorBloqueos.Describir(RutaLocal(origen, relativa), RutaLocal(destino, relativa)), error);
            }
        }
    }

    /// <summary>
    /// Reemplazar un archivo que otro programa tiene abierto NO da "en uso": Windows responde "acceso denegado".
    /// Por eso, ante un acceso denegado se pregunta a Windows si alguien lo tiene abierto antes de culpar a los permisos.
    /// </summary>
    private static bool EstaEnUso(Exception error, IUbicacion origen, IUbicacion destino, string relativa) => error switch
    {
        IOException io => io.HResult is HResultEnUso or HResultBloqueoParcial,
        UnauthorizedAccessException => DetectorBloqueos.QuienLoUsa(RutaLocal(destino, relativa)).Count > 0
            || DetectorBloqueos.QuienLoUsa(RutaLocal(origen, relativa)).Count > 0,
        _ => false,
    };

    private static string RutaLocal(IUbicacion ubicacion, string relativa) =>
        ubicacion.Tipo == TipoUbicacion.Telefono ? string.Empty : Path.Combine(ubicacion.Raiz, relativa);

    private static async Task IntentarCopiarAsync(
        IUbicacion origen, IUbicacion destino, string relativa, bool verificar,
        ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var temporal = relativa + ExtensionTemporal;
        destino.CrearCarpeta(Path.GetDirectoryName(relativa) ?? string.Empty);
        try
        {
            archivo.Reiniciar("Copiando");
            string huellaOrigen;
            await using (var lectura = new FlujoMedido(origen.AbrirLectura(relativa), leidos => progreso.SumarBytes(archivo, leidos)))
            {
                await destino.EscribirAsync(temporal, lectura, cancelacion);
                huellaOrigen = lectura.Huella();
            }

            if (verificar)
            {
                archivo.Reiniciar("Verificando SHA-256");
                var huellaCopia = await CalculadoraHash.CalcularAsync(destino, temporal, leidos => progreso.SumarBytes(archivo, leidos), cancelacion);
                if (huellaCopia != huellaOrigen)
                {
                    throw new CopiaNoIdenticaException();
                }
            }

            destino.Reemplazar(temporal, relativa, origen.LeerMetadatos(relativa));
        }
        catch
        {
            BorrarTemporal(destino, temporal);
            throw;
        }
    }

    private static void BorrarTemporal(IUbicacion destino, string temporal)
    {
        try
        {
            destino.BorrarArchivo(temporal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Si no se puede borrar el temporal no es grave: se sobrescribe en la siguiente copia.
        }
    }
}
