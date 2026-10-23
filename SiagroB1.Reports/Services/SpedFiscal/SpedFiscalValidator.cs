using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services.SpedFiscal;

/// <summary>
/// Inconsistências que impedem a geração do SPED Fiscal (o PVA recusaria o arquivo). Os avisos
/// vêm das regras da Conferência Fiscal e são somados por <see cref="SpedFiscalFileService"/>.
/// Estático de propósito: o Scrutor só registra classes terminadas em "Service".
/// </summary>
public static class SpedFiscalValidator
{
    public static List<SpedFiscalIssueDto> Validate(SpedFiscalData data)
    {
        var issues = new List<SpedFiscalIssueDto>();
        void Block(string message, string reference) =>
            issues.Add(new SpedFiscalIssueDto { Blocking = true, Message = message, Reference = reference });

        var branch = data.Branch;
        var branchCode = branch.Code ?? "";
        if (string.IsNullOrWhiteSpace(Digits(branch.TaxId))) Block("Filial sem CNPJ.", branchCode);
        if (string.IsNullOrWhiteSpace(Digits(branch.StateRegistration))) Block("Filial sem inscrição estadual.", branchCode);
        if (string.IsNullOrWhiteSpace(branch.StateCode)) Block("Filial sem UF.", branchCode);
        if (string.IsNullOrWhiteSpace(branch.MunicipalityCode)) Block("Filial sem município.", branchCode);
        if (data.Settings == null) Block("Parâmetros do SPED Fiscal não cadastrados para a filial.", branchCode);
        else
        {
            // Campos obrigatórios do 0100
            var s = data.Settings;
            foreach (var (value, field) in new[]
                     {
                         (s.AccountantName, "nome"), (s.AccountantCpf, "CPF"), (s.AccountantCrc, "CRC"),
                         (s.AccountantEmail, "e-mail"), (s.AccountantMunicipalityCode, "município"),
                     })
                if (string.IsNullOrWhiteSpace(value))
                    Block($"Parâmetros do SPED Fiscal: informe {field} do contabilista.", branchCode);
        }

        foreach (var code in data.MissingParticipantCodes)
            Block($"Parceiro {code} sem cadastro.", code);
        foreach (var p in data.Participants)
        {
            // Participante do exterior não tem CNPJ/CPF
            if (p.CountryCode == "01058" && string.IsNullOrWhiteSpace(p.Cnpj) && string.IsNullOrWhiteSpace(p.Cpf)) Block($"Participante {p.Code} sem CNPJ/CPF.", p.Code);
            if (string.IsNullOrWhiteSpace(p.MunicipalityCode)) Block($"Participante {p.Code} sem município.", p.Code);
        }

        foreach (var line in data.Lines)
        {
            var reference = $"{line.Number}/{line.ItemCode}";
            if (string.IsNullOrWhiteSpace(line.Cfop)) Block($"Item {reference} sem CFOP.", reference);
            if (string.IsNullOrWhiteSpace(line.CstIcms)) Block($"Item {reference} sem CST de ICMS.", reference);
        }

        // NF-e (modelo 55) regular exige a chave de 44 dígitos no C100
        foreach (var doc in data.Lines.Where(l => l.Model == "55").GroupBy(l => l.DocumentKey).Select(g => g.First()))
            if (doc.AccessKey is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
                Block($"Documento {doc.Number} (modelo 55) sem chave de acesso.", doc.Number);

        // NCM só é exigido onde o item é detalhado: C170 (documento de terceiro) e H010
        var detailed = data.Lines.Where(l => l.IssuerType == "1").Select(l => l.ItemCode)
            .Concat(InventoryRows(data).Select(r => r.ItemCode))
            .ToHashSet();
        foreach (var item in data.Items.Where(i => detailed.Contains(i.Code) && i.Ncm == ""))
            Block($"Produto {item.Code} sem NCM.", item.Code);

        foreach (var u in data.UnconvertibleUnits)
            Block($"Produto {u.ItemCode} sem fator de conversão para a unidade {u.UnitCode}.", u.ItemCode);

        // Documento sem NF-e sai como modelo 01 (nota de produtor costuma ser 04): só avisa
        foreach (var doc in data.Lines.Where(l => l.Model == "01").GroupBy(l => l.DocumentKey).Select(g => g.First()))
            issues.Add(new SpedFiscalIssueDto
            {
                Blocking = false,
                Message = $"Documento {doc.Number} sem NF-e declarado como modelo 01; confira o modelo.",
                Reference = doc.Number,
            });

        foreach (var row in data.Inventory?.Rows.Where(r => r.Quantity < 0) ?? [])
            Block($"Inventário com saldo negativo: {row.ItemCode}.", row.ItemCode);

        return issues;
    }

    /// <summary>Linhas do inventário que viram H010 (saldo positivo).</summary>
    internal static IEnumerable<FiscalInventoryRowDto> InventoryRows(SpedFiscalData data) =>
        data.Inventory?.Rows.Where(r => r.Quantity > 0) ?? [];

    internal static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
