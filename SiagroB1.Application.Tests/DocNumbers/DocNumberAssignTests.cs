using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.DocNumbers;

public class DocNumberAssignTests
{
    private static readonly Guid AutoKey = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ManualKey = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class StubService : DocNumberSequenceService
    {
        public Dictionary<Guid, DocNumberSeries> Series { get; } = [];
        public int GetDocNumberCalls { get; private set; }
        private int _counter;

        public StubService() : base(null!) { }

        public override Task<DocNumberSeries?> GetSeriesAsync(Guid key) =>
            Task.FromResult(Series.GetValueOrDefault(key));

        public override Task<string> GetDocNumber(Guid docNumberKey)
        {
            GetDocNumberCalls++;
            return Task.FromResult("F" + (++_counter).ToString("000000"));
        }

        public override Task<Guid> GetKeyByTransactionCode(TransactionCode transactionCode) =>
            Task.FromResult(AutoKey);
    }

    private static StubService CreateStub(TransactionCode tc = TransactionCode.Supplier)
    {
        var stub = new StubService();
        stub.Series[AutoKey] = new DocNumberSeries(AutoKey, tc, "Automático", false, false);
        stub.Series[ManualKey] = new DocNumberSeries(ManualKey, tc, "Manual", true, false);
        return stub;
    }

    private static DocNumberRequest Request(
        TransactionCode tc, Guid? key, string? typed, Func<string, Task<bool>>? exists = null, int max = 9) =>
        new(tc, key, typed, max, exists ?? (_ => Task.FromResult(false)));

    [Fact]
    public async Task Manual_ReturnsTrimmedTypedCode_WithoutTouchingCounter()
    {
        var stub = CreateStub();

        var result = await stub.AssignAsync(Request(TransactionCode.Supplier, ManualKey, " F002448 "));

        Assert.Equal("F002448", result.Code);
        Assert.Equal(ManualKey, result.DocNumberKey);
        Assert.Equal(0, stub.GetDocNumberCalls);
    }

    [Fact]
    public async Task Manual_RejectsCodeAlreadyInUse()
    {
        var stub = CreateStub();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => stub.AssignAsync(
            Request(TransactionCode.Supplier, ManualKey, "F002448", _ => Task.FromResult(true))));

        Assert.Contains("já está em uso", ex.Message);
    }

    [Fact]
    public async Task Manual_RejectsCodeLongerThanColumn()
    {
        var stub = CreateStub();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => stub.AssignAsync(
            Request(TransactionCode.Supplier, ManualKey, "DE0000001X", max: 9)));

        Assert.Contains("passa de 9 caracteres", ex.Message);
    }

    [Fact]
    public async Task Manual_EmptyTypedCode_FallsBackToDefaultAutomatic()
    {
        var stub = CreateStub();

        var result = await stub.AssignAsync(Request(TransactionCode.Supplier, ManualKey, "   "));

        Assert.Equal(AutoKey, result.DocNumberKey);
        Assert.Equal("F000001", result.Code);
    }

    [Fact]
    public async Task Manual_NotAllowedForStorageTransaction()
    {
        var stub = CreateStub(TransactionCode.StorageTransaction);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => stub.AssignAsync(
            Request(TransactionCode.StorageTransaction, ManualKey, "X1")));

        Assert.Contains("não aceita numeração manual", ex.Message);
    }

    [Fact]
    public async Task Automatic_SkipsCodesAlreadyInUse()
    {
        var stub = CreateStub();
        HashSet<string> taken = ["F000001", "F000002", "F000003"];

        var result = await stub.AssignAsync(
            Request(TransactionCode.Supplier, null, null, c => Task.FromResult(taken.Contains(c))));

        Assert.Equal("F000004", result.Code);
        Assert.Equal(AutoKey, result.DocNumberKey);
    }

    [Fact]
    public async Task Rejects_InactiveSeries()
    {
        var stub = CreateStub();
        stub.Series[AutoKey] = stub.Series[AutoKey] with { Inactive = true };

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            stub.AssignAsync(Request(TransactionCode.Supplier, AutoKey, null)));

        Assert.Contains("inativa", ex.Message);
    }

    [Fact]
    public async Task Rejects_SeriesOfAnotherTransactionCode()
    {
        var stub = CreateStub(TransactionCode.Customer);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            stub.AssignAsync(Request(TransactionCode.Supplier, AutoKey, null)));

        Assert.Contains("não pertence a este documento", ex.Message);
    }
}
