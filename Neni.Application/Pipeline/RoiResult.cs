namespace Neni.Application.Pipeline;

internal enum RoiOutcome
{
    // La ROI no cambio lo suficiente desde el ultimo OCR: se conserva lo que ya se mostraba.
    Unchanged,
    // Se corrio OCR + traduccion y Blocks trae el resultado nuevo.
    Updated,
    // Algo fallo (recorte, OCR o traduccion); Error trae el motivo. No es lo mismo que Unchanged.
    Failed
}

// Resultado de procesar una ROI en una vuelta.
internal sealed record RoiResult(RoiOutcome Outcome, IReadOnlyList<TranslatedBlock> Blocks, string? Error = null)
{
    public static readonly RoiResult Unchanged = new(RoiOutcome.Unchanged, []);
    public static RoiResult Updated(IReadOnlyList<TranslatedBlock> blocks) => new(RoiOutcome.Updated, blocks);
    public static RoiResult Failed(string error) => new(RoiOutcome.Failed, [], error);
}
