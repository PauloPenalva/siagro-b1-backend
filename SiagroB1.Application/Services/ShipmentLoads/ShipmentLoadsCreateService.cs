using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Criação da carga pela LOGÍSTICA: o planejamento do carregamento, antes de o caminhão
/// carregar. A carga nasce vazia, em <see cref="ShipmentLoadStatus.Planned"/>, e os romaneios
/// de embarque são vinculados depois por
/// <see cref="ShipmentLoadsAttachTransactionsService"/>.
/// </summary>
/// <remarks>
/// Este serviço já foi o oposto: montava a carga a partir de uma seleção de romaneios e copiava
/// os atributos do primeiro deles. Aquele caminho foi REMOVIDO — a carga precisa existir antes
/// do carregamento, que é o problema que a feature resolve. As validações de elegibilidade e de
/// homogeneidade que moravam aqui migraram para a vinculação, onde passaram a comparar o
/// romaneio contra a CARGA em vez de contra o primeiro romaneio da seleção.
/// <para>
/// Placa, produto e filial são obrigatórios porque são a chave de homogeneidade da vinculação:
/// sem eles não há contra o que comparar o romaneio. O banco não ajuda a cobrar isso —
/// <c>TruckCode</c> é nullable na coluna, apesar do <c>NOT NULL</c> decorativo no atributo —,
/// então a obrigatoriedade é imposta aqui.
/// </para>
/// </remarks>
public class ShipmentLoadsCreateService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoad> ExecuteAsync(ShipmentLoad load, string userName)
    {
        Validate(load);

        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.ShipmentLoad);

        load.Key = Guid.NewGuid();
        load.Code = await docNumberSequence.GetDocNumber(docNumberKey);
        load.DocNumberKey = docNumberKey;
        load.Status = ShipmentLoadStatus.Planned;
        load.TotalQuantity = decimal.Zero;
        load.InvoicedQuantity = decimal.Zero;
        load.CancellationReason = null;
        load.CreatedAt = DateTime.Now;
        load.CreatedBy = userName;
        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;

        try
        {
            await db.BeginTransactionAsync();

            db.Context.ShipmentLoads.Add(load);

            // Primeiro SaveChanges: é ele que materializa a chave da carga, de que o movimento
            // precisa. A coluna do histórico tem FK, então não dá para gravar antes.
            await db.SaveChangesAsync();

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Planned,
                decimal.Zero,
                decimal.Zero,
                $"Carga planejada. Veículo {load.TruckCode}, produto {load.ItemCode}" +
                (string.IsNullOrWhiteSpace(load.TruckDriverName)
                    ? "."
                    : $", motorista {load.TruckDriverName}."),
                userName);

            await db.SaveChangesAsync();

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return load;
    }

    private static void Validate(ShipmentLoad load)
    {
        if (string.IsNullOrWhiteSpace(load.BranchCode))
            throw new ApplicationException("Informe a filial da carga.");

        if (string.IsNullOrWhiteSpace(load.TruckCode))
            throw new ApplicationException("Informe a placa do veículo.");

        if (string.IsNullOrWhiteSpace(load.ItemCode))
            throw new ApplicationException("Informe o produto da carga.");

        if (string.IsNullOrWhiteSpace(load.UnitOfMeasureCode))
            throw new ApplicationException("Informe a unidade de medida do produto.");

        if (string.IsNullOrWhiteSpace(load.WarehouseCode))
            throw new ApplicationException("Informe o armazém de carga.");

        if (load.FreightPrice is < 0)
            throw new ApplicationException("O valor do frete não pode ser negativo.");
    }
}
