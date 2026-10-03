namespace Comparador.Nucleo.Servicios;

public static class Formatos
{
    private static readonly string[] Unidades = ["B", "KB", "MB", "GB", "TB"];

    public static string Tamano(long? bytes)
    {
        if (bytes is null)
        {
            return string.Empty;
        }

        double valor = bytes.Value;
        var unidad = 0;
        while (valor >= 1024 && unidad < Unidades.Length - 1)
        {
            valor /= 1024;
            unidad++;
        }

        return unidad == 0 ? $"{valor:0} {Unidades[unidad]}" : $"{valor:0.##} {Unidades[unidad]}";
    }

    public static string Duracion(TimeSpan duracion) => duracion.TotalHours >= 1
        ? $"{(int)duracion.TotalHours} h {duracion.Minutes} min"
        : duracion.TotalMinutes >= 1 ? $"{duracion.Minutes} min {duracion.Seconds} s" : $"{Math.Max(duracion.Seconds, 0)} s";
}
