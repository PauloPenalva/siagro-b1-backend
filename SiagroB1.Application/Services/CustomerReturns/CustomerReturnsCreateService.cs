using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.CustomerReturns;

/// <summary>
/// Registra a devolução emitida pelo cliente.
///
/// Documento de CONTROLE: não grava linha de ledger, não recalcula contrato e não toca em
/// romaneio. O saldo já foi devolvido pela Conferência de Entrega — ver <see cref="CustomerReturn"/>.
/// </summary>
public class CustomerReturnsCreateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService)
{
    public async Task ExecuteAsync(CustomerReturn customerReturn, string userName)
    {
        if (customerReturn.Items.Count == 0)
            throw new DefaultException("Informe ao menos um item na devolução.");

        await EnsureAccessKeyIsFreeAsync(customerReturn.AccessKey, customerReturn.Key);

        customerReturn.CreatedAt = DateTime.Now;
        customerReturn.CreatedBy = userName;
        customerReturn.Status = CustomerReturnStatus.Registered;
        customerReturn.CardName ??=
            (await businessPartnerService.GetByIdAsync(customerReturn.CardCode))?.CardName;

        await db.Context.CustomerReturns.AddAsync(customerReturn);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Uma chave de NF-e, uma devolução registrada. O índice único no banco é a rede de
    /// segurança; esta checagem existe para a mensagem sair legível em pt-BR.
    /// Devolução cancelada não segura a chave — relançar é um caminho legítimo.
    /// </summary>
    private async Task EnsureAccessKeyIsFreeAsync(string? accessKey, Guid key)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            return;

        var duplicated = await db.Context.CustomerReturns
            .AnyAsync(x => x.AccessKey == accessKey &&
                           x.Status != CustomerReturnStatus.Cancelled &&
                           x.Key != key);

        if (duplicated)
            throw new DefaultException(
                $"Já existe devolução registrada com a chave de NF-e {accessKey}.");
    }
}
