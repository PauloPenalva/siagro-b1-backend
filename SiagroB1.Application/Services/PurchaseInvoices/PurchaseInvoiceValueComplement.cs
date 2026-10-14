using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Documento de entrada "Complemento de valor" (spec 2026-10-13 D11): o faturado ABAIXO do contrato. Regra única
/// das linhas, chamada pelos quatro caminhos que gravam linha (deep-insert, PATCH do cabeçalho, POST e PATCH de
/// linha — ver PurchaseInvoiceLineGuard) e de novo na confirmação, que cria os títulos firmes.
/// </summary>
public static class PurchaseInvoiceValueComplement
{
    /// <param name="standalone">
    /// STANDALONE exige natureza com "Gera financeiro"; em SAPB1 a natureza é o OUSG, sem a flag (D10), e basta existir.
    /// </param>
    public static async Task EnsureAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, IUsage usageService, bool standalone)
    {
        if (invoice.InvoiceType != PurchaseInvoiceType.ValueComplement)
        {
            var carrying = lines.FirstOrDefault(l => l.ComplementAmount != 0m);
            if (carrying is not null)
                throw new DefaultException(
                    $"Item {carrying.ItemCode}: valor de complemento só existe no documento do tipo Complemento de valor.");
            return;
        }

        if (invoice.IssuerType == DocumentIssuerType.Own)
            throw new DefaultException(
                "Complemento de valor é só escrituração por enquanto: lance-o como documento de terceiro. A emissão de NF-e de complemento vem depois.");

        foreach (var line in lines)
        {
            if (line.Quantity != 0m || line.UnitPrice != 0m)
                throw new DefaultException(
                    $"Item {line.ItemCode}: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.");

            if (line.ComplementAmount <= 0m)
                throw new DefaultException($"Item {line.ItemCode}: informe o valor do complemento.");

            if (line.PurchaseContractKey is null)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige o contrato de compra na linha.");

            if (line.UsageCode is not { } usageCode)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige a natureza de operação.");

            if (standalone && (await usageService.GetByIdAsync(usageCode))?.CreatesFinancialDocument != true)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a natureza {usageCode} não gera financeiro; o complemento de valor precisa de uma natureza que gere.");
        }
    }
}
