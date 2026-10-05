using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Ubicaciones;

public enum TipoUbicacion
{
    Disco,
    Usb,
    Red,
    Telefono,
}

/// <summary>Fechas y atributos que se le copian al archivo de destino. Lo que el origen no sabe va en null.</summary>
public sealed record MetadatosArchivo(DateTime? Creacion, DateTime? Modificacion, FileAttributes? Atributos);

/// <summary>
/// Una carpeta raíz donde se leen o escriben archivos: un disco, una memoria USB, una carpeta de red o un teléfono.
/// Todas las rutas que recibe son relativas a la raíz ("" es la raíz misma). La comparación y la copia solo hablan con
/// esta interfaz, así funcionan igual en cualquier combinación de origen y destino.
/// </summary>
public interface IUbicacion
{
    string Raiz { get; }

    TipoUbicacion Tipo { get; }

    /// <summary>Los teléfonos (MTP) no guardan la fecha real del archivo: ahí solo se puede comparar por tamaño o contenido.</summary>
    bool FechasFiables { get; }

    bool Existe();

    void CrearCarpeta(string relativa);

    /// <summary>Lo que hay en una carpeta, sin entrar en subcarpetas. Lanza UnauthorizedAccessException si no hay permiso.</summary>
    IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa);

    Stream AbrirLectura(string relativa);

    MetadatosArchivo LeerMetadatos(string relativa);

    /// <summary>Escribe el contenido completo en esa ruta, creándola o reemplazándola.</summary>
    Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion);

    /// <summary>Pone el temporal en el lugar del definitivo de una vez, con las fechas y atributos del origen.</summary>
    void Reemplazar(string temporal, string definitiva, MetadatosArchivo metadatos);

    void BorrarArchivo(string relativa);
}
