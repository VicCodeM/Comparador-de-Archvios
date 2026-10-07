using System.Diagnostics;
using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Clonacion;

/// <param name="Fraccion">Avance del paso actual (0 a 1), o null si no se puede medir (crear particiones, arranque).</param>
public sealed record AvanceClonado(string Paso, double? Fraccion, string Detalle);

/// <summary>
/// Clona un disco entero a otro de cualquier tamaño (mientras quepa lo ocupado) y lo deja arrancable en otra PC:
/// rehace las particiones según el plan, copia sector a sector las fijas y las que crecen (leyendo de una instantánea
/// las que están en uso, como el Windows encendido), estira el NTFS de las que crecen, copia con DISM los archivos de
/// las que encogen y rehace el arranque con bcdboot. Exige administrador.
/// </summary>
public static class ClonadoInteligente
{
    private static readonly Guid Efi = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");

    public static Task<TimeSpan> ClonarAsync(int numeroOrigen, int numeroDestino, bool verificar, IProgress<AvanceClonado>? progreso, CancellationToken cancelacion) =>
        Task.Run(() => new Ejecucion(numeroOrigen, numeroDestino, verificar, progreso, cancelacion).Clonar(), cancelacion);

    /// <summary>Lo que se reparte la pantalla antes de empezar: cómo quedará cada partición. Lanza si no cabe.</summary>
    public static IReadOnlyList<ParticionPlaneada> Planear(DiscoFisico origen, DiscoFisico destino) =>
        PlanParticiones.Planear(origen.Particiones.OrderBy(particion => particion.Inicio).ToList(), destino.Tamano);

    private sealed class Ejecucion(int numeroOrigen, int numeroDestino, bool verificar, IProgress<AvanceClonado>? progreso, CancellationToken cancelacion)
    {
        private readonly List<(int Particion, char Letra)> letras = [];
        private readonly List<Instantanea> instantaneas = [];
        private IReadOnlyList<ParticionPlaneada> plan = [];
        private IReadOnlyList<Particion> nuevas = [];
        private DiscoFisico destino = null!;

        public TimeSpan Clonar()
        {
            var reloj = Stopwatch.StartNew();
            var discos = ListadoDiscos.Leer();
            var problemas = ReglasClon.Revisar(new ExtremoClon.DeDisco(numeroOrigen), new ExtremoClon.DeDisco(numeroDestino), discos, null, ajustar: true);
            if (problemas.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, problemas));
            }

            var origen = discos.Single(disco => disco.Numero == numeroOrigen);
            plan = Planear(origen, discos.Single(disco => disco.Numero == numeroDestino));
            Avisar("Preparando el disco destino", null, $"{plan.Count} particiones, tabla {origen.Estilo.ToString().ToUpperInvariant()}");
            Diskpart.CrearParticiones(numeroDestino, origen.Estilo, plan);
            destino = ListadoDiscos.Leer().Single(disco => disco.Numero == numeroDestino);
            nuevas = destino.Particiones.OrderBy(particion => particion.Inicio).ToList();
            ComprobarParticionesNuevas();

            try
            {
                CopiarSectores();
                AgrandarYCopiarArchivos();
                HacerArrancable(origen.Estilo == EstiloParticiones.Gpt);
            }
            finally
            {
                Limpiar();
            }

