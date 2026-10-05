using Microsoft.EntityFrameworkCore;
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
/// A tributação da natureza mora em USAGES (só STANDALONE). Criar, ler e alterar precisam
/// levar todos os campos; o tipo decide em quais colunas o CFOP é gravado.
/// </summary>
public class UsageTaxationPersistenceTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Taxed() => new()
    {
        Name = "Venda interestadual",
        Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5102",
        CfopOutgoingOutState = "6102",
        InvoiceOperationText = "Venda de producao",
        DefaultAdditionalInfo = "Documento emitido por ME",
        MovesFiscalInventory = true,
        CreatesFinancialDocument = true,
        IcmsInStateCst = "51",
        IcmsInStateRate = 18m,
        IcmsInStateDeferral = 100m,
        IcmsOutStateCst = "00",
        IcmsOutStateCsosn = "900",
        PisCst = "01",
        PisRate = 1.65m,
        CofinsCst = "01",
        CofinsRate = 7.6m,
        ExcludeIcmsFromPisCofinsBase = true,
        IbsCbsCst = "000",
        IbsCbsClassCode = "000001",
        RequiresQuantity = true,
    };

    [Fact]
    public async Task Create_persists_and_projects_every_taxation_field()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Taxed());

        var read = await Service(db).GetByIdAsync(created.Code);

        Assert.NotNull(read);
        Assert.Equal(UsageDirection.Outgoing, read!.Direction);
        Assert.Equal("Venda de producao", read.InvoiceOperationText);
        Assert.Equal("Documento emitido por ME", read.DefaultAdditionalInfo);
        Assert.True(read.MovesFiscalInventory);
        Assert.True(read.CreatesFinancialDocument);
        Assert.Equal("51", read.IcmsInStateCst);
        Assert.Equal(18m, read.IcmsInStateRate);
        Assert.Equal(100m, read.IcmsInStateDeferral);
        Assert.Equal("00", read.IcmsOutStateCst);
        Assert.Equal("900", read.IcmsOutStateCsosn);
        Assert.Equal("01", read.PisCst);
        Assert.Equal(1.65m, read.PisRate);
        Assert.Equal(7.6m, read.CofinsRate);
        Assert.True(read.ExcludeIcmsFromPisCofinsBase);
        Assert.Equal("000", read.IbsCbsCst);
        Assert.Equal("000001", read.IbsCbsClassCode);
    }

    [Fact]
    public async Task Create_without_direction_defaults_to_outgoing()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = Taxed();
        model.Direction = null;

        var created = await Service(db).CreateAsync(model);
        var stored = await db.Context.Usages.SingleAsync(u => u.Code == created.Code);

        Assert.Equal(UsageDirection.Outgoing, stored.Direction);
    }

    [Fact]
    public async Task Incoming_usage_stores_cfops_in_the_incoming_columns()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = new UsageModel
        {
            Name = "Compra de produtor",
            Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1102",
            CfopIncomingOutState = "2102",
            // Lixo de quando o tipo era outro: tem de ser descartado.
            CfopOutgoingInState = "5102",
            RequiresQuantity = true,
        };

        var created = await Service(db).CreateAsync(model);
        var stored = await db.Context.Usages.SingleAsync(u => u.Code == created.Code);

        Assert.Equal("1102", stored.CfopIncomingInState);
        Assert.Equal("2102", stored.CfopIncomingOutState);
        Assert.Null(stored.CfopOutgoingInState);
        Assert.Null(stored.CfopOutgoingOutState);
    }

    [Fact]
    public async Task Blank_codes_are_stored_as_null()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = Taxed();
        model.IcmsOutStateCsosn = "";
        model.IbsCbsCst = " ";
        model.IbsCbsClassCode = "";

        var created = await Service(db).CreateAsync(model);
        var stored = await db.Context.Usages.SingleAsync(u => u.Code == created.Code);

        Assert.Null(stored.IcmsOutStateCsosn);
        Assert.Null(stored.IbsCbsCst);
        Assert.Null(stored.IbsCbsClassCode);
    }

    [Fact]
    public async Task Update_changes_the_taxation_fields()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Taxed());

        var edited = Taxed();
        edited.IcmsInStateRate = 12m;
        edited.PisCst = "09";
        edited.PisRate = null;

        await Service(db).UpdateAsync(created.Code, edited);
        var read = await Service(db).GetByIdAsync(created.Code);

        Assert.Equal(12m, read!.IcmsInStateRate);
        Assert.Equal("09", read.PisCst);
        Assert.Null(read.PisRate);
    }

    [Fact]
    public async Task Direction_cannot_change_once_a_sales_invoice_uses_the_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Taxed());

        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", UsageCode = created.Code,
        });
        await db.SaveChangesAsync();

        var edited = Taxed();
        edited.Direction = UsageDirection.Incoming;
        edited.CfopIncomingInState = "1102";
        edited.CfopOutgoingInState = null;
        edited.CfopOutgoingOutState = null;
        // CST de PIS/COFINS de ENTRADA: senão a validação de coerência barra antes da regra de uso.
        edited.PisCst = "50";
        edited.CofinsCst = "50";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UpdateAsync(created.Code, edited));
        Assert.Contains("já foi utilizada", ex.Message);
    }

    [Fact]
    public async Task Incoming_usage_cannot_be_the_shipment_billing_default()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = new UsageModel
        {
            Name = "Compra", Direction = UsageDirection.Incoming, IsDefault = true, RequiresQuantity = true,
        };

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(model));
        Assert.Contains("entrada", ex.Message);
    }
}
