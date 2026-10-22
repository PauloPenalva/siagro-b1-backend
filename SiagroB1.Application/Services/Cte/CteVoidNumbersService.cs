using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Inutiliza uma faixa de números do CT-e. A lista <c>CteNumberToVoid</c> é o registro: só o 102 marca
/// <c>VoidedAt</c>. O CT-e 4.00 pode não ter o serviço de inutilização (a Zeus não o expõe): nesse caso o número
/// continua na lista e nada é marcado.
/// </summary>
public class CteVoidNumbersService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteServiceContextFactory contexts,
    ICteSefazClient sefaz,
    Func<DateTimeOffset>? clock = null)
{
    public const string InvalidJustification = "A justificativa da inutilização tem de 15 a 255 caracteres.";
    public const string InvalidRange = "Informe uma faixa de números válida (inicial maior que zero e não maior que a final).";
    public const string NotAvailable =
        "A inutilização de numeração não está disponível para o CT-e 4.00 nesta versão; o número fica registrado na lista de descartados.";

    public async Task<NfeSefazResult> VoidAsync(string branchCode, int series, int from, int to, string? justification)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        var reason = (justification ?? string.Empty).Trim();
        if (reason.Length is < 15 or > 255)
            throw new DefaultException(InvalidJustification);
        if (from < 1 || to < from)
            throw new DefaultException(InvalidRange);

        var used = await db.Context.TransportDocuments.AsNoTracking()
            .Where(x => x.BranchCode == branchCode && x.Series == series && x.Number >= from && x.Number <= to
                        && (x.Status == CteStatus.Authorized || x.Status == CteStatus.Cancelled))
            .OrderBy(x => x.Number)
            .Select(x => new { x.Number, x.Status })
            .FirstOrDefaultAsync();
        if (used is not null)
            throw new DefaultException(
                $"O número {used.Number} pertence a um CT-e {(used.Status == CteStatus.Authorized ? "autorizado" : "cancelado")} e não pode ser inutilizado.");

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");
        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode)
                       ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração do CT-e.");
        var state = await CteIssueInputAssembler.IssuerStateAsync(db, branchCode);
        var stateCode = TransportDocumentsConfirmService.UfCodes.GetValueOrDefault(state)
                        ?? throw new DefaultException($"UF {state} sem código IBGE.");
        var year = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone).Year;

        using var context = await contexts.OpenAsync(branchCode, settings.Environment);

        NfeVoidNumberResult result;
        try
        {
            result = await sefaz.VoidNumberAsync(
                new CteVoidNumberRequest(NfeText.AlphaNumeric(branch.TaxId), stateCode, year, series, from, to, reason), context.Settings);
        }
        catch (NotSupportedException)
        {
            throw new DefaultException(NotAvailable);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException("Sem resposta da SEFAZ na inutilização: tente de novo.");
        }

        if (result.StatusCode != NfeStatusCodes.NumberVoided)
            return new NfeSefazResult(result.StatusCode, result.Reason);

        var pending = await db.Context.CteNumbersToVoid
            .Where(x => x.BranchCode == branchCode && x.Series == series && x.Number >= from && x.Number <= to && x.VoidedAt == null)
            .ToListAsync();
        foreach (var item in pending)
        {
            item.VoidedAt = DateTime.Now;
            item.VoidProtocol = result.Protocol;
        }
        await db.SaveChangesAsync();

        return new NfeSefazResult(result.StatusCode, result.Reason, result.Protocol);
    }
}
