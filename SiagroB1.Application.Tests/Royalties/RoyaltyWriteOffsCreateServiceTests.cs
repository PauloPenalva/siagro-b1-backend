using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Royalties;

public class RoyaltyWriteOffsCreateServiceTests
{
    private const string ValidCpf = "529.982.247-25";
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    public RoyaltyWriteOffsCreateServiceTests() =>
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });

    private StorageTransaction Receipt(string code, decimal net, RoyaltySituation situation = RoyaltySituation.Declared,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt, TransactionStatus = status,
            NetWeight = net, RoyaltySituation = situation, RoyaltyOriginCode = "P0001",
        };
        _db.Context.StorageTransactions.Add(st);
        return st;
    }

    private static RoyaltyWriteOffInput Input(string itsId, params (Guid Key, decimal Weight)[] lines) =>
        new(new DateTime(2026, 10, 10), itsId, ValidCpf, "Produtor Teste", null,
            lines.Select(l => new RoyaltyWriteOffLineInput(l.Key, l.Weight)).ToList());

    private RoyaltyWriteOffsCreateService Service() => new(_db);

    private decimal WrittenOff(Guid key) =>
        _db.Context.RoyaltyWriteOffLines
            .Where(l => l.StorageTransactionKey == key && l.RoyaltyWriteOff!.Status == RoyaltyWriteOffStatus.Active)
            .Sum(l => l.Weight);

    [Fact]
    public async Task Creates_a_write_off_covering_several_receipts()
    {
        var a = Receipt("R1", 30_000m);
        var b = Receipt("R2", 20_000m);
        await _db.SaveChangesAsync();

        var created = await Service().ExecuteAsync(Input("ITS-1", (a.Key, 30_000m), (b.Key, 5_000m)), "tester");

        var saved = await _db.Context.RoyaltyWriteOffs.Include(x => x.Lines).AsNoTracking().SingleAsync(x => x.Key == created.Key);
        Assert.Equal(RoyaltySituation.Declared, saved.Situation);
        Assert.Equal(RoyaltyWriteOffStatus.Active, saved.Status);
        Assert.Equal("52998224725", saved.TaxId);
        Assert.Equal("tester", saved.CreatedBy);
        Assert.Equal(2, saved.Lines.Count);
        Assert.Equal(5_000m, WrittenOff(b.Key));
    }

    [Fact]
    public async Task A_receipt_can_be_written_off_in_parts_down_to_zero_with_three_decimals()
    {
        var a = Receipt("R1", 1_234.567m);
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(Input("ITS-1", (a.Key, 1_000m)), "tester");
        await Service().ExecuteAsync(Input("ITS-2", (a.Key, 234.567m)), "tester");

        Assert.Equal(1_234.567m, WrittenOff(a.Key));
    }

    [Fact]
    public async Task Refuses_weight_above_the_balance()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        await Service().ExecuteAsync(Input("ITS-1", (a.Key, 600m)), "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-2", (a.Key, 500m)), "tester"));

        Assert.Contains("excede o saldo do romaneio R1", ex.Message);
        Assert.Equal(600m, WrittenOff(a.Key));
    }

    [Fact]
    public async Task Refuses_the_same_receipt_twice_in_one_write_off()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 600m), (a.Key, 400m)), "tester"));

        Assert.Contains("mais de uma vez", ex.Message);
    }

    [Fact]
    public async Task Refuses_mixed_situations()
    {
        var a = Receipt("R1", 1_000m, RoyaltySituation.Declared);
        var b = Receipt("R2", 1_000m, RoyaltySituation.Conventional);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 1m), (b.Key, 1m)), "tester"));

        Assert.Equal("Selecione romaneios de uma mesma situação.", ex.Message);
    }

    [Fact]
    public async Task Refuses_a_receipt_that_is_not_eligible()
    {
        var pending = Receipt("R1", 1_000m, status: StorageTransactionsStatus.Pending);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (pending.Key, 1m)), "tester"));

        Assert.Contains("não está apto", ex.Message);
    }

    [Fact]
    public async Task Refuses_an_its_id_already_used_by_an_active_write_off()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        await Service().ExecuteAsync(Input("ITS-1", (a.Key, 100m)), "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 100m)), "tester"));

        Assert.Contains("ID do ITS ITS-1 já utilizado", ex.Message);
    }

    [Theory]
    [InlineData("", ValidCpf, "Nome", "Informe o ID do ITS.")]
    [InlineData("ITS-1", "123", "Nome", "CPF/CNPJ inválido.")]
    [InlineData("ITS-1", ValidCpf, " ", "Informe o nome.")]
    public async Task Validates_the_header(string itsId, string taxId, string name, string message)
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        var input = new RoyaltyWriteOffInput(DateTime.Today, itsId, taxId, name, null,
            [new RoyaltyWriteOffLineInput(a.Key, 1m)]);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(input, "tester"));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Refuses_an_empty_write_off_and_non_positive_weights()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();

        var empty = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(Input("ITS-1"), "tester"));
        Assert.Equal("Selecione ao menos um romaneio.", empty.Message);
        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 0m)), "tester"));
        Assert.Contains("maior que zero", ex.Message);
    }

    [Fact]
    public async Task Refuses_over_long_fields()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        var lines = new[] { new RoyaltyWriteOffLineInput(a.Key, 1m) };

        var its = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            new RoyaltyWriteOffInput(DateTime.Today, new string('x', 51), ValidCpf, "Nome", null, lines), "tester"));
        var name = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            new RoyaltyWriteOffInput(DateTime.Today, "ITS-1", ValidCpf, new string('x', 201), null, lines), "tester"));
        var comments = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            new RoyaltyWriteOffInput(DateTime.Today, "ITS-1", ValidCpf, "Nome", new string('x', 501), lines), "tester"));

        Assert.Equal("O ID do ITS deve ter no máximo 50 caracteres.", its.Message);
        Assert.Equal("O nome deve ter no máximo 200 caracteres.", name.Message);
        Assert.Equal("As observações devem ter no máximo 500 caracteres.", comments.Message);
    }

    [Fact]
    public async Task Refuses_an_empty_guid_key()
    {
        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (Guid.Empty, 1m)), "tester"));

        Assert.Equal("Selecione ao menos um romaneio.", ex.Message);
    }

    [Fact]
    public async Task Accepts_a_receipt_and_a_purchase_of_the_same_situation()
    {
        var receipt = Receipt("R1", 1_000m);
        var purchase = Receipt("P1", 1_000m);
        purchase.TransactionType = StorageTransactionType.Purchase;
        await _db.SaveChangesAsync();

        var created = await Service().ExecuteAsync(Input("ITS-1", (receipt.Key, 10m), (purchase.Key, 20m)), "tester");

        var saved = await _db.Context.RoyaltyWriteOffs.Include(x => x.Lines).AsNoTracking().SingleAsync(x => x.Key == created.Key);
        Assert.Equal(2, saved.Lines.Count);
    }
}
