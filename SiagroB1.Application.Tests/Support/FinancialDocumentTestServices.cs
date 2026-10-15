using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Monta <see cref="FinancialDocumentsCancelService"/> e <see cref="FinancialDocumentsGenerateService"/>
/// para os testes de serviços de contrato que passaram a recebê-los (Task 7: cancelamento,
/// encerramento, reabertura e estorno de fixação).
///
/// Existe pelo mesmo motivo de <see cref="TestNotificationOutbox"/>: os testes que NÃO tratam
/// de documento financeiro não precisam saber montar o construtor inteiro (UnitOfWork,
/// DocNumberSequenceService, IBusinessPartnerService) — só precisam de uma instância funcional
/// que não atrapalhe.
/// </summary>
public static class FinancialDocumentTestServices
{
    public static FinancialDocumentsCancelService Cancel(AppDbContext context) =>
        new(new UnitOfWork(context));

    public static FinancialDocumentsContractCancellationGuardService CancellationGuard(
        AppDbContext context) => new(new UnitOfWork(context));

    public static FinancialDocumentsGenerateService Generate(AppDbContext context) => new(
        context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string>
        {
            ["F0001"] = "PRODUTOR TESTE",
            ["C0001"] = "CLIENTE TESTE",
        }));

    /// <summary>
    /// Sync do firme da conciliação (spec 2026-10-13, títulos firmes), com numeração em memória. A natureza é lida pelo
    /// UsageService local (D10); em "SAPB1" o modo decide e a natureza nem é consultada.
    /// </summary>
    public static FinancialDocumentsFirmSyncService FirmSync(
        IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE") =>
        new(db, numbers ?? new FakeDocNumberSequenceService(), new FinancialDocumentChangeLogService(db.Context),
            new UsageService(db, NullLogger<UsageService>.Instance), TestConfiguration.Erp(erp),
            new FinancialDocumentsNettingService(db));

    /// <summary>Sync do firme a receber (spec 2026-10-15), com numeração em memória e a natureza lida pelo UsageService local.</summary>
    public static FinancialDocumentsSalesFirmSyncService SalesFirmSync(
        IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE")
    {
        var sequence = numbers ?? new FakeDocNumberSequenceService();
        return new(db, sequence, new FinancialDocumentChangeLogService(db.Context), FirmSync(db, sequence, erp),
            new FinancialDocumentsNettingService(db));
    }
}
