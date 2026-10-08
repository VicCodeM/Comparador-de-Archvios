using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>El archivo está abierto por otro programa; dice cuál.</summary>
public sealed class ArchivoEnUsoException(string programas, Exception interna)
    : IOException($"Está abierto en {programas}", interna);

/// <summary>La copia no es idéntica al origen según SHA-256.</summary>
public sealed class CopiaNoIdenticaException() : IOException("La verificación SHA-256 falló: la copia no es idéntica al origen");

/// <summary>Se apagó la verificación mientras se releía el archivo: la copia se da por buena sin esperar.</summary>
internal sealed class VerificacionApagadaException() : OperationCanceledException("Se apagó la verificación");

/// <summary>
/// Copia un archivo sin dejarlo a medias: primero a un temporal junto al destino, calculando el SHA-256 del origen en
/// esa misma lectura; si se pide, relee el temporal y compara huellas; y solo entonces reemplaza el destino de una vez.
/// Funciona entre cualquier par de ubicaciones (disco, USB, red, teléfono).
/// </summary>
public static class CopiaSegura
{
    public const string ExtensionTemporal = ".comparador-tmp";
    private const int Reintentos = 3;

    /// <summary>
    /// Desde este tamaño se copia sin pasar por la memoria de Windows: un archivo de varios GB la llenaría y la copia
    /// se frenaría a la mitad. Los pequeños sí ganan con ella.
    /// </summary>
    private const long UmbralSinCache = 256L * 1024 * 1024;
    private const int HResultEnUso = unchecked((int)0x80070020);
    private const int HResultBloqueoParcial = unchecked((int)0x80070021);

