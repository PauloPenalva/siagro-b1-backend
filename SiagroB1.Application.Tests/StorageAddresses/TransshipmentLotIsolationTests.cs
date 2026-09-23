using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageAddresses;

/// <summary>
/// GAC-1181 fase 2, Task 7: o lote de transbordo (<see cref="StorageAddressNature.Transshipment"/>)
/// guarda mercadoria em trânsito, não armazenagem contratada. Estes testes isolam-no de três
/// lugares onde ele não deve aparecer — a Expedição de Grãos comum e os dois jobs de cobrança —
/// sem sumir com ele dos leitores de saldo, que continuam precisando enxergá-lo porque o saldo
/// dele é real.
///
/// Cada cenário de "esconde" grava DOIS lotes, um <see cref="StorageAddressNature.Regular"/> e
/// um <see cref="StorageAddressNature.Transshipment"/>, ambos elegíveis por todos os outros
/// critérios do serviço (mesmo item, mesmo armazém, com saldo, no status certo) — senão o lote
/// de transbordo poderia sumir por outro motivo qualquer, e o teste não provaria o filtro.
/// </summary>
public class TransshipmentLotIsolationTests
{
    private const string RegularCode = "L100";
    private const string TransshipmentCode = "L200";
    private static readonly DateTime Day = new(2026, 9, 1);

    private static StorageAddress NewAddress(string code, StorageAddressNature nature, string? processingCostCode = null) => new()
    {
        Code = code,
        Description = nature == StorageAddressNature.Transshipment ? "Lote de transbordo" : "Lote regular",
        CardCode = "C0001",
        ItemCode = "SOJA",
        WarehouseCode = "01",
        UoM = "KG",
        Status = StorageAddressStatus.Open,
        Nature = nature,
        ProcessingCostCode = processingCostCode,
    };

    private static StorageTransaction Receipt(string storageAddressCode, decimal netWeight, DateTime? date = null) => new()
    {
        Key = Guid.NewGuid(),
        StorageAddressCode = storageAddressCode,
        TransactionType = StorageTransactionType.Receipt,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        TransactionDate = date ?? Day,
        NetWeight = netWeight,
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
    };

    // ---- StorageAddressesListOpenedByItemService (Expedição de Grãos comum) ----

    /// <summary>
    /// Grava um lote regular e um de transbordo, ambos com saldo (um recebimento confirmado) e
    /// abertos para o mesmo item — o cenário compartilhado pelos dois testes abaixo.
    /// </summary>
    private static async Task<IUnitOfWork> SeedListOpenedByItemScenario()
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.StorageAddresses.AddRange(
            NewAddress(RegularCode, StorageAddressNature.Regular),
            NewAddress(TransshipmentCode, StorageAddressNature.Transshipment));

