namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// CFOPs de exportação direta (7xxx) e de remessa com fim específico de exportação / formação de lote
/// (5501/6501, 5502/6502, 5504/6504, 5505/6505). Neles o grupo tributável do item sai na unidade de
/// exportação configurada no complemento do produto (spec 2026-10-14 §3). Regra fiscal: fica no código.
/// </summary>
public static class ExportCfops
{
    private static readonly HashSet<string> SpecificPurpose =
        ["5501", "6501", "5502", "6502", "5504", "6504", "5505", "6505"];

    public static bool Contains(string? cfop)
    {
        var code = cfop?.Trim();
        if (code is not { Length: 4 })
            return false;

        return code[0] == '7' || SpecificPurpose.Contains(code);
    }
}
