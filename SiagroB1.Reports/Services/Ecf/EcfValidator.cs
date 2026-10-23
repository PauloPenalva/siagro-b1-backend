using System.Globalization;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.Ecf;

/// <summary>
/// Inconsistências que impedem a geração da ECF: as da ECD do mesmo ano (contabilidade, meses fechados, plano
/// referencial, visões, resultado apurado…, pelo <see cref="EcdValidator"/>, porque os blocos J/K são os da ECD) e as
/// próprias da ECF. Estático de propósito: o Scrutor só registra classes terminadas em "Service".
/// </summary>
public static class EcfValidator
{
    private static readonly CultureInfo Br = new("pt-BR");

    public static List<EcdIssueDto> Validate(EcfData data, EcfRequest request)
    {
        // Regras da ECD; a substituição da ECD não se aplica (a retificadora da ECF é conferida abaixo)
        var issues = EcdValidator.Validate(data.Ledger, new EcdRequest { BranchCode = request.BranchCode, Year = request.Year });
        void Block(string message, string reference = "") =>
            issues.Add(new EcdIssueDto { Blocking = true, Message = message, Reference = reference });

        if (request.Year < EcfFileService.FirstSupportedYear)
            Block($"ECF disponível a partir do ano-calendário {EcfFileService.FirstSupportedYear}.");

        var code = data.Ledger.Declarant?.Code ?? "";
        if (data.Ledger.Declarant != null)
        {
            if (data.Settings is not { } s) Block("Parâmetros da ECF não cadastrados para a filial.", code);
            else
            {
                var cnae = s.MainCnae?.Trim() ?? "";
                if (cnae.Length != 7 || !cnae.All(char.IsAsciiDigit))
                    Block($"Parâmetros da ECF: CNAE principal {cnae} inválido (7 dígitos).", code);
                if (request.Purpose == 1 && string.IsNullOrWhiteSpace(s.PreviousEcfHash))
                    Block("ECF retificadora: informe o hash da ECF anterior nos parâmetros da ECF.", code);

                var annual = s.AssessmentForm == EcfAssessmentForm.Annual;
                foreach (var a in data.Adjustments)
                {
                    if (annual ? a.Period != 0 : a.Period is < 1 or > 4)
                        Block($"Ajuste \"{a.Description}\" no período {a.Period}, incompatível com a apuração "
                              + (annual ? "anual (período 0)." : "trimestral (períodos 1 a 4)."), a.Description);
                    if (string.IsNullOrWhiteSpace(a.TableCode))
                        Block($"Ajuste \"{a.Description}\" sem código da tabela dinâmica.", a.Description);
                }

                if (data.Adjustments.All(a => a.Amount >= 0))
                    foreach (var p in EcfAssessment.Assess(data, s.AssessmentForm, request.Year))
                        if (p.Irpj.OffsetExceedsLimit || p.Csll.OffsetExceedsLimit)
                            Block($"Compensação de prejuízo do período {p.Period.Code} acima de 30% do lucro ajustado.", p.Period.Code);
            }

            if (data.Partners.Count == 0) Block("Quadro societário não informado nos parâmetros da ECF.", code);
            else
            {
                var total = data.Partners.Sum(p => p.TotalCapitalPercent);
                if (Math.Abs(total - 100m) > 0.01m)
                    Block($"Quadro societário deve somar 100% do capital (soma atual {total.ToString("N2", Br)}%).", code);
            }
        }

        foreach (var a in data.Adjustments.Where(a => a.Amount < 0))
            Block($"Ajuste \"{a.Description}\" com valor negativo.", a.Description);

        return issues.DistinctBy(i => (i.Blocking, i.Message)).ToList();
    }
}
