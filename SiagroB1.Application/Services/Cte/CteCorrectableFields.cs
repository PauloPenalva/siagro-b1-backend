namespace SiagroB1.Application.Services.Cte;

/// <summary>Campo do CT-e que a CC-e pode corrigir.</summary>
public sealed record CteCorrectableField(string Group, string Field, string Label, int MaxLength);

/// <summary>
/// Lista fechada dos campos corrigíveis por CC-e estruturada. O grupo é só o nome da TAG (grupoAlterado, até 20 caracteres no XSD), nunca um caminho. Valor do serviço, tributos, tomador, remetente,
/// datas e chave não podem ser corrigidos por carta (regra do CT-e).
/// </summary>
public static class CteCorrectableFields
{
    public static readonly IReadOnlyList<CteCorrectableField> All =
    [
        new("compl", "xObs", "Observações gerais", 2000),
        new("receb", "xNome", "Recebedor: nome", 60),
        new("enderReceb", "xLgr", "Recebedor: logradouro", 255),
        new("enderReceb", "nro", "Recebedor: número", 60),
        new("enderReceb", "xBairro", "Recebedor: bairro", 60),
        new("dest", "xNome", "Destinatário: nome", 60),
        new("enderDest", "xLgr", "Destinatário: logradouro", 255),
        new("enderDest", "nro", "Destinatário: número", 60),
        new("enderDest", "xBairro", "Destinatário: bairro", 60),
        new("infCarga", "proPred", "Produto predominante", 60),
    ];

    public static CteCorrectableField? Find(string? group, string? field) =>
        All.FirstOrDefault(x => x.Group.Equals(group?.Trim(), StringComparison.OrdinalIgnoreCase)
                                && x.Field.Equals(field?.Trim(), StringComparison.OrdinalIgnoreCase));
}
