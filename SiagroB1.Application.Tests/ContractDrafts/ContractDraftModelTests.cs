using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>O modelo EF das minutas: FKs sem cascata, índice único filtrado e check constraint.</summary>
public class ContractDraftModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public void Draft_foreign_keys_do_not_cascade()
    {
        var entity = _db.Context.Model.FindEntityType(typeof(ContractDraft))!;

        foreach (var fk in entity.GetForeignKeys())
            Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
    }

    [Fact]
    public void External_document_id_is_unique_while_present()
    {
        var index = _db.Context.Model.FindEntityType(typeof(ContractDraft))!
            .GetIndexes().Single(i => i.Properties.Count == 1
                && i.Properties[0].Name == nameof(ContractDraft.ExternalDocumentId));

        Assert.True(index.IsUnique);
        Assert.Equal("[ExternalDocumentId] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void Exactly_one_contract_is_enforced_by_check_constraint()
    {
        // Check constraint só existe no modelo de design; o de runtime (Context.Model) não guarda
        // essa configuração — mesma armadilha de StorageAddressesGetServiceTests.IncludeProperties.
        var entity = _db.Context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ContractDraft))!;

        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "CK_CONTRACT_DRAFTS_ONE_CONTRACT");
    }

    [Fact]
    public void Signatory_emails_are_unique()
    {
        var company = _db.Context.Model.FindEntityType(typeof(CompanySignatory))!;
        var partner = _db.Context.Model.FindEntityType(typeof(BusinessPartnerSignatory))!;

        Assert.Contains(company.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "Email");
        Assert.Contains(partner.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(["CardCode", "Email"]));
    }
}