        db.Context.StorageTransactions.AddRange(
            Receipt(RegularCode, 1000m),
            Receipt(TransshipmentCode, 1000m));

        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return db;
    }

    [Fact]
    public async Task ListOpenedByItem_HidesTransshipmentLots()
    {
        var db = await SeedListOpenedByItemScenario();

        var result = await new StorageAddressesListOpenedByItemService(db).ExecuteAsync("SOJA");

        Assert.DoesNotContain(result, x => x.Code == TransshipmentCode);
    }

    /// <summary>
    /// Prova que o filtro não levou o lote comum junto: sem este teste, um filtro invertido por
    /// engano (que escondesse o Regular em vez do Transshipment) passaria despercebido.
    /// </summary>
    [Fact]
    public async Task ListOpenedByItem_StillListsRegularLots()
    {
        var db = await SeedListOpenedByItemScenario();

        var result = await new StorageAddressesListOpenedByItemService(db).ExecuteAsync("SOJA");

        var regular = Assert.Single(result, x => x.Code == RegularCode);
        Assert.Equal(1000m, regular.Balance);
    }

    // ---- Cobrança de armazenagem e quebra técnica ----

    /// <summary>
    /// Custo com armazenagem E quebra técnica configuradas, carência zero e intervalo de um
    /// dia — o mínimo para os dois calculadores gerarem cobrança no mesmo dia do recebimento,
    /// sem depender de janelas de vários dias.
    /// </summary>
    private static ProcessingCost NewBillableCost(string code) => new()
    {
        Code = code,
        Description = "Custo de teste",
        StoragePrice = 5m,
        StorageGraceDays = 0,
        StorageBillingIntervalDays = 1,
        TechnicalLossRate = 1m,
        TechnicalLossGraceDays = 0,
        TechnicalLossIntervalDays = 1,
        StoragePriceCalculationMethod = StoragePriceCalculationMethod.AfterFirstReceipting,
    };

    /// <summary>
    /// Grava um lote regular e um de transbordo, ambos abertos, com o mesmo custo cobrável e o
    /// mesmo recebimento e saldo diário — elegíveis por igual para os dois calculadores.
    /// </summary>
    private static async Task<IUnitOfWork> SeedChargeScenario()
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.ProcessingCosts.Add(NewBillableCost("PC01"));
        db.Context.StorageAddresses.AddRange(
            NewAddress(RegularCode, StorageAddressNature.Regular, "PC01"),
            NewAddress(TransshipmentCode, StorageAddressNature.Transshipment, "PC01"));

        foreach (var code in new[] { RegularCode, TransshipmentCode })
        {
            db.Context.StorageTransactions.Add(Receipt(code, 100m));
            db.Context.StorageDailyBalances.Add(new StorageDailyBalance
            {
                StorageAddressCode = code,
                BalanceDate = Day,
                ClosingBalance = 100m,
                BillableBalance = 100m,
            });
        }

        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return db;
    }

    [Fact]
    public async Task StorageCharge_SkipsTransshipmentLots()
    {
        var db = await SeedChargeScenario();

        await new StorageAddressesStorageChargeCalculatorService(db).CalculateAsync(Day);

        var chargedCodes = await db.Context.StorageCharges
            .Where(x => x.ChargeType == StorageChargeType.Storage)
            .Select(x => x.StorageAddressCode)
            .ToListAsync();

        Assert.Contains(RegularCode, chargedCodes);
        Assert.DoesNotContain(TransshipmentCode, chargedCodes);
    }

    [Fact]
    public async Task TechnicalLoss_SkipsTransshipmentLots()
    {
        var db = await SeedChargeScenario();

        var service = new StorageAddressesTechnicalLossCalculatorService(db, new FakeDocNumberSequenceService());
        await service.CalculateAsync(Day);

        var chargedCodes = await db.Context.StorageCharges
            .Where(x => x.ChargeType == StorageChargeType.TechnicalLoss)
            .Select(x => x.StorageAddressCode)
            .ToListAsync();

        Assert.Contains(RegularCode, chargedCodes);
        Assert.DoesNotContain(TransshipmentCode, chargedCodes);

        var lossTransactionCodes = await db.Context.StorageTransactions
            .Where(x => x.TransactionType == StorageTransactionType.TechnicalLoss)
            .Select(x => x.StorageAddressCode)
            .ToListAsync();

        Assert.Contains(RegularCode, lossTransactionCodes);
        Assert.DoesNotContain(TransshipmentCode, lossTransactionCodes);
    }

    // ---- Leitores de saldo: NÃO devem esconder o lote de transbordo ----

    /// <summary>
    /// Trava contra "limpar" o lote de transbordo de tudo por simetria com os três filtros
    /// acima. O lote tem saldo real — a sobra da operação de transbordo mora nele — e sumir
    /// com ele dos leitores de saldo criaria divergência entre o físico e o relatado.
    ///
    /// Exercita <see cref="StorageAddressesDailyBalanceBuilderService"/> (o leitor mais
    /// arriscado: mesmo formato de query dos dois calculadores acima, maior candidato a um
    /// futuro "conserto por simetria"). <see cref="StorageAddressesGetBalanceService"/> não é
    /// exercitado aqui porque soma <c>STORAGE_TRANSACTIONS</c> via Dapper/SQL puro sobre um
    /// <see cref="System.Data.IDbConnection"/> real — o provider InMemory não traduz esse SQL, e
    /// o próprio código-fonte confirma que ele nunca referencia <c>STORAGE_ADDRESSES</c> ou
    /// <c>Nature</c>: filtra só por <c>StorageAddressCode</c>, então é estruturalmente incapaz de
    /// discriminar por natureza. Pelo mesmo motivo (sem infraestrutura de FastReport nos testes),
    /// <see cref="Reports.Services.StorageAddressReportService"/> também não é exercitado aqui;
    /// sua query só filtra por <c>Status</c> quando o pedido pede explicitamente, nunca por
    /// natureza.
    /// </summary>
    [Fact]
    public async Task BalanceReaders_StillSeeTransshipmentLots()
    {
        var db = await SeedListOpenedByItemScenario();

        await new StorageAddressesDailyBalanceBuilderService(db).BuildAsync(Day);

        var balanceCodes = await db.Context.StorageDailyBalances
            .Where(x => x.BalanceDate == Day)
            .Select(x => x.StorageAddressCode)
            .ToListAsync();

        Assert.Contains(RegularCode, balanceCodes);
        Assert.Contains(TransshipmentCode, balanceCodes);

        var transshipmentBalance = await db.Context.StorageDailyBalances
            .SingleAsync(x => x.StorageAddressCode == TransshipmentCode && x.BalanceDate == Day);

        Assert.Equal(1000m, transshipmentBalance.ClosingBalance);
    }
}
