using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Regras compartilhadas pelos serviços de LINHA do documento de entrada.
///
/// Existem porque a grade da tela grava por POST/PATCH/DELETE direto em
/// <c>/PurchaseInvoicesItems</c>, sem passar pelo <c>PurchaseInvoicesUpdateService</c>. Deixar a
/// guarda só no cabeçalho abriria a porta dos fundos para alterar documento confirmado.
/// </summary>
internal static class PurchaseInvoiceLineGuard
{
    public static async Task EnsureParentIsPendingAsync(IUnitOfWork db, Guid? purchaseInvoiceKey)
    {
        if (purchaseInvoiceKey is null)
            throw new DefaultException("Linha sem documento de entrada informado.");

        var status = await db.Context.PurchaseInvoices
            .Where(x => x.Key == purchaseInvoiceKey)
            .Select(x => (InvoiceStatus?)x.InvoiceStatus)
            .FirstOrDefaultAsync();

        if (status is null)
            throw new NotFoundException("Documento de entrada não encontrado.");

        if (status != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ter as linhas alteradas. " +
                "Estorne a confirmação antes.");
    }

    /// <summary>
    /// Resolve a descrição do produto SÓ quando ela não veio — ou quando o produto MUDOU.
    ///
    /// A descrição lida do XML é a que CONSTA NA NOTA e vale mais que a do cadastro; e o código do
    /// emitente pode nem existir no cadastro local, caso em que sobrescrever apagaria a descrição
    /// em vez de melhorá-la.
    ///
    /// "Não veio" inclui VAZIO, não só null: o <c>create()</c> do UI5 é obrigado a declarar toda
    /// propriedade editável para a primeira digitação não abrir "Must not change a property before
    /// it has been read", então a linha chega com <c>""</c>. E ela chega assim mesmo quando o
    /// operador escolheu o produto no value help: a descrição é copiada para a tela com group ID
    /// <c>null</c> de propósito (campo desnormalizado é do servidor) e NÃO entra no POST/PATCH.
    /// Sem esta resolução no servidor, a descrição aparecia na tela e gravava em branco.
    /// </summary>
    /// <param name="itemCodeChanged">
    /// Verdadeiro quando o produto da linha foi TROCADO. Nesse caso a descrição que chegou é a do
    /// produto ANTERIOR, pelo mesmo motivo acima — copiá-la deixaria a linha com o código de um
    /// produto e a descrição de outro.
    /// </param>
    public static async Task<string?> ResolveItemNameAsync(
        IItemService itemService, string? itemCode, string? itemName, bool itemCodeChanged = false)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            return itemName;

        if (!itemCodeChanged)
            return string.IsNullOrWhiteSpace(itemName)
                ? (await itemService.GetByIdAsync(itemCode))?.ItemName
                : itemName;

        // Produto trocado por um código fora do cadastro (o value help filtra por grupo, o XML
        // não): manter a descrição que veio é melhor que esvaziar a linha.
        return (await itemService.GetByIdAsync(itemCode))?.ItemName ?? itemName;
    }

    /// <summary>
    /// Valida a amarração da linha com o contrato de compra.
    ///
    /// Mora aqui, e não em um serviço, porque são TRÊS os caminhos que gravam linha — deep-insert
    /// do documento, POST de linha e PATCH de linha —, mais o SyncItems do cabeçalho, que cobre
    /// as linhas que chegam no payload do PATCH do cabeçalho. A grade da tela alcança cada um por
    /// uma ação diferente. Deixar a regra em um só deles é o erro previsível: já aconteceu com a
    /// re-resolução da descrição do produto, que ficou só no SyncItems e não valia para a troca de
    /// produto pela grade do Edit.
    ///
    /// Linha SEM contrato passa: o campo é opcional por decisão de projeto.
    /// </summary>
    public static async Task EnsureContractIsCompatibleAsync(
        IUnitOfWork db, Guid? purchaseContractKey, string? itemCode, string cardCode)
    {
        if (purchaseContractKey is null)
            return;

        var contract = await db.Context.PurchaseContracts
            .AsNoTracking()
            .Where(x => x.Key == purchaseContractKey)
            .Select(x => new { x.Code, x.CardCode, x.ItemCode, x.Status })
            .FirstOrDefaultAsync();

        if (contract is null)
            throw new DefaultException("Contrato de compra informado na linha não foi encontrado.");

        // Encerrado ENTRA de propósito: a NF chega com frequência depois de o contrato fechar, e
        // recusá-la deixaria essa nota sem como ser conciliada.
        if (contract.Status != ContractStatus.Approved && contract.Status != ContractStatus.Finished)
            throw new DefaultException(
                $"O contrato {contract.Code}, amarrado na linha do produto {itemCode}, não está " +
                "aprovado nem encerrado e não pode ser amarrado a um documento de entrada.");

        if (contract.CardCode != cardCode)
            throw new DefaultException(
                $"O contrato {contract.Code}, amarrado na linha do produto {itemCode}, é de " +
                "outro fornecedor e não pode ser amarrado a este documento.");

        if (contract.ItemCode != itemCode)
            throw new DefaultException(
                $"O contrato {contract.Code}, amarrado na linha do produto {itemCode}, é de " +
                "outro produto e não pode ser amarrado a esta linha.");
    }
}
