using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>Cadastros de ICMS por UF e por produto, resolução da alíquota, destinação da natureza e regime de PIS/COFINS da filial
/// (spec 2026-10-17 §1).</summary>
public class IcmsStateSettingsTests
{
    private static async Task<UnitOfWork> SeedAsync(decimal? itemRate = null, decimal? itemFcp = null)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.IcmsStateSettings.Add(new IcmsStateSetting
            { StateCode = "SP", InternalRate = 18, FcpRate = 2, DifalBaseRule = DifalBaseRule.Double });

        if (itemRate is not null)
            db.Context.IcmsItemStateRates.Add(new IcmsItemStateRate
                { Key = Guid.NewGuid(), ItemCode = "P1", StateCode = "SP", InternalRate = itemRate.Value, FcpRate = itemFcp });

        await db.Context.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task Item_rate_overrides_state_rate()
    {
        var db = await SeedAsync(12, 1);

        var r = await IcmsInternalRateResolver.ResolveAsync(db.Context, "P1", "SP");

        Assert.Equal((12m, 1m, DifalBaseRule.Double), r);
    }

    [Fact]
    public async Task Item_null_fcp_inherits_state_fcp()
    {
        var db = await SeedAsync(12, null);

        var r = await IcmsInternalRateResolver.ResolveAsync(db.Context, "P1", "SP");

        Assert.Equal((12m, 2m, DifalBaseRule.Double), r);
    }

    [Fact]
    public async Task Item_without_rate_uses_state_rate()
    {
        var db = await SeedAsync();

        var r = await IcmsInternalRateResolver.ResolveAsync(db.Context, "OTHER", "sp");

        Assert.Equal((18m, 2m, DifalBaseRule.Double), r);
    }

    [Fact]
    public async Task No_state_setting_returns_null()
    {
        var db = await SeedAsync(12, 1);

        Assert.Null(await IcmsInternalRateResolver.ResolveAsync(db.Context, "P1", "MG"));
    }

