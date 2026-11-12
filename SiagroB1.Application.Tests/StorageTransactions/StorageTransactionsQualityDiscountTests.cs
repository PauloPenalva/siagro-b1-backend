using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Desconto de classificação calculado na confirmação do romaneio de entrada/compra.
/// </summary>
/// <remarks>
/// Atributos como PH e FN do trigo são apenas informativos (classificam TIPO 1, TIPO 2...) e
/// entram na tabela de custos com % de quebra (<c>ExcessDiscountRate</c>) zerada. O cálculo
/// ignorava a quebra e descontava todo o excedente sobre a tolerância: PH 77,5 + FN 254,0
/// com tolerância zero descontavam 331,5% do peso bruto (Yokotobi, tabela 000004).
/// </remarks>
public class StorageTransactionsQualityDiscountTests
{
    private const string ProcessingCostCode = "000004";
    private const decimal GrossWeight = 10_000m;

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsConfirmedService Service() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private async Task<StorageTransaction> SeedAsync(
        QualityAttribType attribType, decimal maxLimitRate, decimal excessDiscountRate, decimal inspectedValue)
    {
        var attrib = new QualityAttrib { Code = "PH", Name = "PH", Type = attribType };
        _db.Context.Set<QualityAttrib>().Add(attrib);

        var processingCost = new ProcessingCost { Code = ProcessingCostCode, Description = "Trigo" };
        processingCost.QualityParameters.Add(new ProcessingCostQualityParameter
        {
            ProcessingCostCode = ProcessingCostCode,
            QualityAttribCode = attrib.Code,
            MaxLimitRate = maxLimitRate,
            ExcessDiscountRate = excessDiscountRate,
        });
        _db.Context.ProcessingCosts.Add(processingCost);

        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "C0001",
            ItemCode = "TRIGO",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            ProcessingCostCode = ProcessingCostCode,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Pending,
            GrossWeight = GrossWeight,
        };
        transaction.QualityInspections.Add(new StorageTransactionQualityInspection
        {
            Key = Guid.NewGuid(),
            QualityAttribCode = attrib.Code,
            QualityAttrib = attrib,
            Value = inspectedValue,
        });
        _db.Context.StorageTransactions.Add(transaction);

        await _db.SaveChangesAsync();
        return transaction;
    }

    /// <summary>
    /// % de quebra zerada ou negativa = atributo informativo: não desconta nada, mesmo com o
    /// valor muito acima da tolerância.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Quality_attribute_without_discount_rate_does_not_discount(decimal excessDiscountRate)
    {
        var transaction = await SeedAsync(QualityAttribType.Quality, 0m, excessDiscountRate, 254.0m);

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Equal(0m, transaction.OthersDicount);
        Assert.Equal(GrossWeight, transaction.NetWeight);
    }

    /// <summary>
    /// Os parâmetros de impureza (limpeza) vêm da mesma tabela e seguem a mesma regra.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Cleaning_attribute_without_discount_rate_does_not_discount(decimal excessDiscountRate)
    {
        var transaction = await SeedAsync(QualityAttribType.Cleaning, 0m, excessDiscountRate, 77.5m);

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Equal(0m, transaction.CleaningDiscount);
        Assert.Equal(0m, transaction.CleaningServicePrice);
        Assert.Equal(GrossWeight, transaction.NetWeight);
    }

    /// <summary>
    /// O "Desconto %" é o fator aplicado a cada ponto acima da tolerância. Com fator 1, 3%
    /// inspecionado contra 1% de tolerância desconta 2% do bruto.
    /// </summary>
    [Theory]
    [InlineData(QualityAttribType.Quality)]
    [InlineData(QualityAttribType.Cleaning)]
    public async Task Discount_rate_of_one_discounts_the_excess_one_to_one(QualityAttribType attribType)
    {
        var transaction = await SeedAsync(attribType, 1m, 1m, 3m);

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Equal(200m, transaction.CleaningDiscount + transaction.OthersDicount);
        Assert.Equal(9_800m, transaction.NetWeight);
    }

    /// <summary>
    /// Caso da Yokotobi: impureza configurada com 2% de desconto e 0,7% informado no romaneio
    /// tem de descontar 1,4% do bruto. Antes o fator era ignorado e descontava só os 0,7%.
    /// </summary>
    [Theory]
    [InlineData(QualityAttribType.Cleaning)]
    [InlineData(QualityAttribType.Quality)]
    public async Task Discount_rate_multiplies_the_excess(QualityAttribType attribType)
    {
        var transaction = await SeedAsync(attribType, 0m, 2m, 0.7m);

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Equal(140m, transaction.CleaningDiscount + transaction.OthersDicount);
        Assert.Equal(9_860m, transaction.NetWeight);
    }

    /// <summary>
    /// GAC-1197: a tolerância aplicada fica gravada na linha da classificação, para a tela mostrar
    /// o que valeu no cálculo mesmo que a Tabela de Custos mude depois. Vale também para o
    /// atributo informativo (% de quebra zerada), que tem tolerância mas não desconta.
    /// </summary>
    [Theory]
    [InlineData(QualityAttribType.Quality, 1)]
    [InlineData(QualityAttribType.Cleaning, 1)]
    [InlineData(QualityAttribType.Quality, 0)]
    public async Task Confirming_records_the_tolerance_applied_to_the_inspection(
        QualityAttribType attribType, decimal excessDiscountRate)
    {
        var transaction = await SeedAsync(attribType, 1.5m, excessDiscountRate, 3m);

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Equal(1.5m, transaction.QualityInspections.Single().ToleranceRate);
    }

    /// <summary>
    /// Atributo fora da Tabela de Custos não tem tolerância: fica nulo (em branco na tela), e não
    /// zero — zero é uma tolerância de verdade.
    /// </summary>
    [Fact]
    public async Task Inspection_without_parameter_in_the_cost_table_has_no_tolerance()
    {
        var transaction = await SeedAsync(QualityAttribType.Quality, 1.5m, 1m, 3m);
        var other = new QualityAttrib { Code = "FN", Name = "FN", Type = QualityAttribType.Quality };
        _db.Context.Set<QualityAttrib>().Add(other);
        // Add no DbSet, e não na coleção: com Key preenchida, o EF trataria a linha nova como
        // Modified e o SaveChanges falharia.
        _db.Context.Set<StorageTransactionQualityInspection>().Add(new StorageTransactionQualityInspection
        {
            Key = Guid.NewGuid(),
            StorageTransactionKey = transaction.Key,
            QualityAttribCode = other.Code,
            QualityAttrib = other,
            Value = 254m,
            ToleranceRate = 9m,
        });
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Null(transaction.QualityInspections.Single(x => x.QualityAttribCode == "FN").ToleranceRate);
    }

    /// <summary>
    /// Umidade trabalha por faixa da tabela de secagem, não por tolerância.
    /// </summary>
    [Fact]
    public async Task Drying_inspection_has_no_tolerance()
    {
        var transaction = await SeedAsync(QualityAttribType.Drying, 1.5m, 1m, 3m);

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Null(transaction.QualityInspections.Single().ToleranceRate);
    }

    /// <summary>
    /// A cópia do romaneio (estorno, trocas) leva a tolerância gravada junto com o valor.
    /// </summary>
    [Fact]
    public void Copy_keeps_the_recorded_tolerance()
    {
        var original = new StorageTransactionQualityInspection
        {
            QualityAttribCode = "PH", Value = 3m, ToleranceRate = 1.5m,
        };

        var copy = StorageTransactionQualityInspectionCopyFactory.CreateFrom(original, null!);

        Assert.Equal(1.5m, copy.ToleranceRate);
    }
}
