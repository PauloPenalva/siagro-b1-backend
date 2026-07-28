using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// O diff decide o que vai parar no WhatsApp de um grupo de pessoas. O risco não é errar um
/// rótulo: é uma coluna derivada entrar na lista e transformar um recálculo em lote numa
/// enxurrada de mensagens. Estes testes existem para travar a allow-list.
/// </summary>
public class ContractHeaderDiffBuilderTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <summary>
    /// Persiste um contrato e devolve a instância rastreada, pronta para ser alterada — é
    /// assim que o <c>UpdateService</c> enxerga a entidade quando monta o diff.
    /// </summary>
    private PurchaseContract TrackedPurchaseContract()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-000123",
            CardCode = "F0001",
            CardName = "AGRO XPTO LTDA",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 400_000m,
            StandardPrice = 2.40m,
            DeliveryEndDate = new DateTime(2026, 8, 25),
            Status = ContractStatus.Draft,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.SaveChanges();

        return contract;
    }

    private List<ContractNotificationFieldChange> DiffAfter(Action<PurchaseContract> change)
    {
        var contract = TrackedPurchaseContract();
        change(contract);

        return ContractHeaderDiffBuilder
            .Build(_db.Context.Entry(contract), NotificationDocumentType.PurchaseContract)
            .ToList();
    }

    [Fact]
    public void Build_VolumeChanged_ReportsLabelAndBothValuesInPtBr()
    {
        var changes = DiffAfter(c => c.TotalVolume = 500_000m);

        var change = Assert.Single(changes);
        Assert.Equal(nameof(PurchaseContract.TotalVolume), change.Field);
        Assert.Equal("Volume total", change.Label);
        Assert.Equal("400.000,000", change.OldValue);
        Assert.Equal("500.000,000", change.NewValue);
    }

    [Fact]
    public void Build_PriceChanged_UsesTwoDecimals()
    {
        var change = Assert.Single(DiffAfter(c => c.StandardPrice = 2.50m));

        Assert.Equal("Preço", change.Label);
        Assert.Equal("2,40", change.OldValue);
        Assert.Equal("2,50", change.NewValue);
    }

    [Fact]
    public void Build_DateChanged_UsesBrazilianFormat()
    {
        var change = Assert.Single(DiffAfter(c => c.DeliveryEndDate = new DateTime(2026, 8, 31)));

        Assert.Equal("Fim da entrega", change.Label);
        Assert.Equal("25/08/2026", change.OldValue);
        Assert.Equal("31/08/2026", change.NewValue);
    }

    [Fact]
    public void Build_PartnerChanged_LabelDependsOnDocumentType()
    {
        var change = Assert.Single(DiffAfter(c => c.CardName = "OUTRO FORNECEDOR"));

        Assert.Equal("Fornecedor", change.Label);
    }

    /// <summary>
    /// O teste que justifica a existência da allow-list. <c>AllocatedVolume</c> e
    /// <c>FixedVolume</c> são gravados por serviços de recálculo — inclusive um que percorre
    /// TODOS os contratos em aberto. Se entrarem no diff, esse recálculo vira spam.
    /// </summary>
    [Theory]
    [InlineData(nameof(PurchaseContract.AllocatedVolume))]
    [InlineData(nameof(PurchaseContract.FixedVolume))]
    [InlineData(nameof(PurchaseContract.UpdatedAt))]
    [InlineData(nameof(PurchaseContract.UpdatedBy))]
    [InlineData(nameof(PurchaseContract.Status))]
    [InlineData(nameof(PurchaseContract.ApprovalComments))]
    public void Build_DerivedOrAuditColumnChanged_IsIgnored(string property)
    {
        var contract = TrackedPurchaseContract();
        var entry = _db.Context.Entry(contract);

        entry.Property(property).CurrentValue = property switch
        {
            nameof(PurchaseContract.AllocatedVolume) or nameof(PurchaseContract.FixedVolume) => 99_000m,
            nameof(PurchaseContract.UpdatedAt) => DateTime.Now.AddDays(1),
            nameof(PurchaseContract.Status) => ContractStatus.Approved,
            _ => "mudou",
        };

        Assert.Empty(ContractHeaderDiffBuilder.Build(entry, NotificationDocumentType.PurchaseContract));
    }

    [Fact]
    public void Build_NothingChanged_ReturnsEmpty()
    {
        var contract = TrackedPurchaseContract();

        Assert.Empty(ContractHeaderDiffBuilder.Build(
            _db.Context.Entry(contract), NotificationDocumentType.PurchaseContract));
    }

    [Fact]
    public void Build_SeveralFieldsChanged_ReportsOneEntryEach()
    {
        var changes = DiffAfter(c =>
        {
            c.TotalVolume = 500_000m;
            c.StandardPrice = 2.50m;
        });

        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void Build_ValueBecameEmpty_RendersDashInsteadOfBlank()
    {
        var change = Assert.Single(DiffAfter(c => c.CardName = null));

        Assert.Equal("AGRO XPTO LTDA", change.OldValue);
        Assert.Equal("—", change.NewValue);
    }
}
