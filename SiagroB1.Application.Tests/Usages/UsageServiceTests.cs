using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>
/// Cadastro local de natureza de operação (modo STANDALONE): as duas regras que não
/// existem no banco — efeito no contrato exige contrato, e exclusão de natureza já
/// usada é bloqueada (não há FK para USAGES, por ser dual-mode).
/// </summary>
public class UsageServiceTests
{
    private static UsageService Service(UnitOfWork db) =>
        new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Model(
        ContractBalanceEffect balance = ContractBalanceEffect.None,
        ContractValueEffect value = ContractValueEffect.None,
        bool requiresContract = false) => new()
        {
            Name = "Complemento de preço",
            CfopOutgoingInState = "5949",
            CfopOutgoingOutState = "6949",
            ContractBalanceEffect = balance,
            ContractValueEffect = value,
            RequiresContract = requiresContract,
            RequiresQuantity = true,
        };

    [Fact]
    public async Task Create_rejects_value_effect_without_requiring_contract()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).CreateAsync(Model(value: ContractValueEffect.Add)));
    }

    [Fact]
    public async Task Create_rejects_balance_effect_without_requiring_contract()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).CreateAsync(Model(balance: ContractBalanceEffect.Restore)));
    }

    [Fact]
    public async Task Create_accepts_effect_when_contract_is_required()
    {
        var db = TestDb.CreateUnitOfWork();

        var created = await Service(db).CreateAsync(
            Model(value: ContractValueEffect.Add, requiresContract: true));

        Assert.True(created.Code > 0);
        Assert.Equal(ContractValueEffect.Add, created.ContractValueEffect);
    }

    [Fact]
    public async Task Update_rejects_removing_the_contract_requirement_of_an_effect()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(
            Model(balance: ContractBalanceEffect.Restore, requiresContract: true));

        var edited = Model(balance: ContractBalanceEffect.Restore);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).UpdateAsync(created.Code, edited));
    }

    [Fact]
    public async Task Delete_is_blocked_when_a_sales_invoice_references_the_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Model());

        // A natureza é referenciada pela LINHA, não pelo cabeçalho.
        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            UsageCode = created.Code,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).DeleteAsync(created.Code));
    }

    [Fact]
    public async Task Delete_removes_an_unused_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Model());

        Assert.True(await Service(db).DeleteAsync(created.Code));
        Assert.Empty(db.Context.Usages);
    }
}
