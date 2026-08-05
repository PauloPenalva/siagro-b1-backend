using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Resolução do CFOP: compara a UF da filial do documento com a UF do destinatário.
/// Nenhum caminho pode gravar CFOP vazio em silêncio — ausência de UF ou de CFOP
/// cadastrado é erro de negócio.
/// </summary>
public class SalesInvoicesCfopResolveServiceTests
{
    private const string InState = "5102";
    private const string OutState = "6102";

    private static async Task<int> SeedUsageAsync(
        UnitOfWork db, string? inState = InState, string? outState = OutState)
    {
        var service = new UsageService(db, NullLogger<UsageService>.Instance);

        var created = await service.CreateAsync(new UsageModel
        {
            Name = "Venda de grãos",
            CfopOutgoingInState = inState,
            CfopOutgoingOutState = outState,
        });

        return created.Code;
    }

    private static async Task SeedBranchAsync(UnitOfWork db, string code, string? stateCode)
    {
        db.Context.Branchs.Add(new Branch
        {
            Code = code,
            BranchName = $"Filial {code}",
            StateCode = stateCode,
        });

        await db.SaveChangesAsync();
    }

    private static SalesInvoicesCfopResolveService Service(
        UnitOfWork db, string partnerState = "MT") =>
        new(db,
            new UsageService(db, NullLogger<UsageService>.Instance),
            new FakeBusinessPartnerService(
                names: new() { ["C0001"] = "Cliente" },
                states: string.IsNullOrEmpty(partnerState)
                    ? new()
                    : new() { ["C0001"] = partnerState }));

    [Fact]
    public async Task Same_state_uses_the_in_state_cfop()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);
        await SeedBranchAsync(db, "01", "MT");

        var cfop = await Service(db, partnerState: "MT").ResolveAsync(usageCode, "01", "C0001");

        Assert.Equal(InState, cfop);
    }

    [Fact]
    public async Task Different_state_uses_the_out_of_state_cfop()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);
        await SeedBranchAsync(db, "01", "MT");

        var cfop = await Service(db, partnerState: "GO").ResolveAsync(usageCode, "01", "C0001");

        Assert.Equal(OutState, cfop);
    }

    [Fact]
    public async Task Missing_branch_state_is_a_business_error()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);
        await SeedBranchAsync(db, "01", null);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ResolveAsync(usageCode, "01", "C0001"));
    }

    [Fact]
    public async Task Missing_partner_state_is_a_business_error()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);
        await SeedBranchAsync(db, "01", "MT");

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, partnerState: "").ResolveAsync(usageCode, "01", "C0001"));
    }

    [Fact]
    public async Task Missing_cfop_on_usage_is_a_business_error()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, outState: null);
        await SeedBranchAsync(db, "01", "MT");

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, partnerState: "GO").ResolveAsync(usageCode, "01", "C0001"));
    }

    /// <summary>
    /// Em STANDALONE o parceiro devolve TODOS os endereços, e o de entrega pode estar em
    /// outra UF. Confiar na ordem da coleção faria o mesmo parceiro gerar CFOP diferente
    /// conforme o modo — o de faturamento é quem manda.
    /// </summary>
    [Fact]
    public async Task Billing_address_wins_over_other_addresses()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);
        await SeedBranchAsync(db, "01", "MT");

        var service = new SalesInvoicesCfopResolveService(
            db,
            new UsageService(db, NullLogger<UsageService>.Instance),
            new FakeBusinessPartnerService(
                names: new() { ["C0001"] = "Cliente" },
                addresses: new()
                {
                    ["C0001"] =
                    [
                        // Entrega em GO vem primeiro na coleção, de propósito.
                        new AddressModel { AddressName = "ENTREGA", AdresType = "S", State = "GO" },
                        new AddressModel { AddressName = "FATURAMENTO", AdresType = "B", State = "MT" },
                    ]
                }));

        Assert.Equal(InState, await service.ResolveAsync(usageCode, "01", "C0001"));
    }

    [Fact]
    public async Task Address_without_state_is_ignored_in_favour_of_one_that_has_it()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);
        await SeedBranchAsync(db, "01", "MT");

        var service = new SalesInvoicesCfopResolveService(
            db,
            new UsageService(db, NullLogger<UsageService>.Instance),
            new FakeBusinessPartnerService(
                names: new() { ["C0001"] = "Cliente" },
                addresses: new()
                {
                    ["C0001"] =
                    [
                        new AddressModel { AddressName = "FATURAMENTO", AdresType = "B", State = null },
                        new AddressModel { AddressName = "ENTREGA", AdresType = "S", State = "GO" },
                    ]
                }));

        Assert.Equal(OutState, await service.ResolveAsync(usageCode, "01", "C0001"));
    }

    [Fact]
    public async Task Missing_branch_is_a_business_error()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ResolveAsync(usageCode, "99", "C0001"));
    }
}
