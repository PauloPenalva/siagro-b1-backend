using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.OwnershipTransfers;

/// <summary>
/// Regras do vínculo entre a transferência de titularidade e o contrato de compra.
/// Fonte única: roda na criação/alteração (para o usuário errar cedo) e de novo na
/// confirmação, que é a autoritativa — o saldo do contrato se move entre os dois
/// momentos.
/// </summary>
public class OwnershipTransfersValidateContractService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    /// <summary>
    /// Tolerância de comparação de saldo. Os saldos do contrato arredondam para 2
    /// casas, enquanto Quantity e ReleasedQuantity são DECIMAL(18,3): sem a folga,
    /// uma quantidade com a terceira casa preenchida passaria aqui e estouraria
    /// depois, na alocação feita pela Expedição de Grãos.
    /// </summary>
    private const decimal BalanceTolerance = 0.001m;

    /// <summary>
    /// Variante para os serviços de gravação, que ainda não têm os lotes carregados.
    /// Resolve os dois lotes pelos códigos da transferência e aplica as mesmas regras.
    /// </summary>
    public async Task<PurchaseContract?> ValidateForPersistAsync(OwnershipTransfer transfer)
    {
        if (!transfer.PurchaseContractKey.HasValue)
            return null;

        var origin = await db.Context.StorageAddresses.AsNoTracking()
                         .FirstOrDefaultAsync(x => x.Code == transfer.StorageAddressOriginCode)
                     ?? throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);

        var destination = await db.Context.StorageAddresses.AsNoTracking()
                              .FirstOrDefaultAsync(x => x.Code == transfer.StorageAddressDestinationCode)
                          ?? throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);

        return await ExecuteAsync(transfer, origin, destination);
    }

    /// <summary>
    /// Devolve o contrato carregado (rastreado, com as liberações incluídas) ou
    /// <c>null</c> quando a transferência não tem contrato — o vínculo é opcional.
    /// </summary>
    public async Task<PurchaseContract?> ExecuteAsync(
        OwnershipTransfer transfer,
        StorageAddress origin,
        StorageAddress destination)
    {
        if (!transfer.PurchaseContractKey.HasValue)
            return null;

        if (destination.OwnershipType != StorageOwnershipType.OwnedInOurCustody)
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_DESTINATION_NOT_OWN"].Value);

        // Origem já própria significaria a empresa "comprando" grão que já é dela:
        // baixaria contrato e emitiria liberação sem nenhuma compra real acontecer.
        if (origin.OwnershipType == StorageOwnershipType.OwnedInOurCustody)
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_ORIGIN_IS_OWN"].Value);

        // Include obrigatório: TotalAvailableToRelease deriva desta navegação.
        // AvaiableVolume vem de AllocatedVolume (persistido) e não precisa de include.
        var contract = await db.Context.PurchaseContracts
                           .Include(x => x.ShipmentReleases)
                           .FirstOrDefaultAsync(x => x.Key == transfer.PurchaseContractKey.Value)
                       ?? throw new NotFoundException(
                           resource["OWNERSHIP_TRANSFER_CONTRACT_NOT_FOUND"].Value);

        // A liberação nasce Actived, contornando ShipmentReleasesApprovationService —
        // que é onde esta guarda normalmente vive.
        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_NOT_APPROVED"].Value);

        // Sem amarração por CardCode: o fornecedor do contrato não precisa ser o dono
        // do lote de origem.
        if (!string.Equals(contract.ItemCode, transfer.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_ITEM_MISMATCH"].Value);

        // Igualdade estrita: não existe tabela de conversão de unidade no projeto.
        if (!string.Equals(contract.UnitOfMeasureCode, transfer.UomCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_UOM_MISMATCH"].Value);

        if (transfer.Quantity - contract.TotalAvailableToRelease > BalanceTolerance)
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_RELEASE_BALANCE"].Value);

        // O eixo de alocação não é consumido agora, mas precisa ter espaço: quem aloca
        // é o Purchase(8) da Expedição de Grãos, e essa alocação não pode falhar depois.
        if (transfer.Quantity - contract.AvaiableVolume > BalanceTolerance)
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_CONTRACT_ALLOCATION_BALANCE"].Value);

        return contract;
    }
}