            return reloj.Elapsed;
        }

        /// <summary>
        /// Antes de escribir un solo byte: cada partición nueva es del tipo y al menos del tamaño del plan. Si diskpart
        /// creó de más o de menos, copiar "en orden" pondría cada cosa en la partición equivocada.
        /// </summary>
        private void ComprobarParticionesNuevas()
        {
            var distintas = nuevas.Count != plan.Count || plan.Where((planeada, indice) =>
                nuevas[indice].Tamano < planeada.Tamano
                || nuevas[indice].TipoGpt != planeada.Origen.TipoGpt
                || nuevas[indice].TipoMbr != planeada.Origen.TipoMbr && planeada.Origen.TipoMbr != 0).Any();
            if (distintas)
            {
                throw new InvalidOperationException(
                    $"El disco destino no quedó como se planeó ({nuevas.Count} particiones, debían ser {plan.Count}). No se copió nada.");
            }
        }

        /// <summary>Las fijas y las que crecen, sector a sector, con el disco destino bloqueado mientras tanto.</summary>
        private void CopiarSectores()
        {
            using var escritura = DiscoCrudo.AbrirEscritura(numeroDestino);
            for (var indice = 0; indice < plan.Count; indice++)
            {
                if (plan[indice].Modo == ModoParticion.Archivos)
                {
                    continue;
                }

                var paso = $"Copiando la partición {indice + 1} de {plan.Count} ({plan[indice].Origen.Tipo})";
                using var lectura = AbrirOrigen(plan[indice]);
                using var parte = new DestinoDisco(escritura, nuevas[indice].Inicio, nuevas[indice].Tamano, destino.TamanoSector, propio: false);
                MotorClon.Copiar(lectura, parte, verificar, new Progress<AvanceClon>(avance => Avisar(Fase(paso, avance), Fraccion(avance), Detalle(avance))), cancelacion);
            }
        }

        /// <summary>Si la partición está en uso (tiene volumen y va a crecer), se lee de una instantánea: congelada y coherente.</summary>
        private OrigenClon AbrirOrigen(ParticionPlaneada planeada)
        {
            if (planeada.Modo == ModoParticion.Agrandar && planeada.Origen.Volumen is { } volumen)
            {
                var congelado = DiscoCrudo.AbrirVolumen(Congelar(volumen).Dispositivo);

                return OrigenClon.DeTramo(congelado, 0, congelado.Largo());
            }

            return OrigenClon.DeTramo(DiscoCrudo.AbrirLectura(numeroOrigen), planeada.Origen.Inicio, planeada.Origen.Tamano);
        }

        private void AgrandarYCopiarArchivos()
        {
            for (var indice = 0; indice < plan.Count; indice++)
            {
                var numero = nuevas[indice].Numero;
                if (plan[indice].Modo == ModoParticion.Agrandar)
                {
                    Avisar($"Agrandando la partición {indice + 1} a {Formatos.Tamano(plan[indice].Tamano)}", null, plan[indice].Origen.Tipo);
                    Diskpart.Extender(numeroDestino, numero);
                    Asignar(numero);
                }
                else if (plan[indice].Modo == ModoParticion.Archivos)
                {
                    CopiarArchivos(indice, numero);
                }
            }
        }

        private void CopiarArchivos(int indice, int numero)
        {
            var origen = plan[indice].Origen;
            var paso = $"Copiando los archivos de la partición {indice + 1} ({origen.Tipo})";
            Avisar(paso, null, "Preparando la partición nueva");
            Diskpart.Formatear(numeroDestino, numero, origen.Etiqueta);
            var letra = Asignar(numero);
            var congelado = Congelar(origen.Volumen!);
            var enlace = Path.Combine(Path.GetTempPath(), $"espejo-instantanea-{Guid.NewGuid():N}");
            Directory.CreateSymbolicLink(enlace, congelado.Dispositivo + @"\");
            var imagen = Path.Combine(CarpetaTemporal(origen.Usado!.Value, letra, plan[indice].Tamano), $"espejo-{Guid.NewGuid():N}.wim");
            try
            {
                Dism.Capturar(enlace, imagen, fraccion => Avisar(paso, fraccion / 2, "Leyendo los archivos (DISM)"), cancelacion);
                Dism.Aplicar(imagen, $@"{letra}:\", fraccion => Avisar(paso, 0.5 + fraccion / 2, "Escribiendo los archivos (DISM)"), cancelacion);
            }
            finally
            {
                File.Delete(imagen);
                Directory.Delete(enlace);
            }
        }

        /// <summary>
        /// Dónde dejar el .wim de paso: el disco (que no sea el destino) con más espacio libre; si no hay, la propia
        /// partición nueva si le sobra sitio para el .wim y los archivos a la vez.
        /// </summary>
        private string CarpetaTemporal(long usado, char letraNueva, long tamanoNuevo)
        {
            var delDestino = ListadoDiscos.Leer().Single(disco => disco.Numero == numeroDestino).Letras;
            var otro = DriveInfo.GetDrives()
                .Where(unidad => unidad is { DriveType: DriveType.Fixed, IsReady: true, DriveFormat: "NTFS" } && unidad.AvailableFreeSpace > usado
                    && !delDestino.Contains(unidad.Name.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase))
                .MaxBy(unidad => unidad.AvailableFreeSpace);
            if (otro is not null)
            {
                return otro.RootDirectory.FullName;
            }

            return tamanoNuevo - usado > usado
                ? $@"{letraNueva}:\"
                : throw new InvalidOperationException($"Hace falta un sitio temporal con {Formatos.Tamano(usado)} libres (en otro disco) para copiar los archivos.");
        }

        private void HacerArrancable(bool uefi)
        {
            var windows = letras.FirstOrDefault(asignada => Arranque.TieneWindows($@"{asignada.Letra}:\"));
            if (windows == default)
            {
                return;
            }

            var sistema = Enumerable.Range(0, plan.Count).FirstOrDefault(indice => uefi ? plan[indice].Origen.TipoGpt == Efi : plan[indice].Origen.Activa, -1);
            if (sistema < 0)
            {
                throw new InvalidOperationException("El disco tiene Windows pero no se encontró su partición de arranque (EFI o activa).");
            }

            Avisar("Haciendo el disco arrancable", null, uefi ? "Arranque UEFI (bcdboot)" : "Arranque BIOS (bcdboot)");
            var letraSistema = letras.FirstOrDefault(asignada => asignada.Particion == nuevas[sistema].Numero).Letra;
            Arranque.Crear($@"{windows.Letra}:\", $@"{(letraSistema == default ? Asignar(nuevas[sistema].Numero) : letraSistema)}:\", uefi);
        }

        private Instantanea Congelar(string volumen)
        {
            var instantanea = Instantanea.Crear(volumen);
            instantaneas.Add(instantanea);

            return instantanea;
        }

        private char Asignar(int particion)
        {
            var ocupadas = Environment.GetLogicalDrives().Select(unidad => char.ToUpperInvariant(unidad[0])).Concat(letras.Select(asignada => asignada.Letra)).ToHashSet();
            var letra = Enumerable.Range('D', 'Z' - 'D' + 1).Select(codigo => (char)codigo).Reverse().First(candidata => !ocupadas.Contains(candidata));
            Diskpart.AsignarLetra(numeroDestino, particion, letra);
            letras.Add((particion, letra));

            return letra;
        }

        /// <summary>El disco va a otra PC: sin letras de paso en esta, y sin instantáneas ocupando espacio en el origen.</summary>
        private void Limpiar()
        {
            foreach (var (particion, letra) in letras)
            {
                try
                {
                    Diskpart.QuitarLetra(numeroDestino, particion, letra);
                }
                catch (InvalidOperationException)
                {
                    // Una letra que Windows ya quitó no debe ocultar el resultado del clonado.
                }
            }

            foreach (var instantanea in instantaneas)
            {
                instantanea.Dispose();
            }
        }

        private void Avisar(string paso, double? fraccion, string detalle) => progreso?.Report(new AvanceClonado(paso, fraccion, detalle));

        private static string Fase(string paso, AvanceClon avance) => avance.Fase == FaseClon.Verificando ? $"{paso}: verificando" : paso;

        private static double? Fraccion(AvanceClon avance) => avance.Total is > 0 ? (double)avance.Hechos / avance.Total.Value : avance.Fraccion;

        private static string Detalle(AvanceClon avance) =>
            $"{Formatos.Tamano(avance.Hechos)} de {Formatos.Tamano(avance.Total)} · {Formatos.Tamano((long)avance.BytesPorSegundo)}/s";
    }
}
