using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Compõe (aglutina) títulos firmes e de devolução no PROVISÓRIO de uma fixação (spec 2026-10-13, títulos firmes,
/// regras 9–12). Manual: quem decide em qual provisório cada firme entra é o operador, na ordem em que listou.
///
/// Firmes entram primeiro (na ordem listada), depois as devoluções, qualquer que seja a ordem recebida.
/// Firme entra por min(saldo do firme, espaço), espaço = valor do provisório − composto; sem espaço nenhum, recusa.
/// Devolução entra NEGATIVA pelo seu saldo (libera espaço), desde que o composto não fique abaixo do já baixado.
/// Tudo num SaveChanges: linha de composição, abatimento (InvoiceOffset) no título composto, recálculo e log nos dois.
/// </summary>
public class FinancialDocumentsComposeService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task ExecuteAsync(
        Guid provisionalKey, IReadOnlyList<Guid> composedKeys, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var context = db.Context;

        var provisional = await context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == provisionalKey)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (provisional.Nature != FinancialDocumentNature.Provisional)
            throw new ApplicationException($"O título {provisional.Code} não é provisório.");

        if (provisional.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {provisional.Code} está cancelado.");

        // Distinct mantém a ordem da primeira ocorrência: duplo clique ou chave repetida compõe uma vez só.
        var keys = composedKeys.Distinct().ToList();
        if (keys.Count == 0)
            throw new ApplicationException("Selecione ao menos um título firme ou de devolução.");

        var documents = await context.FinancialDocuments.Where(x => keys.Contains(x.Key)).ToListAsync();
        if (documents.Count != keys.Count)
            throw new NotFoundException("Documento financeiro não encontrado.");

        var composed = provisional.ComposedAmount;

        // Firmes antes das devoluções, cada grupo na ordem listada (OrderBy é estável): a devolução só libera espaço
        // sobre o composto final, e [devolução, firme] num provisório vazio não pode recusar o que [firme, devolução]
        // aceita (revisão final I6).
        var ordered = keys
            .Select(key => documents.Single(x => x.Key == key))
            .OrderBy(x => x.Nature == FinancialDocumentNature.Firm ? 0 : 1)
            .ToList();

        foreach (var document in ordered)
        {
            EnsureComposable(provisional, document);

            decimal amount;
            if (document.Nature == FinancialDocumentNature.Firm)
            {
                var space = decimal.Round(provisional.NetAmount - composed, 2, MidpointRounding.ToEven);
                if (space <= 0m)
                    throw new ApplicationException($"O título {provisional.Code} já está integralmente composto.");

                amount = Math.Min(document.OpenAmount, space);
            }
            else
            {
                amount = -document.OpenAmount;
                if (composed + amount < provisional.SettledAmount)
                    throw new ApplicationException(
                        $"A devolução {document.Code} deixaria o composto do título {provisional.Code} abaixo do valor já baixado: estorne a baixa antes.");
            }

            composed += amount;
            Enqueue(provisional, document, amount, userName);
        }

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, provisional.Key);
        foreach (var document in documents)
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }

    /// <summary>Texto do log de composição nos dois títulos: "código do outro: valor assinado N2 pt-BR".</summary>
    internal static string Describe(string? code, decimal amount) => $"{code}: {amount.ToString("N2", PtBr)}";

    /// <summary>Regra 9: mesmo CONTRATO (não o mesmo CardCode — produtor com várias IEs é vários parceiros).</summary>
    private static void EnsureComposable(FinancialDocument provisional, FinancialDocument document)
    {
        if (!document.IsContractFirmDocument)
            throw new ApplicationException(provisional.SalesContractKey is null
                ? $"O título {document.Code} não é firme nem devolução da conciliação fiscal."
                : $"O título {document.Code} não é firme nem devolução de documento de saída.");

        // Spec 2026-10-15, regra 13: o PAR compra/venda — um título de compra nunca casa com um de venda.
        if (!document.HasSameContractAs(provisional))
            throw new ApplicationException($"O título {document.Code} é de outro contrato.");

        // D12: o firme nasce em BRL; provisório em outra moeda (contrato em dólar) não compõe até a variação cambial.
        if (document.Currency != provisional.Currency)
            throw new ApplicationException(
                $"O título {document.Code} está em {Currency(document.Currency)} e o provisório {provisional.Code} em {Currency(provisional.Currency)}: composição entre moedas diferentes ainda não é suportada.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {document.Code} está cancelado.");

        if (document.OpenAmount <= 0m)
            throw new ApplicationException($"O título {document.Code} não tem saldo a compor.");
    }

    private static string Currency(CurrencyType currency) =>
        FinancialDocumentsContractCancellationGuardService.CurrencySymbol(currency);

    private void Enqueue(FinancialDocument provisional, FinancialDocument document, decimal amount, string userName)
    {
        var now = DateTime.Now;

        var composition = new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(),
            ProvisionalDocumentKey = provisional.Key,
            ComposedDocumentKey = document.Key,
            Amount = amount,
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        };
        db.Context.FinancialDocumentCompositions.Add(composition);

        // Regra 12: o abatimento no TÍTULO COMPOSTO, sem conta, sempre positivo (devolução abate o próprio saldo).
        db.Context.FinancialSettlements.Add(new FinancialSettlement // [settlement-posting: not applicable] composição não é pagamento
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = document.Key,
            FinancialAccountCode = null,
            SettlementDate = now.Date,
            Amount = Math.Abs(amount),
            Origin = FinancialSettlementOrigin.InvoiceOffset,
            RelatedDocumentKey = provisional.Key,
            CompositionKey = composition.Key,
            DocumentReference = provisional.Code,
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        });

        changeLog.Register(provisional.Key, FinancialDocumentChangeLogFields.Composition, null, Describe(document.Code, amount), userName);
        changeLog.Register(document.Key, FinancialDocumentChangeLogFields.Composition, null, Describe(provisional.Code, amount), userName);
    }
}
