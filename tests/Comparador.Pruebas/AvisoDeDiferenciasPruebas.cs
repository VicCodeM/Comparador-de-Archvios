using Comparador.App.ModelosVista;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.Pruebas;

/// <summary>Lo distinto se explica con lo que probablemente pasó y lo que pide atención sale arriba de las listas.</summary>
public sealed class AvisoDeDiferenciasPruebas
{
    private static readonly TimeSpan Tolerancia = TimeSpan.FromSeconds(2);
    private static readonly DateTime Antes = new(2026, 10, 1, 9, 0, 0);
    private static readonly DateTime Despues = new(2026, 10, 6, 18, 30, 0);

    [Fact]
    public void Si_el_origen_es_mas_nuevo_dice_que_el_destino_tiene_una_version_anterior() =>
        Assert.Contains("el destino tiene una versión anterior", ExplicacionDiferencia.PorHuella(Despues, Antes, Tolerancia));

    [Fact]
    public void Si_el_destino_es_mas_nuevo_avisa_que_copiar_pierde_sus_cambios() =>
        Assert.Contains("si copias, se pierden esos cambios", ExplicacionDiferencia.PorHuella(Antes, Despues, Tolerancia));

    [Fact]
    public void Con_la_misma_fecha_dice_que_se_modifico_sin_cambiar_la_fecha_o_esta_danado() =>
        Assert.Contains("uno de los dos está dañado", ExplicacionDiferencia.PorHuella(Antes, Antes.AddSeconds(1), Tolerancia));

    private static ElementoComparado Elemento(string ruta, EstadoElemento estado, bool fallida = false) => new()
    {
        Par = new ParRutas(@"C:\origen", @"D:\destino"),
        RutaRelativa = ruta,
        EsCarpeta = false,
        Estado = estado,
        CopiaFallida = fallida,
    };

    [Fact]
    public void En_la_tabla_lo_que_fallo_al_copiar_y_los_problemas_van_antes_que_lo_demas()
    {
        var elementos = new[]
        {
            Elemento("igual.txt", EstadoElemento.Coincide),
            Elemento("falta.txt", EstadoElemento.Falta),
            Elemento("distinto.txt", EstadoElemento.Diferente),
            Elemento("bloqueado.txt", EstadoElemento.Error),
            Elemento("fallo.txt", EstadoElemento.Diferente, fallida: true),
        };

        var avisos = new AvisosModeloVista();
        var tabla = new RevisionModeloVista(avisos, new AccionesArchivo(avisos));
        tabla.Cargar(new ResultadoComparacion { Elementos = [.. elementos], Duracion = TimeSpan.Zero });
        tabla.FijarFiltro(FiltroRevision.Todos);

        Assert.Equal(["fallo.txt", "bloqueado.txt", "distinto.txt", "falta.txt", "igual.txt"], tabla.Visibles.Select(elemento => elemento.RutaRelativa));
    }

    [Fact]
    public void En_la_lista_de_la_copia_los_fallidos_salen_arriba_aunque_sean_los_mas_viejos()
    {
        var terminados = new TerminadosModeloVista(new AccionesArchivo(new AvisosModeloVista()));
        terminados.Empezar();
        terminados.Agregar(
        [
            new ArchivoTerminado("roto.bin", string.Empty, string.Empty, 10, ResultadoArchivo.Fallido, "La verificación SHA-256 falló", Antes),
            new ArchivoTerminado("bien1.bin", string.Empty, string.Empty, 10, ResultadoArchivo.Copiado, string.Empty, Despues),
            new ArchivoTerminado("bien2.bin", string.Empty, string.Empty, 10, ResultadoArchivo.Copiado, string.Empty, Despues),
        ]);
        terminados.Terminar();

        Assert.Equal("roto.bin", terminados.Lista[0].Nombre);
    }
}
