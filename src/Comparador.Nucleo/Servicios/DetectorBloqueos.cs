using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Dice QUÉ programa tiene abierto un archivo, con la API Restart Manager de Windows (la que usan los instaladores).
/// Sustituye al "desbloquear" anterior, que no podía liberar un archivo de otro programa y engañaba al usuario.
/// </summary>
public static class DetectorBloqueos
{
    private const int MaximoClave = 32;
    private const int ErrorMasDatos = 234;

    public static IReadOnlyList<string> QuienLoUsa(string ruta)
    {
        var clave = new StringBuilder(MaximoClave + 1);
        if (RmStartSession(out var sesion, 0, clave) != 0)
        {
            return [];
        }

        try
        {
            return RmRegisterResources(sesion, 1, [ruta], 0, IntPtr.Zero, 0, null) == 0 ? ListarProcesos(sesion) : [];
        }
        finally
        {
            RmEndSession(sesion);
        }
    }

    /// <summary>Los programas que tienen abierta cualquiera de las rutas, o "otro programa" si Windows no lo dice.</summary>
    public static string Describir(params string[] rutas)
    {
        var programas = rutas.SelectMany(QuienLoUsa).Distinct().ToList();

        return programas.Count == 0 ? "otro programa" : string.Join(", ", programas);
    }

    private static IReadOnlyList<string> ListarProcesos(uint sesion)
    {
        uint necesarios = 0, cantidad = 0, motivos = 0;
        if (RmGetList(sesion, out necesarios, ref cantidad, null, ref motivos) != ErrorMasDatos)
        {
            return [];
        }

        var procesos = new RmProcessInfo[necesarios];
        cantidad = necesarios;
        if (RmGetList(sesion, out necesarios, ref cantidad, procesos, ref motivos) != 0)
        {
            return [];
        }

        return procesos.Take((int)cantidad).Select(p => $"{p.NombreAplicacion} (proceso {p.Proceso.IdProceso})").Distinct().ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int IdProceso;
        public FILETIME Inicio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Proceso;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string NombreAplicacion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string NombreServicio;

        public int TipoAplicacion;
        public uint Estado;
        public uint SesionTerminal;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SePuedeReiniciar;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sesion, int flags, StringBuilder clave);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sesion);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sesion, uint archivos, string[] rutas, uint aplicaciones, IntPtr unicos, uint servicios, string[]? nombresServicios);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint sesion, out uint necesarios, ref uint cantidad, [In, Out] RmProcessInfo[]? procesos, ref uint motivos);
}
