using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Exclusão do contrato de venda em rascunho. As tabelas filhas apontam para o contrato SEM cascade (NoAction): o
/// contrato "Fixo" nasce com uma fixação automática, e a exclusão dava 500 no SQL Server (FK) — o InMemory não acusa,
/// então os testes conferem que os filhos SOMEM junto, e não apenas que não há exceção.
/// </summary>
public class SalesContractsDeleteServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsDeleteService Service() =>
        new(_db.Context, NullLogger<SalesContractsDeleteService>.Instance);

    private async Task<SalesContract> SeedDraftWithChildrenAsync(ContractDraftStatus minutaStatus = ContractDraftStatus.Draft)
    {
        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m, status: ContractStatus.Draft);
        _db.Context.SalesContracts.Add(contract);

        _db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation { SalesContractKey = contract.Key });
        _db.Context.SalesContractsDeliveryLocations.Add(new SalesContractDeliveryLocation
        {
            Key = Guid.NewGuid(), SalesContractKey = contract.Key, CardCode = "C0001",
        });
        _db.Context.SalesContractsComments.Add(new SalesContractComment
        {
            Key = Guid.NewGuid(), SalesContractKey = contract.Key, CommentText = "rascunho",
        });
        _db.Context.SalesContractsChangeLogs.Add(new SalesContractChangeLog
        {
            Key = Guid.NewGuid(), SalesContractKey = contract.Key, Field = "Price",
        });
        _db.Context.SalesContractAttachments.Add(new SalesContractAttachment
        {
            Key = Guid.NewGuid(), SalesContractKey = contract.Key, Description = "anexo", FileName = "a.pdf",
            FileData = [1, 2, 3], ContentType = "application/pdf",
        });

        var minuta = new ContractDraft
        {
            Key = Guid.NewGuid(), SalesContractKey = contract.Key, ContractCode = contract.Code!, Description = "minuta",
            BodyHtml = "<p/>", PlaceholdersJson = "{}", Status = minutaStatus,
        };
        _db.Context.ContractDrafts.Add(minuta);
        _db.Context.ContractDraftSigners.Add(new ContractDraftSigner
        {
            Key = Guid.NewGuid(), DraftKey = minuta.Key, Name = "Fulano", TaxId = "00000000000", Email = "f@x.com",
        });

        _db.Context.SalesContractFiscalComplements.Add(new SalesContractFiscalComplement { SalesContractKey = contract.Key, UsageCode = 3 });

        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
        return contract;
    }

    [Fact]
    public async Task Draft_contract_is_deleted_together_with_its_children()
    {
        var contract = await SeedDraftWithChildrenAsync();

        Assert.True(await Service().ExecuteAsync(contract.Key));

        var ctx = _db.Context;
        Assert.False(await ctx.SalesContracts.AnyAsync(x => x.Key == contract.Key));
        Assert.False(await ctx.SalesContractsPriceFixations.AnyAsync(x => x.SalesContractKey == contract.Key));
        Assert.False(await ctx.SalesContractsDeliveryLocations.AnyAsync(x => x.SalesContractKey == contract.Key));
        Assert.False(await ctx.SalesContractsComments.AnyAsync(x => x.SalesContractKey == contract.Key));
        Assert.False(await ctx.SalesContractsChangeLogs.AnyAsync(x => x.SalesContractKey == contract.Key));
        Assert.False(await ctx.SalesContractAttachments.AnyAsync(x => x.SalesContractKey == contract.Key));
        Assert.False(await ctx.ContractDrafts.AnyAsync(x => x.SalesContractKey == contract.Key));
        Assert.False(await ctx.ContractDraftSigners.AnyAsync());
        Assert.False(await ctx.SalesContractFiscalComplements.AnyAsync(x => x.SalesContractKey == contract.Key));
    }

    /// <summary>Minuta já enviada (ou assinada) tem envelope na assinatura eletrônica: excluir deixaria órfão.</summary>
    [Theory]
    [InlineData(ContractDraftStatus.AwaitingSignature)]
    [InlineData(ContractDraftStatus.PartiallySigned)]
    [InlineData(ContractDraftStatus.Signed)]
    public async Task Contract_with_a_minuta_sent_for_signature_is_refused(ContractDraftStatus status)
    {
        var contract = await SeedDraftWithChildrenAsync(status);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(contract.Key));

        Assert.Equal("O contrato tem minuta enviada para assinatura: cancele a minuta antes de excluir o contrato.", ex.Message);
        Assert.True(await _db.Context.SalesContracts.AnyAsync(x => x.Key == contract.Key));
    }

    [Fact]
    public async Task Contract_with_a_canceled_minuta_is_deleted()
    {
        var contract = await SeedDraftWithChildrenAsync(ContractDraftStatus.Canceled);

        Assert.True(await Service().ExecuteAsync(contract.Key));
        Assert.False(await _db.Context.ContractDrafts.AnyAsync(x => x.SalesContractKey == contract.Key));
    }
}
