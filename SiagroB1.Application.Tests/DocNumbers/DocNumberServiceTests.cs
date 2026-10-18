using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.DocNumbers;

public class DocNumberServiceTests
{
    private static DocNumber Row(TransactionCode tc, string name, bool isDefault = false,
        bool isManual = false, bool inactive = false) => new()
    {
        Key = Guid.NewGuid(), TransactionCode = tc, Name = name,
        Default = isDefault, IsManual = isManual, Inactive = inactive
    };

    private static async Task<(DocNumberService Service, string Db)> Seed(params DocNumber[] rows)
    {
        var db = Guid.NewGuid().ToString();
        var uow = TestDb.CreateUnitOfWork(db);
        uow.Context.DocNumbers.AddRange(rows);
        await uow.Context.SaveChangesAsync();
        return (new DocNumberService(TestDb.CreateUnitOfWork(db)), db);
    }

    private static DocNumber Read(string db, Guid key) =>
        TestDb.CreateUnitOfWork(db).Context.DocNumbers.AsNoTracking().Single(d => d.Key == key);

    [Fact]
    public async Task SettingDefault_UnsetsOtherDefaultsOfSameType()
    {
        var oldDefault = Row(TransactionCode.Supplier, "Antiga", isDefault: true);
        var otherType = Row(TransactionCode.Customer, "Outro tipo", isDefault: true);
        var (service, db) = await Seed(oldDefault, otherType);

        await service.CreateAsync(Row(TransactionCode.Supplier, "Nova", isDefault: true));

        Assert.False(Read(db, oldDefault.Key).Default);
        Assert.True(Read(db, otherType.Key).Default);
    }

    [Fact]
    public async Task SettingDefault_OnUpdate_UnsetsOthers()
    {
        var oldDefault = Row(TransactionCode.Supplier, "Antiga", isDefault: true);
        var other = Row(TransactionCode.Supplier, "Outra");
        var (service, db) = await Seed(oldDefault, other);

        var tracked = await service.GetByIdAsync(other.Key);
        tracked!.Default = true;
        await service.UpdateAsync(other.Key, tracked);

        Assert.False(Read(db, oldDefault.Key).Default);
        Assert.True(Read(db, other.Key).Default);
    }

    [Fact]
    public async Task ManualSeries_CannotBeDefault()
    {
        var (service, _) = await Seed();
        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.CreateAsync(Row(TransactionCode.Supplier, "Manual", isDefault: true, isManual: true)));
        Assert.Equal("A série Manual não pode ser a padrão.", ex.Message);
    }

    [Fact]
    public async Task SecondManualSeries_IsRejected()
    {
        var (service, _) = await Seed(Row(TransactionCode.Supplier, "Manual", isManual: true));
        await Assert.ThrowsAsync<ApplicationException>(() =>
            service.CreateAsync(Row(TransactionCode.Supplier, "Manual 2", isManual: true)));
    }

    [Fact]
    public async Task ManualSeries_RejectedForFinancialDocument()
    {
        var (service, _) = await Seed();
        await Assert.ThrowsAsync<ApplicationException>(() =>
            service.CreateAsync(Row(TransactionCode.FinancialDocument, "Manual", isManual: true)));
    }

    [Fact]
    public async Task ManualSeries_CannotBeDeleted()
    {
        var manual = Row(TransactionCode.Supplier, "Manual", isManual: true);
        var (service, db) = await Seed(manual);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.DeleteAsync(manual.Key));
        Assert.Equal("A série Manual não pode ser excluída.", ex.Message);
        Assert.NotNull(Read(db, manual.Key));
    }

    [Fact]
    public async Task ManualFlag_CannotChangeOnUpdate_TrackedEntity()
    {
        var auto = Row(TransactionCode.Supplier, "Auto");
        var (service, _) = await Seed(auto);

        var tracked = await service.GetByIdAsync(auto.Key); // caminho do PATCH: entidade já rastreada
        tracked!.IsManual = true;
        await Assert.ThrowsAsync<ApplicationException>(() => service.UpdateAsync(auto.Key, tracked));
    }

    [Fact]
    public async Task ManualFlag_CannotChangeOnUpdate_DetachedEntity()
    {
        var manual = Row(TransactionCode.Supplier, "Manual", isManual: true);
        var (service, _) = await Seed(manual);

        var detached = Row(TransactionCode.Supplier, "Manual", isManual: false);
        detached.Key = manual.Key; // caminho do PUT
        await Assert.ThrowsAsync<ApplicationException>(() => service.UpdateAsync(manual.Key, detached));
    }

    [Fact]
    public async Task DefaultSeries_CannotBeInactivated()
    {
        var def = Row(TransactionCode.Supplier, "Auto", isDefault: true);
        var (service, _) = await Seed(def);

        var tracked = await service.GetByIdAsync(def.Key);
        tracked!.Inactive = true;
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.UpdateAsync(def.Key, tracked));
        Assert.Equal("A série padrão não pode ser inativada.", ex.Message);
    }

    [Fact]
    public async Task ManualSeries_CannotBeRenamed()
    {
        var manual = Row(TransactionCode.Supplier, "Manual", isManual: true);
        var (service, _) = await Seed(manual);

        var tracked = await service.GetByIdAsync(manual.Key);
        tracked!.Name = "Outro nome";
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.UpdateAsync(manual.Key, tracked));
        Assert.Equal("A série Manual não pode ser renomeada nem mudar de tipo de documento.", ex.Message);
    }

    [Fact]
    public async Task ManualSeries_CannotChangeTransactionCode()
    {
        var manual = Row(TransactionCode.Supplier, "Manual", isManual: true);
        var (service, _) = await Seed(manual);

        var detached = Row(TransactionCode.Customer, "Manual", isManual: true);
        detached.Key = manual.Key;
        await Assert.ThrowsAsync<ApplicationException>(() => service.UpdateAsync(manual.Key, detached));
    }

    [Fact]
    public async Task DefaultSeries_CannotBeDeleted()
    {
        var def = Row(TransactionCode.Supplier, "Auto", isDefault: true);
        var (service, db) = await Seed(def);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.DeleteAsync(def.Key));
        Assert.Equal("A série padrão não pode ser excluída. Marque outra série como padrão antes.", ex.Message);
        Assert.NotNull(Read(db, def.Key));
    }
}
