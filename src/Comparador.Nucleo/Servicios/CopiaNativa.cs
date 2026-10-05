using System.Runtime.InteropServices;

namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Copia un archivo con CopyFile2, la función de copia del propio Windows (la del Explorador; no es un programa
/// externo). Es lo más rápido entre discos y, entre carpetas del mismo servidor, el servidor copia solo sin que los
/// datos pasen por este equipo. Avisa del avance por bloques, se puede cancelar y copia fechas y atributos.
/// </summary>
public static class CopiaNativa
{
    private const uint SinCacheDeWindows = 0x00001000;
    private const uint PedirTraficoComprimido = 0x10000000;
    private const int MensajeBloqueTerminado = 2;
    private const int Continuar = 0;
    private const int Cancelar = 1;
    private const int DesplazamientoBytesTransferidos = 72;

    /// <param name="sinCache">Para archivos grandes: no llena la memoria de Windows y la velocidad no cae a la mitad.</param>
    /// <param name="alAvanzar">Recibe los bytes nuevos desde el último aviso.</param>
    public static Task CopiarAsync(string origenIO, string destinoIO, bool sinCache, Action<long> alAvanzar, CancellationToken cancelacion) =>
        Task.Factory.StartNew(
            () => Copiar(origenIO, destinoIO, sinCache, alAvanzar, cancelacion),
            cancelacion,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static void Copiar(string origenIO, string destinoIO, bool sinCache, Action<long> alAvanzar, CancellationToken cancelacion)
    {
        long anteriores = 0;
        CopyFile2ProgressRoutine aviso = (mensaje, _) =>
        {
            if (Marshal.ReadInt32(mensaje) == MensajeBloqueTerminado)
            {
                var transferidos = Marshal.ReadInt64(mensaje, DesplazamientoBytesTransferidos);
                alAvanzar(transferidos - anteriores);
                anteriores = transferidos;
            }

            return cancelacion.IsCancellationRequested ? Cancelar : Continuar;
        };

        var parametros = new CopyFile2ExtendedParameters
        {
            Tamano = (uint)Marshal.SizeOf<CopyFile2ExtendedParameters>(),
            Opciones = PedirTraficoComprimido | (sinCache ? SinCacheDeWindows : 0),
            Aviso = Marshal.GetFunctionPointerForDelegate(aviso),
        };
        var resultado = CopyFile2(origenIO, destinoIO, ref parametros);
        GC.KeepAlive(aviso);
        cancelacion.ThrowIfCancellationRequested();
        if (resultado < 0)
        {
            throw Marshal.GetExceptionForHR(resultado) ?? new IOException($"Windows no pudo copiar el archivo (0x{resultado:X8})", resultado);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CopyFile2ProgressRoutine(IntPtr mensaje, IntPtr contexto);

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyFile2ExtendedParameters
    {
        public uint Tamano;
        public uint Opciones;
        public IntPtr Cancelado;
        public IntPtr Aviso;
        public IntPtr Contexto;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int CopyFile2(string origen, string destino, ref CopyFile2ExtendedParameters parametros);
}