    [Fact]
    public async Task State_setting_validation()
    {
        var service = new IcmsStateSettingsService(TestDb.CreateUnitOfWork());

        static IcmsStateSetting S(string uf, decimal rate, decimal fcp) =>
            new() { StateCode = uf, InternalRate = rate, FcpRate = fcp };

        Assert.Equal("Informe a UF com a sigla de 2 letras (ex.: SP).",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("SPA", 18, 0)))).Message);
        Assert.Equal("Informe a UF com a sigla de 2 letras (ex.: SP).",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("35", 18, 0)))).Message);
        Assert.Equal("A alíquota interna deve estar entre 0 e 100.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("MG", 101, 0)))).Message);
        Assert.Equal("A alíquota interna deve estar entre 0 e 100.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("MG", -1, 0)))).Message);
        Assert.Equal("O FCP deve estar entre 0 e 100.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("MG", 18, 100.5m)))).Message);
        Assert.Equal("A soma da alíquota interna com o FCP deve ser menor que 100.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("MG", 98, 2)))).Message);

        var saved = await service.CreateAsync(S("mg", 18, 2));
        Assert.Equal("MG", saved.StateCode);

        Assert.StartsWith("Já existe",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(S("MG", 18, 0)))).Message);
    }

    [Fact]
    public async Task Item_rate_validation_and_uniqueness()
    {
        var service = new IcmsItemStateRatesService(TestDb.CreateUnitOfWork());

        static IcmsItemStateRate R(string item, string uf, decimal rate, decimal? fcp = null) =>
            new() { ItemCode = item, StateCode = uf, InternalRate = rate, FcpRate = fcp };

        Assert.Equal("Informe o produto.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(R(" ", "SP", 12)))).Message);
        Assert.Equal("A alíquota interna deve estar entre 0 e 100.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(R("P1", "SP", 120)))).Message);
        Assert.Equal("A soma da alíquota interna com o FCP deve ser menor que 100.",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(R("P1", "SP", 99, 1)))).Message);

        var saved = await service.CreateAsync(R("P1", "sp", 12));
        Assert.NotEqual(Guid.Empty, saved.Key);
        Assert.Equal("SP", saved.StateCode);

        Assert.StartsWith("Já existe",
            (await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(R("P1", "SP", 7)))).Message);
    }

    [Fact]
    public async Task Usage_purchase_purpose_roundtrip()
    {
        var name = Guid.NewGuid().ToString();
        var created = await new UsageService(TestDb.CreateUnitOfWork(name), NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Entrada uso e consumo", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1556", CfopIncomingOutState = "2556", PisCst = "98", CofinsCst = "98",
            RequiresQuantity = true, PurchasePurpose = PurchasePurpose.UseAndConsumption,
        });

        var read = await new UsageService(TestDb.CreateUnitOfWork(name), NullLogger<UsageService>.Instance).GetByIdAsync(created.Code);

        Assert.Equal(PurchasePurpose.UseAndConsumption, read!.PurchasePurpose);
    }

    [Fact]
    public async Task Branch_pis_cofins_regime_roundtrip()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var service = new BranchService(db.Context, TaxTestServices.Config("STANDALONE"));

        var created = await service.CreateAsync(new Branch
        {
            Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101",
            PisCofinsRegime = PisCofinsRegime.NonCumulative,
        });

        var read = await new BranchService(TestDb.CreateUnitOfWork(name).Context, TaxTestServices.Config("STANDALONE"))
            .GetByIdAsync(created.Code!);

        Assert.Equal(PisCofinsRegime.NonCumulative, read!.PisCofinsRegime);
        Assert.Equal(PisCofinsRegime.Cumulative, new Branch { BranchName = "X" }.PisCofinsRegime);
    }

    [Fact]
    public async Task Item_rate_is_validated_against_inherited_state_fcp()
    {
        var db = await SeedAsync();
        var service = new IcmsItemStateRatesService(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(new IcmsItemStateRate { ItemCode = "P9", StateCode = "SP", InternalRate = 99 }));
        Assert.Equal("A soma da alíquota interna com o FCP deve ser menor que 100.", e.Message);

        // Com FCP próprio informado, vale o do produto.
        var ok = await service.CreateAsync(new IcmsItemStateRate { ItemCode = "P9", StateCode = "SP", InternalRate = 99, FcpRate = 0 });
        Assert.Equal(99m, ok.InternalRate);
    }

    [Fact]
    public async Task State_fcp_change_is_refused_when_an_inheriting_item_would_reach_100()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IcmsStateSettingsService(db);
        await service.CreateAsync(new IcmsStateSetting { StateCode = "MG", InternalRate = 18, FcpRate = 0 });
        db.Context.IcmsItemStateRates.Add(new IcmsItemStateRate
            { Key = Guid.NewGuid(), ItemCode = "P7", StateCode = "MG", InternalRate = 99, FcpRate = null });
        await db.Context.SaveChangesAsync();

        var state = (await service.GetByIdAsync("MG"))!;
        state.FcpRate = 2;

        var e = await Assert.ThrowsAsync<DefaultException>(() => service.UpdateAsync(state));
        Assert.Contains("P7", e.Message);
    }

    [Fact]
    public async Task Invalid_enums_are_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var branch = new BranchService(db.Context, TaxTestServices.Config("STANDALONE"));
        var be = await Assert.ThrowsAsync<DefaultException>(() => branch.CreateAsync(new Branch
            { Code = "02", BranchName = "X", ShortName = "X", TaxId = "68583898000101", PisCofinsRegime = (PisCofinsRegime)9 }));
        Assert.Equal("Regime de PIS/COFINS inválido.", be.Message);

        var usage = new UsageService(db, NullLogger<UsageService>.Instance);
        var ue = await Assert.ThrowsAsync<DefaultException>(() => usage.CreateAsync(new UsageModel
        {
            Name = "N", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
            PisCst = "98", CofinsCst = "98", RequiresQuantity = true, PurchasePurpose = (PurchasePurpose)9,
        }));
        Assert.Equal("Destinação da natureza inválida.", ue.Message);
    }
}
