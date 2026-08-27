using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A situação de assinatura do contrato de COMPRA é fato documental: vale em qualquer status,
/// inclusive Encerrado e Cancelado, e por isso não passa pelo PATCH do cabeçalho (que só aceita
/// rascunho). Cada alteração deixa linha no log do contrato.
/// </summary>
public class PurchaseContractSignatureStatusTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsSetSignatureStatusService Service() => new(
        _db.Context,
        new PurchaseContractsChangeLogService(_db.Context));

    private async Task<PurchaseContract> SeedAsync(
        ContractStatus status, SignatureStatus? signature = null)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalVolume = 100_000m,
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            StandardPrice = 2m,
            Type = ContractType.Fixed,
            Status = status,
            SignatureStatus = signature,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    private Task<List<PurchaseContractChangeLog>> LogsAsync(Guid key) =>
        _db.Context.PurchaseContractsChangeLogs
            .Where(l => l.PurchaseContractKey == key)
            .ToListAsync();

    /// <summary>
    /// O ponto da feature: contrato encerrado é imutável em tudo o mais, mas a assinatura passa.
    /// </summary>
    [Theory]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    [InlineData(ContractStatus.Approved)]
    [InlineData(ContractStatus.Draft)]
    public async Task Execute_OnAnyStatus_UpdatesSignature(ContractStatus status)
    {
        var contract = await SeedAsync(status);

        await Service().ExecuteAsync(contract.Key, SignatureStatus.Signed, "joao");

        var reloaded = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        Assert.Equal(SignatureStatus.Signed, reloaded.SignatureStatus);
        Assert.Equal(status, reloaded.Status);
        Assert.Equal("joao", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Execute_RegistersChangeLogWithPtBrLabels()
    {
        var contract = await SeedAsync(ContractStatus.Finished, SignatureStatus.AwaitingSignature);

        await Service().ExecuteAsync(contract.Key, SignatureStatus.Signed, "maria");

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal(ContractChangeLogFields.SignatureStatus, log.Field);
        Assert.Equal("Aguardando Assinatura", log.OldValue);
        Assert.Equal("Assinado", log.NewValue);
        Assert.Equal("maria", log.ChangedBy);
    }

    /// <summary>
    /// Nunca marcado: o "de" fica nulo, que é como o log já representa "não havia valor".
    /// </summary>
    [Fact]
    public async Task Execute_FromNull_LogsNullOldValue()
    {
        var contract = await SeedAsync(ContractStatus.Approved);

        await Service().ExecuteAsync(contract.Key, SignatureStatus.AwaitingSignature, "joao");

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Null(log.OldValue);
        Assert.Equal("Aguardando Assinatura", log.NewValue);
    }

    /// <summary>Nulo é valor legítimo — limpa a situação e fica registrado no log.</summary>
    [Fact]
    public async Task Execute_WithNull_ClearsSignature()
    {
        var contract = await SeedAsync(ContractStatus.Approved, SignatureStatus.Signed);

        await Service().ExecuteAsync(contract.Key, null, "joao");

        var reloaded = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        Assert.Null(reloaded.SignatureStatus);

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal("Assinado", log.OldValue);
        Assert.Null(log.NewValue);
    }

    /// <summary>Reenviar o mesmo valor não pode poluir o log, que é lido pelo usuário.</summary>
    [Fact]
    public async Task Execute_WithSameValue_IsNoOp()
    {
        var contract = await SeedAsync(ContractStatus.Approved, SignatureStatus.Signed);

        await Service().ExecuteAsync(contract.Key, SignatureStatus.Signed, "joao");

        Assert.Empty(await LogsAsync(contract.Key));
    }
}
