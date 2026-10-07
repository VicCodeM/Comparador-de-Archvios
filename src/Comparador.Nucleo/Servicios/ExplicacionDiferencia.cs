namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Por qué dos archivos del mismo tamaño no son iguales, dicho con lo que probablemente pasó: cuál se modificó después
/// o, si las fechas coinciden, que uno cambió sin tocar la fecha o se dañó. "Distinto contenido" a secas no ayuda a
/// decidir si copiar o no.
/// </summary>
public static class ExplicacionDiferencia
{
    public static string PorHuella(DateTime? origen, DateTime? destino, TimeSpan tolerancia) =>
        "Distinto contenido (SHA-256): " + QuePaso(origen, destino, tolerancia,
            igual: "mismo tamaño y misma fecha, así que se modificó sin cambiar la fecha o uno de los dos está dañado");

    public static string PorFecha(DateTime origen, DateTime destino, TimeSpan tolerancia) =>
        "Mismo tamaño pero distinta fecha: " + QuePaso(origen, destino, tolerancia, igual: string.Empty);

    private static string QuePaso(DateTime? origen, DateTime? destino, TimeSpan tolerancia, string igual)
    {
        if (origen is not { } enOrigen || destino is not { } enDestino || (enOrigen - enDestino).Duration() <= tolerancia)
        {
            return igual;
        }

        return enOrigen > enDestino
            ? $"el origen se modificó después ({Fecha(enOrigen)}); el destino tiene una versión anterior ({Fecha(enDestino)})"
            : $"el destino se modificó después ({Fecha(enDestino)}); si copias, se pierden esos cambios";
    }

    private static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy HH:mm");
}
