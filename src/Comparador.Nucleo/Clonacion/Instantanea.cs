using System.Management;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Una instantánea de Windows (VSS) de un volumen: lo congela en un instante para leerlo entero y coherente aunque
/// esté en uso, incluido el disco donde corre Windows. Se borra al soltarla. Exige administrador.
/// </summary>
internal sealed class Instantanea : IDisposable
{
    private readonly string identificador;

    private Instantanea(string identificador, string dispositivo)
    {
        this.identificador = identificador;
        Dispositivo = dispositivo;
    }

    /// <summary>"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN": se lee como un volumen más.</summary>
    public string Dispositivo { get; }

    /// <param name="volumen">"\\?\Volume{...}\" o "C:\".</param>
    public static Instantanea Crear(string volumen)
    {
        using var clase = new ManagementClass(@"root\cimv2", "Win32_ShadowCopy", null);
        using var entrada = clase.GetMethodParameters("Create");
        entrada["Volume"] = volumen;
        entrada["Context"] = "ClientAccessible";
        using var salida = clase.InvokeMethod("Create", entrada, null);
        var codigo = Convert.ToUInt32(salida["ReturnValue"]);
        if (codigo != 0)
        {
            throw new InvalidOperationException($"Windows no pudo congelar el volumen para clonarlo (VSS, código {codigo}). ¿Está activo el servicio \"Instantáneas de volumen\"?");
        }

        var identificador = (string)salida["ShadowID"];

        return new Instantanea(identificador, (string)Buscar(identificador)["DeviceObject"]);
    }

    private static ManagementObject Buscar(string identificador)
    {
        using var busqueda = new ManagementObjectSearcher(@"root\cimv2", $"SELECT * FROM Win32_ShadowCopy WHERE ID='{identificador}'");

        return busqueda.Get().Cast<ManagementObject>().Single();
    }

    public void Dispose()
    {
        try
        {
            using var copia = Buscar(identificador);
            copia.Delete();
        }
        catch (ManagementException)
        {
            // Si ya no existe (Windows la borró por falta de espacio), no hay nada que soltar.
        }
    }
}
