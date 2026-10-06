namespace Comparador.Nucleo.Modelos;

/// <summary>Qué hacer cuando el archivo ya existe en el destino (y es distinto).</summary>
public enum ReglaConflicto
{
    /// <summary>Se reemplaza por el del origen.</summary>
    Reemplazar,

    /// <summary>Se reemplaza solo si el del origen es más nuevo; si el del destino es igual o más nuevo, se deja.</summary>
    SoloSiEsMasNuevo,

    /// <summary>Nunca se toca lo que ya existe.</summary>
    Saltar,

    /// <summary>Se guardan los dos: el nuevo se copia como "nombre (2).ext".</summary>
    ConservarAmbos,
}

public sealed record OpcionesCopia
{
    /// <summary>Releer origen y copia y comparar sus huellas SHA-256 antes de dar el archivo por bueno.</summary>
    public bool Verificar { get; init; } = true;

    /// <summary>Copias a la vez fijadas a mano, o null para que se ajusten solas midiendo la velocidad.</summary>
    public int? HilosManuales { get; init; }

    public ReglaConflicto SiYaExiste { get; init; } = ReglaConflicto.Reemplazar;
}