    /// <param name="relativa">El archivo en el origen.</param>
    /// <param name="relativaDestino">Dónde queda en el destino: casi siempre la misma ruta, o "nombre (2).ext" si se conservan los dos.</param>
    /// <param name="verificar">
    /// Se consulta en cada bloque: apagarlo con la copia en marcha corta al instante la verificación en curso (Victor la
    /// apagaba y los archivos que ya verificaban seguían haciéndolo hasta el final, 2026-10-08).
    /// </param>
    /// <returns>True si la copia quedó verificada con SHA-256.</returns>
    public static async Task<bool> CopiarAsync(
        IUbicacion origen, IUbicacion destino, string relativa, string relativaDestino, Func<bool> verificar,
        ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                return await IntentarCopiarAsync(origen, destino, relativa, relativaDestino, verificar, archivo, progreso, cancelacion);
            }
            catch (Exception error) when (EstaEnUso(error, origen, destino, relativa, relativaDestino) && intento < Reintentos)
            {
                archivo.Reiniciar($"En uso, reintento {intento + 1} de {Reintentos}");
                await Task.Delay(TimeSpan.FromMilliseconds(500 * intento), cancelacion);
            }
            catch (Exception error) when (EstaEnUso(error, origen, destino, relativa, relativaDestino))
            {
                throw new ArchivoEnUsoException(DetectorBloqueos.Describir(RutaLocal(origen, relativa), RutaLocal(destino, relativaDestino)), error);
            }
            catch (CopiaNoIdenticaException) when (intento < Reintentos)
            {
                // Un tropiezo de un cable USB o de la red se arregla copiando otra vez; tres fallos seguidos ya son el
                // disco o la memoria, y entonces sí se avisa. El destino nunca se tocó: la copia mala era el temporal.
                archivo.Reiniciar($"La copia no salió idéntica, reintento {intento + 1} de {Reintentos}");
            }
        }
    }

    /// <summary>
    /// Reemplazar un archivo que otro programa tiene abierto NO da "en uso": Windows responde "acceso denegado".
    /// Por eso, ante un acceso denegado se pregunta a Windows si alguien lo tiene abierto antes de culpar a los permisos.
    /// </summary>
    private static bool EstaEnUso(Exception error, IUbicacion origen, IUbicacion destino, string relativa, string relativaDestino) => error switch
    {
        IOException io => io.HResult is HResultEnUso or HResultBloqueoParcial,
        UnauthorizedAccessException => DetectorBloqueos.QuienLoUsa(RutaLocal(destino, relativaDestino)).Count > 0
            || DetectorBloqueos.QuienLoUsa(RutaLocal(origen, relativa)).Count > 0,
        _ => false,
    };

    private static string RutaLocal(IUbicacion ubicacion, string relativa) =>
        ubicacion.Tipo == TipoUbicacion.Telefono ? string.Empty : Path.Combine(ubicacion.Raiz, relativa);

    private static (UbicacionDisco Origen, UbicacionDisco Destino)? EntreDiscos(IUbicacion origen, IUbicacion destino) =>
        origen is UbicacionDisco discoOrigen && destino is UbicacionDisco discoDestino ? (discoOrigen, discoDestino) : null;

    private static async Task<bool> IntentarCopiarAsync(
        IUbicacion origen, IUbicacion destino, string relativa, string relativaDestino, Func<bool> verificar,
        ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var temporal = relativaDestino + ExtensionTemporal;
        destino.CrearCarpeta(Path.GetDirectoryName(relativaDestino) ?? string.Empty);
        try
        {
            archivo.Reiniciar("Copiando");
            if (EntreDiscos(origen, destino) is { } discos)
            {
                var verificada = await CopiarEntreDiscosAsync(discos.Origen, discos.Destino, relativa, temporal, verificar, archivo, progreso, cancelacion);
                // La copia de Windows ya trae fechas y atributos: volver a leerlos y ponerlos solo costaría tiempo por archivo.
                destino.Reemplazar(temporal, relativaDestino, MetadatosArchivo.YaCopiados);

                return verificada;
            }

            var verificadaPorFlujo = await CopiarPorFlujoAsync(origen, destino, relativa, temporal, verificar, archivo, progreso, cancelacion);
            destino.Reemplazar(temporal, relativaDestino, origen.LeerMetadatos(relativa));

            return verificadaPorFlujo;
        }
        catch
        {
            BorrarTemporal(destino, temporal);
            throw;
        }
    }

    /// <summary>
    /// Disco, USB o red en los dos lados: copia Windows (lo más rápido, y en un mismo servidor copia el servidor) y,
    /// si se verifica, se leen origen y copia del disco mismo y se comparan las huellas.
    /// </summary>
    private static async Task<bool> CopiarEntreDiscosAsync(
        UbicacionDisco origen, UbicacionDisco destino, string relativa, string temporal, Func<bool> verificar,
        ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var rutaOrigen = origen.RutaIO(relativa);
        var rutaTemporal = destino.RutaIO(temporal);
        await CopiaNativa.CopiarAsync(rutaOrigen, rutaTemporal, archivo.Tamano >= UmbralSinCache, bytes => progreso.SumarBytes(archivo, bytes), cancelacion);
        if (!verificar())
        {
            return false;
        }

        archivo.Reiniciar("Verificando SHA-256");
        void Sumar(int leidos) => SumarVerificacion(archivo, progreso, leidos, lecturas: 2, verificar);
        string huellaOrigen, huellaCopia;
        try
        {
            if (MismoDiscoMecanico(origen, destino))
            {
                // Un cabezal saltando entre dos archivos a la vez va mucho más lento que leerlos uno tras otro.
                huellaOrigen = await CalculadoraHash.CalcularDelDiscoAsync(rutaOrigen, Sumar, cancelacion);
                huellaCopia = await CalculadoraHash.CalcularDelDiscoAsync(rutaTemporal, Sumar, cancelacion);
            }
            else
            {
                var huellas = await Task.WhenAll(
                    CalculadoraHash.CalcularDelDiscoAsync(rutaOrigen, Sumar, cancelacion),
                    CalculadoraHash.CalcularDelDiscoAsync(rutaTemporal, Sumar, cancelacion));
                (huellaOrigen, huellaCopia) = (huellas[0], huellas[1]);
            }
        }
        catch (VerificacionApagadaException)
        {
            return false;
        }

        return huellaCopia == huellaOrigen ? true : throw new CopiaNoIdenticaException();
    }

    /// <summary>Avanza la fila y, si se apagó la verificación, la corta ahí mismo.</summary>
    private static void SumarVerificacion(ArchivoEnCurso archivo, ProgresoOperacion progreso, int leidos, int lecturas, Func<bool> verificar)
    {
        progreso.SumarVerificacion(archivo, leidos, lecturas);
        if (!verificar())
        {
            throw new VerificacionApagadaException();
        }
    }

    private static bool MismoDiscoMecanico(UbicacionDisco origen, UbicacionDisco destino) =>
        origen.Perfil.NumeroDisco is { } numero && numero == destino.Perfil.NumeroDisco && origen.Perfil.Medio == MedioDisco.Mecanico;

    /// <summary>Con un teléfono de por medio: se lee y escribe a mano, calculando la huella del origen en la misma lectura.</summary>
    private static async Task<bool> CopiarPorFlujoAsync(
        IUbicacion origen, IUbicacion destino, string relativa, string temporal, Func<bool> verificar,
        ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        string huellaOrigen;
        await using (var lectura = new FlujoMedido(origen.AbrirLectura(relativa), leidos => progreso.SumarBytes(archivo, leidos), calcularHuella: verificar()))
        {
            await destino.EscribirAsync(temporal, lectura, cancelacion);
            huellaOrigen = lectura.Huella();
        }

        if (!verificar())
        {
            return false;
        }

        archivo.Reiniciar("Verificando SHA-256");
        try
        {
            var huellaCopia = await CalculadoraHash.CalcularAsync(
                destino, temporal, leidos => SumarVerificacion(archivo, progreso, leidos, lecturas: 1, verificar), cancelacion);

            return huellaCopia == huellaOrigen ? true : throw new CopiaNoIdenticaException();
        }
        catch (VerificacionApagadaException)
        {
            return false;
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
