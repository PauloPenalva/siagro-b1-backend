using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// "Gerar título": refaz, de forma idempotente, o título a receber de um CT-e autorizado cuja geração falhou no gancho
/// de autorização. Limpa o motivo gravado em <c>FinancialError</c>.
/// </summary>
public class TransportDocumentsGenerateTitleService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    FinancialDocumentsTransportService financial)
{
    public const string NotAuthorized = "Só CT-e autorizado gera título.";
    public const string NoFinancial = "O CT-e não gera título: a natureza não cria financeiro.";

    /// <summary>Devolve os números dos títulos ativos do CT-e (criados agora ou já existentes).</summary>
    public async Task<string> GenerateAsync(Guid key, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.Include(x => x.Parties).FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Authorized)
            throw new DefaultException(NotAuthorized);
        if (!await financial.CreatesFinancialAsync(doc))
            return NoFinancial;

        await financial.EnqueueAsync(doc, user);
        doc.FinancialError = null;
        await db.SaveChangesAsync();

        var codes = await financial.ActiveTitleCodesAsync(doc.Key);
        return $"Título(s) a receber: {string.Join(", ", codes)}.";
    }
}
