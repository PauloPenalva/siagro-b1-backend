using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services.EfdContributions;

/// <summary>
/// Inconsistências que impedem a geração da EFD Contribuições (o PVA recusaria o arquivo). Os
/// avisos da Conferência Fiscal são somados por <see cref="EfdContributionsFileService"/>.
/// Estático de propósito: o Scrutor só registra classes terminadas em "Service".
/// </summary>
public static class EfdContributionsValidator
{
    /// <summary>CST de PIS/COFINS de receita tributada (M210/M610).</summary>
    internal static readonly HashSet<string> TaxedRevenueCsts = ["01", "02"];

    /// <summary>CST de receita sem contribuição que exige a natureza da receita (M400/M410, M800/M810).</summary>
    internal static readonly HashSet<string> UntaxedRevenueCsts = ["04", "05", "06", "07", "08", "09"];

    /// <summary>CST de entrada com direito a crédito (M100/M105, M500/M505).</summary>
    internal static readonly HashSet<string> CreditCsts = ["50", "51", "52", "53", "54", "55", "56"];

    /// <summary>CST de saída com alíquota por unidade de medida: fora do escopo (bloqueia).</summary>
    internal static readonly HashSet<string> UnsupportedRevenueCsts = ["03"];

    /// <summary>CST de entrada com crédito presumido: fora do escopo (bloqueia).</summary>
    internal static readonly HashSet<string> UnsupportedCreditCsts = ["60", "61", "62", "63", "64", "65", "66", "67"];

