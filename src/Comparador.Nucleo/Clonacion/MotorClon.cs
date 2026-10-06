using System.Diagnostics;
using System.Security.Cryptography;
using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Clonacion;

public enum FaseClon
{
    Copiando,
    Verificando,
}

/// <param name="Total">Null si no se sabe (imagen comprimida): entonces <paramref name="Fraccion"/> sale del archivo comprimido.</param>
public sealed record AvanceClon(FaseClon Fase, long Hechos, long? Total, double? Fraccion, double BytesPorSegundo);

public sealed record ResultadoClon(long Bytes, string Huella, bool Verificado, TimeSpan Duracion);

/// <summary>
/// Clona byte a byte de un extremo a otro: disco, partición o imagen. Calcula el SHA-256 de lo leído mientras
/// escribe y, si se pide, relee el destino entero y compara. Exige administrador si toca un disco.
/// </summary>
public static class MotorClon
{
    private const int Bloque = 4 << 20;
    private static readonly TimeSpan CadaCuantoInformar = TimeSpan.FromMilliseconds(250);

    public static Task<ResultadoClon> ClonarAsync(
        ExtremoClon origen, ExtremoClon destino, bool verificar, IProgress<AvanceClon>? progreso, CancellationToken cancelacion) =>
        Task.Run(() =>
        {
            var discos = ListadoDiscos.Leer();
            using var lectura = AbrirOrigen(origen, discos);
            var problemas = ReglasClon.Revisar(origen, destino, discos, lectura.Largo);
            if (problemas.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, problemas));
            }

            using var escritura = AbrirDestino(destino, discos);

            return Copiar(lectura, escritura, verificar, progreso, cancelacion);
        }, cancelacion);

    /// <summary>Cuánto se va a escribir, si se sabe sin leerlo todo (una imagen .xz o .gz no lo dice hasta el final).</summary>
    public static long? LargoDe(ExtremoClon origen, IReadOnlyList<DiscoFisico> discos)
    {
        if (origen.Tramo(discos) is { } tramo)
        {
            return tramo.Largo;
        }

        using var imagen = OrigenClon.DeImagen(((ExtremoClon.DeImagen)origen).Ruta);

        return imagen.Largo;
    }

    private static OrigenClon AbrirOrigen(ExtremoClon origen, IReadOnlyList<DiscoFisico> discos) => origen.Tramo(discos) is { } tramo
        ? OrigenClon.DeTramo(DiscoCrudo.AbrirLectura(tramo.Disco.Numero), tramo.Inicio, tramo.Largo)
        : OrigenClon.DeImagen(((ExtremoClon.DeImagen)origen).Ruta);

    private static IDestinoClon AbrirDestino(ExtremoClon destino, IReadOnlyList<DiscoFisico> discos) => destino.Tramo(discos) is { } tramo
        ? new DestinoDisco(DiscoCrudo.AbrirEscritura(tramo.Disco.Numero), tramo.Inicio, tramo.Largo, tramo.Disco.TamanoSector)
        : new DestinoArchivo(((ExtremoClon.DeImagen)destino).Ruta);

    internal static ResultadoClon Copiar(OrigenClon origen, IDestinoClon destino, bool verificar, IProgress<AvanceClon>? progreso, CancellationToken cancelacion)
    {
        var reloj = Stopwatch.StartNew();
        var bloque = new byte[Bloque];
        using var huella = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var informe = new Informe(progreso, reloj);
        long hechos = 0;
        int leidos;
        while ((leidos = origen.Datos.ReadAtLeast(bloque, bloque.Length, throwOnEndOfStream: false)) > 0)
        {
            cancelacion.ThrowIfCancellationRequested();
            if (destino.Capacidad is { } capacidad && hechos + leidos > capacidad)
            {
                throw new InvalidOperationException($"No cabe: el destino mide {Formatos.Tamano(capacidad)} y el origen es más grande.");
            }

            huella.AppendData(bloque, 0, leidos);
            destino.Escribir(bloque.AsSpan(0, leidos), hechos);
            hechos += leidos;
            informe.Avisar(FaseClon.Copiando, hechos, origen.Largo, origen.Fraccion);
        }

        var esperada = huella.GetHashAndReset();
        if (verificar)
        {
            Verificar(destino, hechos, esperada, bloque, informe, cancelacion);
        }

        destino.Terminar();

        return new ResultadoClon(hechos, Convert.ToHexString(esperada), verificar, reloj.Elapsed);
    }

    private static void Verificar(IDestinoClon destino, long largo, byte[] esperada, byte[] bloque, Informe informe, CancellationToken cancelacion)
    {
        using var huella = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        informe.Reiniciar();
        for (long hechos = 0; hechos < largo;)
        {
            cancelacion.ThrowIfCancellationRequested();
            var leidos = destino.Leer(bloque.AsSpan(0, (int)Math.Min(bloque.Length, largo - hechos)), hechos);
            if (leidos <= 0)
            {
                throw new CopiaNoIdenticaException();
            }

            huella.AppendData(bloque, 0, leidos);
            hechos += leidos;
            informe.Avisar(FaseClon.Verificando, hechos, largo, null);
        }

        if (!huella.GetHashAndReset().AsSpan().SequenceEqual(esperada))
        {
            throw new CopiaNoIdenticaException();
        }
    }

    /// <summary>Cuatro avisos por segundo como mucho: con bloques de 4 MB y un SSD rápido serían cientos.</summary>
    private sealed class Informe(IProgress<AvanceClon>? progreso, Stopwatch reloj)
    {
        private TimeSpan? ultimo;
        private TimeSpan inicioFase;

        public void Reiniciar()
        {
            inicioFase = reloj.Elapsed;
            ultimo = null;
        }

        public void Avisar(FaseClon fase, long hechos, long? total, double? fraccion)
        {
            var ahora = reloj.Elapsed;
            if (progreso is null || ahora - ultimo < CadaCuantoInformar && hechos != total)
            {
                return;
            }

            ultimo = ahora;
            var segundos = Math.Max((ahora - inicioFase).TotalSeconds, 0.001);
            progreso.Report(new AvanceClon(fase, hechos, total, fraccion, hechos / segundos));
        }
    }
}
