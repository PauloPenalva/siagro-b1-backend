using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// GAC-1177 (v2): trocar a liberação de uma Expedição que está numa carga, mesmo faturada, por
/// romaneios de compensação — estorno na origem (12, e 9 quando a origem tem perna de compra) e
/// Expedição nova no destino (8+7, ou só 7 com lote), todos com a data da carga e amarrados por
/// um <see cref="ShippingReleaseChange"/>.
/// </summary>
public class ShippingTransactionsChangeReleaseServiceTests
{
    private static readonly DateTime LoadDate = new(2026, 9, 10);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private decimal _lotBalance = 100_000m;

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private StorageTransactionsGetService GetService() =>
        new(_db, NullLogger<StorageTransactionsGetService>.Instance);

    private StorageTransactionsCreateService StorageCreate(FakeDocNumberSequenceService docNumbers) =>
        new(_db,
            docNumbers,
            new FakeBusinessPartnerService(new() { ["F0001"] = "Produtor Um", ["F0002"] = "Produtor Dois" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["01"] = "Armazém 01", ["02"] = "Armazém 02" }),
            Recalc(),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirmed() =>
        new(_db, new FakeStringLocalizer<Resource>(), Recalc(),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    // Uma sequência só para o teste inteiro: a Expedição e a troca não podem repetir código.
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private ShippingTransactionsCreateService CreateService()
    {
        var storageCreate = StorageCreate(_docNumbers);
        return new ShippingTransactionsCreateService(
            _db, storageCreate, StorageConfirmed(),
            new StorageTransactionsCopyService(_db, _docNumbers, storageCreate, new FakeStringLocalizer<Resource>()),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            Recalc(),
            new FakeStorageAddressBalanceReader(_lotBalance));
    }

    private ShippingTransactionsChangeReleaseService Service() =>
        new(_db,
            StorageCreate(_docNumbers),
            StorageConfirmed(),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            Recalc(),
            new ShipmentLoadsMovementLogService(_db.Context),
            new ShippingReleaseChangeReconciliationGuard(_db.Context),
            new FakeStorageAddressBalanceReader(_lotBalance));

    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedReleaseAsync(
        string contractCode,
        string cardCode,
        decimal releasedQuantity = 1500m,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        string? lotCode = null,
        ContractStatus contractStatus = ContractStatus.Approved,
        ReleaseStatus releaseStatus = ReleaseStatus.Actived,
        string itemCode = "SOJA",
        string warehouseCode = "01",
        decimal totalVolume = 10_000m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = contractCode,
            CardCode = cardCode,
            CardName = cardCode == "F0001" ? "Produtor Um" : "Produtor Dois",
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouseCode,
            Status = ContractStatus.Approved,
            TotalVolume = totalVolume,
            AllocatedVolume = 0m,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = warehouseCode,
            DeliveryLocationName = $"Armazém {warehouseCode}",
            ReleasedQuantity = releasedQuantity,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = origin,
            StorageAddressCode = lotCode,
        };

        if (lotCode != null && !await _db.Context.StorageAddresses.AnyAsync(x => x.Code == lotCode))
        {
            _db.Context.StorageAddresses.Add(new StorageAddress
            {
                Code = lotCode,
                Description = "Lote próprio",
                CardCode = "E0001",
                ItemCode = itemCode,
                WarehouseCode = warehouseCode,
                UoM = "KG",
                OwnershipType = StorageOwnershipType.OwnedInOurCustody,
                Status = StorageAddressStatus.Open,
            });
        }

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        // Status finais só depois de expedir: um contrato encerrado/liberação pausada não
        // aceitaria o embarque que monta o cenário.
        contract.Status = contractStatus;
        release.Status = releaseStatus;
        await _db.Context.SaveChangesAsync();

        return (contract, release);
    }

    /// <summary>
    /// Tabela de custo com desconto de qualidade: 3% inspecionado contra 1% de tolerância,
    /// fator 1 → 2% do bruto. É o que faz o líquido da perna 8 diferir do bruto.
    /// </summary>
    private async Task<(string ProcessingCostCode, QualityAttrib Attrib)> SeedDiscountTableAsync()
    {
        var attrib = new QualityAttrib { Code = "AVAR", Name = "Avariados", Type = QualityAttribType.Quality };
        _db.Context.Set<QualityAttrib>().Add(attrib);

        var processingCost = new ProcessingCost { Code = "000001", Description = "Soja", ReceiptPrice = 0.01m };
        processingCost.QualityParameters.Add(new ProcessingCostQualityParameter
        {
            ProcessingCostCode = "000001",
            QualityAttribCode = attrib.Code,
            MaxLimitRate = 1m,
            ExcessDiscountRate = 1m,
        });
        _db.Context.ProcessingCosts.Add(processingCost);
        await _db.Context.SaveChangesAsync();

        return ("000001", attrib);
    }

    /// <summary>
    /// Expedição real (pelo serviço de criação) sobre a liberação, com a perna de venda montada
    /// numa carga de data passada. <paramref name="invoicedQuantity"/> &gt; 0 cria uma nota
    /// normal confirmada da carga com essa quantidade (default = bruto) e marca a perna como
    /// faturada; 0 deixa a carga sem nota.
    /// </summary>
    private async Task<(ShippingTransaction Shipping, ShipmentLoad Load)> ShipIntoInvoicedLoadAsync(
        PurchaseContract contract,
        ShipmentRelease release,
        decimal gross,
        ShipmentLoad? load = null,
        decimal? invoicedQuantity = null,
        (string Code, QualityAttrib Attrib)? discountTable = null)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = contract.CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Pending,
            GrossWeight = gross,
            ShipmentReleaseKey = release.Key,
            TransactionTime = "07:15:00",
            // NF do produtor na perna de compra.
            InvoiceNumber = "000456",
            InvoiceSerie = "1",
            InvoiceQty = gross,
            ChaveNFe = "41260900000000000000550010000004561000004560",
        };