    public static List<EfdContributionsIssueDto> Validate(EfdContributionsData data, EfdContributionsRequest request)
    {
        var issues = new List<EfdContributionsIssueDto>();
        void Block(string message, string reference) =>
            issues.Add(new EfdContributionsIssueDto { Blocking = true, Message = message, Reference = reference });

        if (request.Purpose == 1 && string.IsNullOrWhiteSpace(request.PreviousReceiptNumber))
            Block("Escrituração retificadora: informe o número do recibo da escrituração anterior.", "");

        if (new DateTime(request.Year, request.Month, 1) < EfdContributionsFileService.FirstSupportedPeriod)
            Block("EFD Contribuições disponível a partir de 01/2019 (leiaute 005).", "");

        var declarant = data.Declarant;
        if (declarant == null)
        {
            var root = data.Establishments.Select(e => Digits(e.Branch.TaxId)).FirstOrDefault(c => c.Length >= 8)?[..8] ?? "";
            Block($"Filiais do CNPJ {root} sem matriz (/0001) cadastrada.", root);
        }
        else
        {
            var code = declarant.Code ?? "";
            if (data.Settings == null) Block("Parâmetros do SPED Fiscal não cadastrados para a filial.", code);
            else
            {
                // Campos obrigatórios do 0100 (mesmos do SPED Fiscal)
                var s = data.Settings;
                foreach (var (value, field) in new[]
                         {
                             (s.AccountantName, "nome"), (s.AccountantCpf, "CPF"), (s.AccountantCrc, "CRC"),
                             (s.AccountantEmail, "e-mail"), (s.AccountantMunicipalityCode, "município"),
                         })
                    if (string.IsNullOrWhiteSpace(value))
                        Block($"Parâmetros do SPED Fiscal: informe {field} do contabilista.", code);
            }
        }

        foreach (var e in data.Establishments)
        {
            var branch = e.Branch;
            var branchCode = branch.Code ?? "";
            if (Digits(branch.TaxId).Length != 14) Block($"Filial {branchCode} sem CNPJ.", branchCode);
            if (string.IsNullOrWhiteSpace(branch.StateCode)) Block($"Filial {branchCode} sem UF.", branchCode);
            if (string.IsNullOrWhiteSpace(branch.MunicipalityCode)) Block($"Filial {branchCode} sem município.", branchCode);

            var master = e.MasterData;
            foreach (var code in master.MissingParticipantCodes)
                Block($"Parceiro {code} sem cadastro.", code);
            foreach (var p in master.Participants)
            {
                // Participante do exterior não tem CNPJ/CPF
                if (p.CountryCode == "01058" && string.IsNullOrWhiteSpace(p.Cnpj) && string.IsNullOrWhiteSpace(p.Cpf)) Block($"Participante {p.Code} sem CNPJ/CPF.", p.Code);
                if (string.IsNullOrWhiteSpace(p.MunicipalityCode)) Block($"Participante {p.Code} sem município.", p.Code);
            }

            // Toda linha vira C170 na EFD Contribuições: NCM de todos os produtos
            foreach (var item in master.Items.Where(i => i.Ncm == ""))
                Block($"Produto {item.Code} sem NCM.", item.Code);
            foreach (var u in master.UnconvertibleUnits)
                Block($"Produto {u.ItemCode} sem fator de conversão para a unidade {u.UnitCode}.", u.ItemCode);

            var nonCumulative = branch.PisCofinsRegime == PisCofinsRegime.NonCumulative;
            foreach (var line in e.Lines)
            {
                var reference = $"{line.Number}/{line.ItemCode}";
                if (string.IsNullOrWhiteSpace(line.Cfop)) Block($"Item {reference} sem CFOP.", reference);
                if (string.IsNullOrWhiteSpace(line.CstPis) || string.IsNullOrWhiteSpace(line.CstCofins))
                {
                    Block($"Linha {reference} sem CST de PIS ou COFINS.", reference);
                    continue;
                }

                var lineCsts = new[] { line.CstPis, line.CstCofins }.Distinct().ToList();
                if (line.Direction == "S")
                    foreach (var cst in lineCsts.Where(UnsupportedRevenueCsts.Contains))
                        Block($"Linha {reference} com CST {cst} de PIS/COFINS (alíquota por unidade) não suportado na EFD Contribuições.", reference);
                if (line.Direction == "E")
                {
                    foreach (var cst in lineCsts.Where(UnsupportedCreditCsts.Contains))
                        Block($"Linha {reference} com CST {cst} (crédito presumido) não suportado na EFD Contribuições.", reference);
                    // Filial cumulativa não apura crédito: o CST 5x é descartado do bloco M
                    if (!nonCumulative)
                        foreach (var cst in lineCsts.Where(CreditCsts.Contains))
                            issues.Add(new EfdContributionsIssueDto
                            {
                                Blocking = false,
                                Message = $"Linha {reference} com CST {cst} de crédito em filial do regime cumulativo; o crédito não entra na apuração.",
                                Reference = reference,
                            });
                }

                var usage = line.UsageCode is int u ? data.Usages.GetValueOrDefault(u) : null;
                var usageRef = line.UsageCode?.ToString() ?? "(sem natureza)";
                if (line.Direction == "S")
                    foreach (var cst in new[] { line.CstPis, line.CstCofins }.Distinct().Where(UntaxedRevenueCsts.Contains))
                        if (string.IsNullOrWhiteSpace(usage?.PisCofinsRevenueNatureCode))
                            Block($"Natureza {usageRef} sem Natureza da receita (EFD Contribuições) para CST {cst}.", usageRef);

                if (nonCumulative && line.Direction == "E" && (CreditCsts.Contains(line.CstPis) || CreditCsts.Contains(line.CstCofins))
                    && string.IsNullOrWhiteSpace(usage?.PisCofinsCreditBaseCode))
                    Block($"Natureza {usageRef} sem Base de cálculo do crédito (EFD Contribuições).", usageRef);
            }

            // NF-e (modelo 55) regular exige a chave de 44 dígitos no C100
            foreach (var doc in e.Lines.Where(l => l.Model == "55").GroupBy(l => l.DocumentKey).Select(g => g.First()))
                if (doc.AccessKey is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
                    Block($"Documento {doc.Number} (modelo 55) sem chave de acesso.", doc.Number);

            // Documento sem NF-e sai como modelo 01 (nota de produtor costuma ser 04): só avisa
            foreach (var doc in e.Lines.Where(l => l.Model == "01").GroupBy(l => l.DocumentKey).Select(g => g.First()))
                issues.Add(new EfdContributionsIssueDto
                {
                    Blocking = false,
                    Message = $"Documento {doc.Number} sem NF-e declarado como modelo 01; confira o modelo.",
                    Reference = doc.Number,
                });
        }

        // A mesma natureza repetida em várias linhas gera uma mensagem só
        return issues.DistinctBy(i => (i.Blocking, i.Message)).ToList();
    }

    internal static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
