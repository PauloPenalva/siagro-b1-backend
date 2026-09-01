using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// A regressão silenciosa mais grave da Carga: o documento de carga tem
/// <c>SalesTransactions</c> VAZIA, e <c>SalesInvoicesConfirmService</c> escolhia o ramo de
/// processamento contando essa coleção. Sem o resolvedor, a nota de carga cai no ramo AVULSO
/// e grava alocação de <b>ajuste fiscal</b> em vez de faturamento — corrompendo o saldo do
/// contrato sem erro nenhum na tela, e só por Estornar Confirmação → Confirmar.
/// </summary>
public class ShipmentLoadInvoiceBranchRegressionTests
{
    private static UsageService Usages(UnitOfWork db) =>
        new(db, NullLogger<UsageService>.Instance);

    private static SalesInvoicesConfirmService Confirm(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(Usages(db)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    [Fact]
    public async Task Confirming_a_load_invoice_writes_a_Billing_allocation_not_a_fiscal_adjustment()
    {
        var db = TestDb.CreateUnitOfWork();

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 100m,
            Status = ShipmentLoadStatus.Open,
        };

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        var release = SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 1_000m);

        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Pending);
        // O que caracteriza a nota de carga: aponta a carga e NÃO tem romaneio.
        invoice.ShipmentLoadKey = load.Key;
        SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, release.Key, quantity: 100m);

        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(release);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        await Confirm(db).ExecuteAsync(invoice.Key, "tester");

        var allocations = await db.Context.SalesContractsAllocations
            .AsNoTracking()
            .Where(a => a.SalesContractKey == contract.Key)
            .ToListAsync();

        var line = Assert.Single(allocations);
        Assert.Equal(SalesContractAllocationOrigin.Billing, line.Origin);
        Assert.Equal(100m, line.Volume);

        // E a liberação de entrega foi consumida — o que o ramo avulso não faria.
        Assert.Equal(release.Key, line.SalesShipmentReleaseKey);
    }

    [Fact]
    public async Task A_standalone_invoice_still_goes_through_the_fiscal_adjustment_branch()
    {
        // A rede de proteção do outro lado: a correção não pode ter arrastado o documento
        // avulso para o ramo de faturamento.
        var db = TestDb.CreateUnitOfWork();

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Pending);
        var item = SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, releaseKey: null, quantity: 100m);

        var usage = await Usages(db).CreateAsync(new Domain.Models.UsageModel
        {
            Name = "Ajuste fiscal",
            CfopOutgoingInState = "5949",
            CfopOutgoingOutState = "6949",
            ContractBalanceEffect = ContractBalanceEffect.Consume,
            ContractValueEffect = ContractValueEffect.None,
            RequiresContract = true,
            RequiresQuantity = false,
        });
        item.UsageCode = usage.Code;

        db.Context.SalesContracts.Add(contract);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        await Confirm(db).ExecuteAsync(invoice.Key, "tester");

        var line = Assert.Single(await db.Context.SalesContractsAllocations
            .AsNoTracking()
            .Where(a => a.SalesContractKey == contract.Key)
            .ToListAsync());

        Assert.Equal(SalesContractAllocationOrigin.FiscalAdjustment, line.Origin);
    }
}