        if (discountTable is { } table)
        {
            st.ProcessingCostCode = table.Code;
            st.QualityInspections.Add(new StorageTransactionQualityInspection
            {
                Key = Guid.NewGuid(),
                QualityAttribCode = table.Attrib.Code,
                QualityAttrib = table.Attrib,
                Value = 3m,
            });
        }

        var withoutPurchaseLeg = ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin);
        var shipping = await CreateService().ExecuteAsync(
            withoutPurchaseLeg ? null : contract.Key, st, "tester");

        load ??= new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "01",
            LoadDate = LoadDate,
            Status = ShipmentLoadStatus.Open,
        };

        if (_db.Context.Entry(load).State == EntityState.Detached)
            _db.Context.ShipmentLoads.Add(load);

        var sales = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        sales.ShipmentLoadKey = load.Key;
        sales.TransactionStatus = StorageTransactionsStatus.Confirmed;
        load.TotalQuantity += gross;

        var invoiced = invoicedQuantity ?? gross;
        if (invoiced > 0)
        {
            var invoice = new SalesInvoice
            {
                Key = Guid.NewGuid(),
                CardCode = "C001",
                InvoiceNumber = "000000123",
                InvoiceStatus = InvoiceStatus.Confirmed,
                InvoiceType = SalesInvoiceType.Normal,
                ShipmentLoadKey = load.Key,
            };
            invoice.Items.Add(new SalesInvoiceItem
            {
                Key = Guid.NewGuid(),
                SalesInvoiceKey = invoice.Key,
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                Quantity = invoiced,
            });
            _db.Context.SalesInvoices.Add(invoice);

            sales.SalesInvoiceKey = invoice.Key;
            sales.TransactionStatus = StorageTransactionsStatus.Invoiced;
            sales.IsInvoiced = true;
            sales.InvoicedAt = LoadDate.AddHours(10);
            sales.InvoiceQty = gross;
            sales.SalesShipmentReleaseKey = Guid.NewGuid();
        }

        await _db.Context.SaveChangesAsync();

        // Carga coerente com as notas: InvoicedQuantity/Status pelo escritor único.
        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, null);
        await _db.Context.SaveChangesAsync();

        return (shipping, load);
    }

    private Task<decimal> WarehouseBalance(string warehouseCode) =>
        StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, warehouseCode, "SOJA");

    private Task<StorageTransaction> Tx(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<ShipmentRelease> ReleaseOf(Guid key) =>
        _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<PurchaseContract> ContractOf(Guid key) =>
        _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<ShipmentLoad> LoadOf(Guid key) =>
        _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<ShippingReleaseChange> ChangeOf(Guid originalSalesKey) =>
        _db.Context.ShippingReleaseChanges.AsNoTracking()
            .SingleAsync(x => x.OriginalSalesStorageTransactionKey == originalSalesKey);

    // ─────────────────────────────── 1 ───────────────────────────────

    [Fact]
    public async Task StandardToStandard_ReversesAtOrigin_AndCreatesNewShipmentAtDestination()
    {
        var table = await SeedDiscountTableAsync();
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var (c3, r3) = await SeedReleaseAsync("PC-003", "F0001", releasedQuantity: 5000m);
        var (_, load) = await ShipIntoInvoicedLoadAsync(c3, r3, 1000m);
        var originBefore = await WarehouseBalance("01");
        var destinationBefore = await WarehouseBalance("02");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m, load, discountTable: table);

        var originalSales = await Tx(shipping.SalesStorageTransactionKey);
        var originalPurchase = await Tx(shipping.PurchaseStorageTransactionKey!.Value);
        Assert.Equal(980m, originalPurchase.NetWeight); // cenário: líquido ≠ bruto
        Assert.True(originalPurchase.ReceiptServicePrice > 0m); // cenário: 8 original precificada

        await Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "Relatório do armazém", "tester");

        var change = await ChangeOf(originalSales.Key);

        // Original: substituída, fora da carga, status intacto.
        var sales = await Tx(originalSales.Key);
        var purchase = await Tx(originalPurchase.Key);
        Assert.Null(sales.ShipmentLoadKey);
        Assert.Equal(change.Key, sales.ReplacedByShippingReleaseChangeKey);
        Assert.Equal(change.Key, purchase.ReplacedByShippingReleaseChangeKey);
        Assert.Equal(StorageTransactionsStatus.Invoiced, sales.TransactionStatus);
        Assert.Equal(r1.Key, sales.ShipmentReleaseKey);

        // Estorno 12: data da carga, bruto original, sem liberação (origem Standard), sem carga/nota.
        var return12 = await Tx(change.ReturnSalesStorageTransactionKey);
        Assert.Equal(StorageTransactionType.SalesShipmentReturn, return12.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, return12.TransactionStatus);
        Assert.Equal(LoadDate, return12.TransactionDate);
        Assert.Equal(originalSales.TransactionTime, return12.TransactionTime);
        Assert.Equal(1000m, return12.GrossWeight);
        Assert.Equal(1000m, return12.NetWeight);
        Assert.Equal("01", return12.WarehouseCode);
        Assert.Null(return12.ShipmentReleaseKey);
        Assert.Null(return12.ShipmentLoadKey);
        Assert.Null(return12.SalesInvoiceKey);
        Assert.Null(return12.ReplacedByShippingReleaseChangeKey);
        Assert.Equal(change.Key, return12.ShippingReleaseChangeKey);
        Assert.Equal(originalSales.InvoiceNumber, return12.InvoiceNumber);
        Assert.Equal(originalSales.InvoiceQty, return12.InvoiceQty);
        Assert.Equal(originalSales.ChaveNFe, return12.ChaveNFe);

        // Estorno 9: mesmo líquido da 8 original, liberação de origem, alocação negativa.
        var return9 = await Tx(change.ReturnPurchaseStorageTransactionKey!.Value);
        Assert.Equal(StorageTransactionType.PurchaseReturn, return9.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, return9.TransactionStatus);
        Assert.Equal(LoadDate, return9.TransactionDate);
        Assert.Equal(originalPurchase.GrossWeight, return9.GrossWeight);
        Assert.Equal(originalPurchase.NetWeight, return9.NetWeight);
        Assert.Equal(originalPurchase.OthersDicount, return9.OthersDicount);
        Assert.Equal(r1.Key, return9.ShipmentReleaseKey);
        Assert.Null(return9.StorageAddressCode);
        Assert.Null(return9.ShipmentLoadKey);
        Assert.Equal(change.Key, return9.ShippingReleaseChangeKey);
        Assert.Equal("000456", return9.InvoiceNumber);
        Assert.Equal(originalPurchase.ChaveNFe, return9.ChaveNFe);
        Assert.Equal(0m, return9.ReceiptServicePrice);
        var reversal = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == return9.Key);
        Assert.Equal(c1.Key, reversal.PurchaseContractKey);
        Assert.Equal(-980m, reversal.Volume);

        // Nova 7: na carga, faturada, armazém/fornecedor do destino, data da carga.
        var newSales = await Tx(change.NewSalesStorageTransactionKey);
        Assert.Equal(StorageTransactionType.SalesShipment, newSales.TransactionType);
        Assert.Equal(load.Key, newSales.ShipmentLoadKey);
        Assert.Equal(StorageTransactionsStatus.Invoiced, newSales.TransactionStatus);
        Assert.Equal(originalSales.SalesInvoiceKey, newSales.SalesInvoiceKey);
        Assert.Equal("02", newSales.WarehouseCode);
        Assert.Equal("F0002", newSales.CardCode);
        Assert.Equal(r2.Key, newSales.ShipmentReleaseKey);
        Assert.Equal(LoadDate, newSales.TransactionDate);
        Assert.Equal(1000m, newSales.GrossWeight);
        Assert.Equal(change.Key, newSales.ShippingReleaseChangeKey);
        Assert.True(newSales.IsInvoiced);
        Assert.Equal(originalSales.InvoicedAt, newSales.InvoicedAt);
        Assert.NotNull(newSales.SalesShipmentReleaseKey);
        Assert.Equal(originalSales.SalesShipmentReleaseKey, newSales.SalesShipmentReleaseKey);
        Assert.Equal(1000m, newSales.InvoiceQty);

        // Nova 8: confirmada, alocada no destino pelo líquido.
        var newPurchase = await Tx(change.NewPurchaseStorageTransactionKey!.Value);
        Assert.Equal(StorageTransactionType.Purchase, newPurchase.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, newPurchase.TransactionStatus);
        Assert.Equal(980m, newPurchase.NetWeight);
        Assert.Equal("02", newPurchase.WarehouseCode);
        Assert.Null(newPurchase.ShipmentLoadKey);
        Assert.Null(newPurchase.StorageAddressCode);
        Assert.Equal(LoadDate, newPurchase.TransactionDate);
        // Outro fornecedor: nada da NF do produtor original nem da nossa nota.
        Assert.Null(newPurchase.InvoiceNumber);
        Assert.Null(newPurchase.InvoiceSerie);
        Assert.Equal(0m, newPurchase.InvoiceQty);
        Assert.Null(newPurchase.ChaveNFe);
        // Tarifas de serviço já estão na 8 original.
        Assert.Equal(0m, newPurchase.ReceiptServicePrice);
        Assert.Equal(0m, newPurchase.DryingServicePrice);
        Assert.Equal(0m, newPurchase.CleaningServicePrice);
        Assert.Equal(0m, newPurchase.ShipmentPrice);
        var allocation = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == newPurchase.Key);
        Assert.Equal(c2.Key, allocation.PurchaseContractKey);
        Assert.Equal(980m, allocation.Volume);

        var link = await _db.Context.ShippingTransactions.AsNoTracking()
            .SingleAsync(x => x.SalesStorageTransactionKey == newSales.Key);
        Assert.Equal(newPurchase.Key, link.PurchaseStorageTransactionKey);

        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(980m, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(980m, (await ReleaseOf(r2.Key)).ShippedQuantity);

        // Armazém: a origem volta ao saldo de antes da Expedição original (12 +1000 e 9 −980
        // compensam 8 +980 e 7 −1000); o destino recebe 8 nova +980 e 7 nova −1000.
        Assert.Equal(originBefore, await WarehouseBalance("01"));
        Assert.Equal(destinationBefore - 1000m + 980m, await WarehouseBalance("02"));

        var loadAfter = await LoadOf(load.Key);
        Assert.Equal(2000m, loadAfter.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, loadAfter.Status);

        // Documento completo.
        Assert.Equal(load.Key, change.ShipmentLoadKey);
        Assert.Equal(originalPurchase.Key, change.OriginalPurchaseStorageTransactionKey);
        Assert.Equal(r1.Key, change.SourceShipmentReleaseKey);
        Assert.Equal(r2.Key, change.TargetShipmentReleaseKey);
        Assert.Equal(1000m, change.OriginalQuantity);
        Assert.Equal(1000m, change.NewQuantity);
        Assert.Equal("Relatório do armazém", change.Reason);
        Assert.NotEqual(Guid.Empty, change.OperationGroupKey);
        Assert.Equal("tester", change.CreatedBy);

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == load.Key && x.MovementType == ShipmentLoadMovementType.ReleaseChanged);
        Assert.Equal(0m, movement.Quantity);
        Assert.Equal(loadAfter.AvailableQuantity, movement.BalanceAfter);
        Assert.Equal("Relatório do armazém", movement.Reason);
        Assert.Equal(newSales.Key, movement.StorageTransactionKey);
        Assert.Equal("F0002", movement.CardCode);
        Assert.Equal("02", movement.WarehouseCode);
        Assert.Contains(originalSales.Code!, movement.Description);
        Assert.Contains(newSales.Code!, movement.Description);
        Assert.Contains(return12.Code!, movement.Description);
        Assert.Contains("PC-001", movement.Description);
        Assert.Contains("PC-002", movement.Description);
    }

    /// <summary>
    /// O confirm do 9 recalcula pela tabela de custo ATUAL. Se ela mudou desde a 8 original, o
    /// estorno ainda precisa espelhar a original — senão sobra resíduo no contrato e na liberação.
    /// </summary>
    [Fact]
    public async Task ReturnPurchase_MirrorsTheOriginal_EvenWhenTheCostTableChangedSince()
    {
        var table = await SeedDiscountTableAsync();
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m, discountTable: table);

        var parameter = await _db.Context.Set<ProcessingCostQualityParameter>().SingleAsync();
        parameter.ExcessDiscountRate = 2m; // 2 pontos × 2 = 4% a partir de agora
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        var return9 = await Tx(change.ReturnPurchaseStorageTransactionKey!.Value);
        Assert.Equal(980m, return9.NetWeight);
        Assert.Equal(20m, return9.OthersDicount);
        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);

        // A Expedição nova é uma confirmação nova: usa a tabela vigente.
        var newPurchase = await Tx(change.NewPurchaseStorageTransactionKey!.Value);
        Assert.Equal(960m, newPurchase.NetWeight);
        Assert.Equal(960m, (await ContractOf(c2.Key)).AllocatedVolume);
    }

    // ─────────────────────────────── 2 ───────────────────────────────

    [Fact]
    public async Task SingleCurrentShipment_NewQuantityIsTheLoadInvoicedQuantity()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m, invoicedQuantity: 980m);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        Assert.Equal(1000m, change.OriginalQuantity);
        Assert.Equal(980m, change.NewQuantity);

        var newSales = await Tx(change.NewSalesStorageTransactionKey);
        Assert.Equal(980m, newSales.GrossWeight);
        Assert.Equal(980m, newSales.InvoiceQty);
        Assert.Equal(1000m, (await Tx(change.ReturnSalesStorageTransactionKey)).GrossWeight);

        var loadAfter = await LoadOf(load.Key);
        Assert.Equal(980m, loadAfter.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, loadAfter.Status);

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.MovementType == ShipmentLoadMovementType.ReleaseChanged);
        Assert.Equal(-20m, movement.Quantity);
    }

    [Fact]
    public async Task SingleCurrentShipment_WithoutInvoicedQuantity_Rejects()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m, invoicedQuantity: 0m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Equal("Carga sem faturamento: desvincule e estorne a Expedição.", ex.Message);
        Assert.Empty(await _db.Context.ShippingReleaseChanges.ToListAsync());
    }

    // ─────────────────────────────── 3 ───────────────────────────────

    [Fact]
    public async Task StandardToOwnershipTransfer_OnLotInOtherWarehouse_CreatesOnlyTheNewSalesLeg()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002",
            origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-02", warehouseCode: "02");
        var originBefore = await WarehouseBalance("01");
        var destinationBefore = await WarehouseBalance("02");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "Produtor informou", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        Assert.Null(change.NewPurchaseStorageTransactionKey);
        Assert.NotNull(change.ReturnPurchaseStorageTransactionKey);

        var return12 = await Tx(change.ReturnSalesStorageTransactionKey);
        Assert.Null(return12.ShipmentReleaseKey);

        var newSales = await Tx(change.NewSalesStorageTransactionKey);
        Assert.Equal("02", newSales.WarehouseCode);
        Assert.Equal("L-02", newSales.StorageAddressCode);
        Assert.Equal(StorageTransactionType.SalesShipment, newSales.TransactionType);
        Assert.Equal(r2.Key, newSales.ShipmentReleaseKey);
        Assert.Null(return12.StorageAddressCode);

        // Armazém: origem restaurada (12 e 9 compensam a original); destino só perde a 7 nova.
        Assert.Equal(originBefore, await WarehouseBalance("01"));
        Assert.Equal(destinationBefore - 1000m, await WarehouseBalance("02"));

        var link = await _db.Context.ShippingTransactions.AsNoTracking()
            .SingleAsync(x => x.SalesStorageTransactionKey == newSales.Key);
        Assert.Null(link.PurchaseStorageTransactionKey);

        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
    }

    // ─────────────────────────────── 4 ───────────────────────────────

    [Fact]
    public async Task OwnershipTransferToStandard_ReturnsToTheLot_AndCreatesPurchaseAndSalesLegs()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-YKT");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var originBefore = await WarehouseBalance("01");
        var destinationBefore = await WarehouseBalance("02");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        Assert.Null(shipping.PurchaseStorageTransactionKey);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "Armazém baixou do produtor", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        Assert.Null(change.OriginalPurchaseStorageTransactionKey);
        Assert.Null(change.ReturnPurchaseStorageTransactionKey);

        var return12 = await Tx(change.ReturnSalesStorageTransactionKey);
        Assert.Equal(r1.Key, return12.ShipmentReleaseKey);
        Assert.Equal("L-YKT", return12.StorageAddressCode);
        Assert.Equal(StorageTransactionsStatus.Confirmed, return12.TransactionStatus);
        Assert.Equal(StorageTransactionType.SalesShipmentReturn, return12.TransactionType);

        var newSales = await Tx(change.NewSalesStorageTransactionKey);
        var newPurchase = await Tx(change.NewPurchaseStorageTransactionKey!.Value);
        Assert.Null(newSales.StorageAddressCode);
        Assert.Null(newPurchase.StorageAddressCode);
        Assert.Equal(StorageTransactionsStatus.Confirmed, newPurchase.TransactionStatus);
        Assert.Equal("F0002", newPurchase.CardCode);
        Assert.False(string.IsNullOrEmpty(newPurchase.Code));

        var allocation = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == newPurchase.Key);
        Assert.Equal(c2.Key, allocation.PurchaseContractKey);
        Assert.Equal(newPurchase.NetWeight, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(newPurchase.NetWeight, (await ReleaseOf(r2.Key)).ShippedQuantity);

        // Armazém: origem restaurada pelo 12; destino recebe 8 nova +líq e 7 nova −1000.
        Assert.Equal("02", newSales.WarehouseCode);
        Assert.Equal(originBefore, await WarehouseBalance("01"));
        Assert.Equal(destinationBefore - 1000m + newPurchase.NetWeight, await WarehouseBalance("02"));
    }

    // ─────────────────────────────── 5 ───────────────────────────────

    /// <summary>
    /// O estorno devolve ao lote o que a Expedição original tirou: trocar dentro do MESMO lote
    /// exige saldo líquido zero, e o leitor (que ainda debita a original) marcando 0 não pode
    /// recusar.
    /// </summary>
    [Fact]
    public async Task OwnershipTransferToOwnershipTransfer_OnTheSameLot_WithZeroReaderBalance_Succeeds()
    {
        var (_, r1) = await SeedReleaseAsync("PC-001", "F0001", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-A");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-A");
        var c1 = await ContractOf(r1.PurchaseContractKey);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        _lotBalance = 0m;

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        Assert.Equal("L-A", (await Tx(change.NewSalesStorageTransactionKey)).StorageAddressCode);
        Assert.Equal("L-A", (await Tx(change.ReturnSalesStorageTransactionKey)).StorageAddressCode);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
    }

    // ─────────────────────────────── 6 ───────────────────────────────

    [Fact]
    public async Task Swap_StandardAndTransfer_EachNewShipmentRepeatsTheOriginalGross()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", releasedQuantity: 1000m);
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", releasedQuantity: 1000m,
            origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-A");
        var warehouseBefore = await WarehouseBalance("01");
        var (s1, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var (s2, _) = await ShipIntoInvoicedLoadAsync(c2, r2, 800m, load);

        await Service().ExecuteAsync(
            [new(s1.SalesStorageTransactionKey, r2.Key), new(s2.SalesStorageTransactionKey, r1.Key)],
            "Ordem financeira", "tester");

        var change1 = await ChangeOf(s1.SalesStorageTransactionKey);
        var change2 = await ChangeOf(s2.SalesStorageTransactionKey);
        Assert.Equal(change1.OperationGroupKey, change2.OperationGroupKey);
        Assert.NotEqual(change1.Key, change2.Key);

        // s1 (Standard, 1000) → transferência: só 7 nova, no lote.
        Assert.Equal(1000m, change1.NewQuantity);
        Assert.Null(change1.NewPurchaseStorageTransactionKey);
        var new1 = await Tx(change1.NewSalesStorageTransactionKey);
        Assert.Equal(1000m, new1.GrossWeight);
        Assert.Equal("L-A", new1.StorageAddressCode);

        // s2 (transferência, 800) → Standard: 8+7 nova, sem lote; 12 devolve ao lote.
        Assert.Equal(800m, change2.NewQuantity);
        var new2 = await Tx(change2.NewSalesStorageTransactionKey);
        Assert.Equal(800m, new2.GrossWeight);
        Assert.Null(new2.StorageAddressCode);
        Assert.NotNull(change2.NewPurchaseStorageTransactionKey);
        Assert.Equal("L-A", (await Tx(change2.ReturnSalesStorageTransactionKey)).StorageAddressCode);

        Assert.Equal(800m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
        Assert.Equal(800m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(1800m, (await LoadOf(load.Key)).TotalQuantity);

        // Mesmo armazém nas duas pontas: as originais são inteiramente compensadas pelos
        // estornos; sobra o efeito das novas — 7 de 1000 (transferência) e 8+7 de 800 (Standard).
        Assert.Equal(warehouseBefore - 1000m + (800m - 800m), await WarehouseBalance("01"));
        Assert.Equal(2, await _db.Context.ShipmentLoadMovements
            .CountAsync(x => x.MovementType == ShipmentLoadMovementType.ReleaseChanged));
    }

    /// <summary>
    /// Mesmo fornecedor no destino: a 8 nova herda a NF do produtor da 8 original.
    /// </summary>
    [Fact]
    public async Task StandardToStandard_SameProducer_KeepsTheProducerInvoiceOnTheNewPurchaseLeg()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0001");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var originalPurchase = await Tx(shipping.PurchaseStorageTransactionKey!.Value);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        var newPurchase = await Tx(change.NewPurchaseStorageTransactionKey!.Value);
        Assert.Equal("000456", newPurchase.InvoiceNumber);
        Assert.Equal(originalPurchase.InvoiceSerie, newPurchase.InvoiceSerie);
        Assert.Equal(originalPurchase.InvoiceQty, newPurchase.InvoiceQty);
        Assert.Equal(originalPurchase.ChaveNFe, newPurchase.ChaveNFe);
    }

    /// <summary>
    /// Liberação de origem Finalizada/Pausada: os estornos passam pela trava de movimentação sem
    /// a chave da liberação e só a recebem depois do confirm.
    /// </summary>
    [Theory]
    [InlineData(ReleaseStatus.Completed, ReleaseOrigin.Standard)]
    [InlineData(ReleaseStatus.Paused, ReleaseOrigin.Standard)]
    [InlineData(ReleaseStatus.Completed, ReleaseOrigin.OwnershipTransfer)]
    public async Task Accepts_WhenSourceReleaseIsCompletedOrPaused(ReleaseStatus status, ReleaseOrigin origin)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", origin: origin,
            lotCode: origin == ReleaseOrigin.OwnershipTransfer ? "L-A" : null);
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var tracked = await _db.Context.ShipmentReleases.SingleAsync(x => x.Key == r1.Key);
        tracked.Status = status;
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        var return12 = await Tx(change.ReturnSalesStorageTransactionKey);
        Assert.Equal(StorageTransactionsStatus.Confirmed, return12.TransactionStatus);
        if (origin == ReleaseOrigin.Standard)
        {
            Assert.Null(return12.ShipmentReleaseKey);
            var return9 = await Tx(change.ReturnPurchaseStorageTransactionKey!.Value);
            Assert.Equal(StorageTransactionsStatus.Confirmed, return9.TransactionStatus);
            Assert.Equal(r1.Key, return9.ShipmentReleaseKey);
        }
        else
        {
            Assert.Equal(r1.Key, return12.ShipmentReleaseKey);
        }

        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
    }

    /// <summary>
    /// Romaneio cancelado/devolvido que ainda aponta a carga não é vigente: a carga continua com
    /// UMA Expedição vigente e a quantidade nova é o faturado.
    /// </summary>
    [Theory]
    [InlineData(StorageTransactionsStatus.Cancelled)]
    [InlineData(StorageTransactionsStatus.Returned)]
    public async Task CancelledOrReturnedShipmentInTheLoad_IsNotCountedAsCurrent(StorageTransactionsStatus status)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (c3, r3) = await SeedReleaseAsync("PC-003", "F0001");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m, invoicedQuantity: 980m);
        var (other, _) = await ShipIntoInvoicedLoadAsync(c3, r3, 1000m, load, invoicedQuantity: 0m);
        var otherSales = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == other.SalesStorageTransactionKey);
        otherSales.TransactionStatus = status;
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        Assert.Equal(980m, change.NewQuantity);
        Assert.Equal(load.Key, (await Tx(other.SalesStorageTransactionKey)).ShipmentLoadKey);
    }

    // ─────────────────────────────── 7 ───────────────────────────────

    [Fact]
    public async Task NewShipment_CanBeChangedAgain_AndTheReplacedOneCannot()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (_, r3) = await SeedReleaseAsync("PC-003", "F0001");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "primeira", "tester");
        var first = await ChangeOf(shipping.SalesStorageTransactionKey);

        await Service().ExecuteAsync([new(first.NewSalesStorageTransactionKey, r3.Key)], "segunda", "tester");
        var second = await ChangeOf(first.NewSalesStorageTransactionKey);

        Assert.NotEqual(first.OperationGroupKey, second.OperationGroupKey);
        var firstNew = await Tx(first.NewSalesStorageTransactionKey);
        Assert.Equal(second.Key, firstNew.ReplacedByShippingReleaseChangeKey);
        Assert.Null(firstNew.ShipmentLoadKey);
        Assert.Equal(load.Key, (await Tx(second.NewSalesStorageTransactionKey)).ShipmentLoadKey);

        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(0m, (await ReleaseOf(r2.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r3.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await LoadOf(load.Key)).TotalQuantity);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r3.Key)], "de novo", "tester"));
        Assert.Contains("substituído", ex.Message);
    }

    // ─────────────────────────────── 8 ───────────────────────────────

    private async Task SeedReconciliationAsync(
        string code, string warehouseCode, WarehouseReconciliationStatus status, DateTime referenceDate)
    {
        _db.Context.WarehouseReconciliations.Add(new WarehouseReconciliation
        {
            Key = Guid.NewGuid(),
            Code = code,
            Status = status,
            WarehouseCode = warehouseCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            ReferenceDate = referenceDate,
            ReasonKey = Guid.NewGuid(),
        });
        await _db.Context.SaveChangesAsync();
    }

    [Theory]
    [InlineData("02", 0)]  // armazém de destino, mesma data da carga
    [InlineData("01", 3)]  // armazém de origem, depois da carga
    public async Task Rejects_WhenApprovedReconciliationIsNotBeforeTheLoadDate(string warehouseCode, int daysAfter)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        await SeedReconciliationAsync("CS000009", warehouseCode, WarehouseReconciliationStatus.Approved,
            LoadDate.AddDays(daysAfter));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Equal(
            $"A conferência de saldo CS000009 do armazém {warehouseCode} ({LoadDate.AddDays(daysAfter):dd/MM/yyyy}) " +
            "tem data igual ou posterior à data da carga. Cancele a conferência antes de trocar a liberação.",
            ex.Message);
    }

    [Theory]
    [InlineData(WarehouseReconciliationStatus.Draft, 0)]
    [InlineData(WarehouseReconciliationStatus.Cancelled, 0)]
    [InlineData(WarehouseReconciliationStatus.Approved, -1)]
    public async Task Accepts_WhenReconciliationIsNotApprovedOrIsBeforeTheLoadDate(
        WarehouseReconciliationStatus status, int daysAfter)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        await SeedReconciliationAsync("CS000009", "02", status, LoadDate.AddDays(daysAfter));

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        Assert.Single(await _db.Context.ShippingReleaseChanges.ToListAsync());
    }

    // ─────────────────────────────── 9 ───────────────────────────────

    [Fact]
    public async Task Rejects_WhenSourceContractIsFinished()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == c1.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("PC-001", ex.Message);
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetContractIsFinished()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", contractStatus: ContractStatus.Finished);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("PC-002", ex.Message);
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task Accepts_SalesReturnReleases_WithFinishedContracts()
    {
        // Origem: liberação de devolução de um contrato encerrado.
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", origin: ReleaseOrigin.SalesReturn);
        // Destino: outra liberação de devolução, também de contrato encerrado.
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", origin: ReleaseOrigin.SalesReturn,
            contractStatus: ContractStatus.Finished);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == c1.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        Assert.Null(change.ReturnPurchaseStorageTransactionKey);
        Assert.Null(change.NewPurchaseStorageTransactionKey);
        Assert.Equal(r1.Key, (await Tx(change.ReturnSalesStorageTransactionKey)).ShipmentReleaseKey);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
    }

    // ─────────────────────────────── 10 ───────────────────────────────

    [Fact]
    public async Task Rejects_WhenTargetReleaseHasNoBalance()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", releasedQuantity: 500m);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("Saldo insuficiente na liberação do contrato PC-002", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetContractHasNoBalance()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", totalVolume: 500m);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        // Números formatados pela cultura do runner: confere só o texto fixo.
        Assert.StartsWith("O contrato PC-002 não tem saldo para ", ex.Message);
        Assert.Contains("(disponível ", ex.Message);
        Assert.Empty(await _db.Context.ShippingReleaseChanges.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Rejects_WithoutReason(string? reason)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], reason, "tester"));

        Assert.Contains("motivo", ex.Message);
    }

    [Theory]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    public async Task Rejects_WhenTargetReleaseIsNotActive(ReleaseStatus status)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", releaseStatus: status);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("não está ativa", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetIsOtherItem()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", itemCode: "MILHO");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("produto", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetIsTheCurrentRelease()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r1.Key)], "motivo", "tester"));

        Assert.Contains("já está", ex.Message);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    [InlineData(ShipmentLoadStatus.Returned)]
    public async Task Rejects_WhenLoadIsCancelledOrReturned(ShipmentLoadStatus status)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        load.Status = status;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("CG000001", ex.Message);
    }

    [Fact]
    public async Task Rejects_DuplicatedShipment()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key), new(shipping.SalesStorageTransactionKey, r1.Key)],
            "motivo", "tester"));

        Assert.Contains("mais de uma vez", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetLotHasNoBalance()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-YKT");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        _lotBalance = 10m;

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("Saldo insuficiente no lote", ex.Message);
    }

    // ─────────────────────────────── 11 ───────────────────────────────

    /// <summary>
    /// Cancelar a carga depois da troca devolve à Montagem só a Expedição vigente (a nova); a
    /// substituída já está fora da carga e continua fora.
    /// </summary>
    [Fact]
    public async Task CancellingTheLoadAfterTheChange_ReleasesOnlyTheNewShipment()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (c3, r3) = await SeedReleaseAsync("PC-003", "F0001");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m, invoicedQuantity: 0m);
        var (other, _) = await ShipIntoInvoicedLoadAsync(c3, r3, 1000m, load, invoicedQuantity: 0m);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");
        var change = await ChangeOf(shipping.SalesStorageTransactionKey);

        var cancel = new ShipmentLoadsCancelService(
            _db,
            new ShipmentLoadsCompositionGuardService(_db.Context),
            new ShipmentLoadsMovementLogService(_db.Context),
            new ShipmentLoadsChangeLogService(_db.Context));
        await cancel.ExecuteAsync(load.Key, "Caminhão quebrou", "tester");

        var newSales = await Tx(change.NewSalesStorageTransactionKey);
        Assert.Null(newSales.ShipmentLoadKey);
        Assert.Equal(StorageTransactionsStatus.Confirmed, newSales.TransactionStatus);
        Assert.Null((await Tx(other.SalesStorageTransactionKey)).ShipmentLoadKey);

        var replaced = await Tx(shipping.SalesStorageTransactionKey);
        Assert.Null(replaced.ShipmentLoadKey);
        Assert.Equal(change.Key, replaced.ReplacedByShippingReleaseChangeKey);

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.MovementType == ShipmentLoadMovementType.Cancelled);
        Assert.Contains("2 romaneio(s)", movement.Description);
    }
}
