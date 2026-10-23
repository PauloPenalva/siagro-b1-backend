using System.Globalization;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.Ecf;

/// <summary>
/// Inconsistências que impedem a geração da ECF: as da ECD do mesmo ano (contabilidade, meses fechados, plano
/// referencial, visões, resultado apurado…, por <see cref="EcdValidator.ValidateLedger"/>, porque os blocos J/K são
/// os da ECD; sem as regras dos dados do livro da ECD) e as
/// próprias da ECF. Estático de propósito: o Scrutor só registra classes terminadas em "Service".
/// </summary>
public static class EcfValidator
{
    private static readonly CultureInfo Br = new("pt-BR");

    public static List<EcdIssueDto> Validate(EcfData data, EcfRequest request)
    {
        // Regras da escrituração contábil (as da ECD), sem os dados do livro da ECD (NIRE, atos, CRC…)
        var issues = new List<EcdIssueDto>();
        EcdValidator.ValidateLedger(data.Ledger, request.Year, "ECF", issues, bookSettings: false);
        void Block(string message, string reference = "") =>
            issues.Add(new EcdIssueDto { Blocking = true, Message = message, Reference = reference });

        if (request.Year < EcfFileService.FirstSupportedYear)
            Block($"ECF disponível a partir do ano-calendário {EcfFileService.FirstSupportedYear}.");

        var code = data.Ledger.Declarant?.Code ?? "";
        if (data.Ledger.Declarant is { } declarant)
        {
            // 0030: endereço obrigatório
            if (new[] { declarant.Street, declarant.StreetNumber, declarant.District, declarant.ZipCode }.Any(string.IsNullOrWhiteSpace))
                Block("Endereço da filial matriz incompleto para a ECF (logradouro, número, bairro e CEP).", code);

            if (data.Settings is not { } s) Block("Parâmetros da ECF não cadastrados para a filial.", code);
            else
            {
                var cnae = s.MainCnae?.Trim() ?? "";
                if (cnae.Length != 7 || !cnae.All(char.IsAsciiDigit))
                    Block($"Parâmetros da ECF: CNAE principal {cnae} inválido (7 dígitos).", code);
                var nature = s.NatureCode?.Trim() ?? "";
                if (nature.Length != 4 || !nature.All(char.IsAsciiDigit))
                    Block("Natureza jurídica deve ter 4 dígitos.", code);
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
