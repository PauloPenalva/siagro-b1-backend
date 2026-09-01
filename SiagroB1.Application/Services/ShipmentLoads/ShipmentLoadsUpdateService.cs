using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Edição dos dados cadastrais da carga informados pela Logística.
/// </summary>
/// <remarks>
/// A regra de imutabilidade acompanha o que já saiu em documento fiscal, e não o status por si:
/// <list type="bullet">
/// <item><c>Planned</c> e <c>Open</c> — tudo editável.</item>
/// <item><c>PartiallyInvoiced</c> e <c>Invoiced</c> — produto, unidade e filial TRAVADOS, porque
/// já viraram linha de nota; motorista, transportadora, frete, excesso, cliente, armazém e
/// observações seguem editáveis, que é o caso real de "o motorista trocou depois de
/// carregar".</item>
/// <item><c>Cancelled</c> — documento morto, nada editável.</item>
/// </list>
/// <para>
/// <b>A placa é caso à parte.</b> Ela é a chave de homogeneidade da vinculação, então trocá-la
/// numa carga que já tem romaneios deixaria a carga inconsistente com a própria composição —
/// romaneios de uma placa dentro de uma carga de outra. A recusa nomeia o romaneio conflitante,
/// e o caminho é desvincular antes.
/// </para>
/// </remarks>
public class ShipmentLoadsUpdateService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoad> ExecuteAsync(ShipmentLoad input, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == input.Key) ??
                   throw new NotFoundException($"Shipment load not found key {input.Key}");

        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException(
                $"A carga {load.Code} está cancelada e não pode ser alterada.");

        Validate(input);

        var fiscalFieldsLocked = load.Status is ShipmentLoadStatus.PartiallyInvoiced
            or ShipmentLoadStatus.Invoiced;

        if (fiscalFieldsLocked)
            EnsureFiscalFieldsUnchanged(load, input);

        await EnsureTruckChangeIsSafeAsync(load, input);

        var changes = DescribeChanges(load, input);

        load.LoadDate = input.LoadDate;
        load.TruckCode = input.TruckCode;
        load.TruckDriverCode = input.TruckDriverCode;
        load.TruckDriverName = input.TruckDriverName;
        load.CarrierCardCode = input.CarrierCardCode;
        load.CarrierName = input.CarrierName;
        load.CardCode = input.CardCode;
        load.CardName = input.CardName;
        load.WarehouseCode = input.WarehouseCode;
        load.WarehouseName = input.WarehouseName;
        load.HasExcess = input.HasExcess;
        load.FreightPrice = input.FreightPrice;
        load.Comments = input.Comments;

        if (!fiscalFieldsLocked)
        {
            load.BranchCode = input.BranchCode;
            load.ItemCode = input.ItemCode;
            load.ItemName = input.ItemName;
            load.UnitOfMeasureCode = input.UnitOfMeasureCode;
        }

        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;

        try
        {
            await db.BeginTransactionAsync();

            if (changes.Count > 0)
            {
                movementLog.Register(
                    load.Key,
                    ShipmentLoadMovementType.Updated,
                    decimal.Zero,
                    load.AvailableQuantity,
                    "Dados da carga alterados: " + string.Join("; ", changes) + ".",
                    userName);
            }

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

    private static void Validate(ShipmentLoad input)
    {
        if (string.IsNullOrWhiteSpace(input.BranchCode))
            throw new ApplicationException("Informe a filial da carga.");

        if (string.IsNullOrWhiteSpace(input.TruckCode))
            throw new ApplicationException("Informe a placa do veículo.");

        if (string.IsNullOrWhiteSpace(input.ItemCode))
            throw new ApplicationException("Informe o produto da carga.");

        if (string.IsNullOrWhiteSpace(input.UnitOfMeasureCode))
            throw new ApplicationException("Informe a unidade de medida do produto.");

        if (string.IsNullOrWhiteSpace(input.WarehouseCode))
            throw new ApplicationException("Informe o armazém de carga.");

        if (input.FreightPrice is < 0)
            throw new ApplicationException("O valor do frete não pode ser negativo.");
    }

    private static void EnsureFiscalFieldsUnchanged(ShipmentLoad load, ShipmentLoad input)
    {
        if (input.ItemCode != load.ItemCode)
            throw new ApplicationException(
                $"A carga {load.Code} já foi faturada e o produto não pode mais ser alterado.");

        if (input.UnitOfMeasureCode != load.UnitOfMeasureCode)
            throw new ApplicationException(
                $"A carga {load.Code} já foi faturada e a unidade de medida não pode mais ser alterada.");

        if (input.BranchCode != load.BranchCode)
            throw new ApplicationException(
                $"A carga {load.Code} já foi faturada e a filial não pode mais ser alterada.");
    }

    /// <summary>
    /// Trocar a placa só é seguro enquanto nenhum romaneio de outra placa estiver vinculado.
    /// </summary>
    private async Task EnsureTruckChangeIsSafeAsync(ShipmentLoad load, ShipmentLoad input)
    {
        if (input.TruckCode == load.TruckCode)
            return;

        var conflicting = await db.Context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == load.Key && x.TruckCode != input.TruckCode)
            .Select(x => new { x.Code, x.TruckCode })
            .FirstOrDefaultAsync();

        if (conflicting == null)
            return;

        throw new ApplicationException(
            $"O romaneio {conflicting.Code} vinculado à carga {load.Code} é do veículo " +
            $"{conflicting.TruckCode}. Desvincule-o antes de trocar a placa da carga.");
    }

    /// <summary>
    /// Descreve o que mudou ANTES de a entidade rastreada ser sobrescrita — depois da atribuição
    /// não haveria mais com o que comparar.
    /// </summary>
    private static List<string> DescribeChanges(ShipmentLoad load, ShipmentLoad input)
    {
        var changes = new List<string>();

        void Compare(string label, string? before, string? after)
        {
            if ((before ?? string.Empty) != (after ?? string.Empty))
                changes.Add($"{label}: '{before}' para '{after}'");
        }

        Compare("Veículo", load.TruckCode, input.TruckCode);
        Compare("Motorista", load.TruckDriverName, input.TruckDriverName);
        Compare("Transportadora", load.CarrierName, input.CarrierName);
        Compare("Cliente", load.CardName, input.CardName);
        Compare("Armazém", load.WarehouseCode, input.WarehouseCode);
        Compare("Produto", load.ItemCode, input.ItemCode);
        Compare("Filial", load.BranchCode, input.BranchCode);
        Compare("Observações", load.Comments, input.Comments);

        if (load.LoadDate.Date != input.LoadDate.Date)
            changes.Add($"Data: '{load.LoadDate:dd/MM/yyyy}' para '{input.LoadDate:dd/MM/yyyy}'");

        if (load.HasExcess != input.HasExcess)
            changes.Add($"Excesso: '{(load.HasExcess ? "Sim" : "Não")}' para '{(input.HasExcess ? "Sim" : "Não")}'");

        if (load.FreightPrice != input.FreightPrice)
            changes.Add($"Valor do frete: '{load.FreightPrice:N2}' para '{input.FreightPrice:N2}'");

        return changes;
    }
}
