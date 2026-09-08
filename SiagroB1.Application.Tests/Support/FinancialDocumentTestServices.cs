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

    public static FinancialDocumentsGenerateService Generate(AppDbContext context) => new(
        context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string>
        {
            ["F0001"] = "PRODUTOR TESTE",
            ["C0001"] = "CLIENTE TESTE",
        }));
}
