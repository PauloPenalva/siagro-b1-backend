using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ContractTemplates;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Shared;

/// <summary>
/// Regressão: atualizar QUALQUER entidade pelo CRUD genérico devolvia 500.
///
/// <c>BaseService.UpdateAsync</c> fazia <c>Entry(entity).State = Modified</c>, que marca TODAS as
/// colunas como alteradas — inclusive <c>RowId</c>, que é <c>Identity</c> em <c>BaseEntity</c> e
/// portanto existe em toda entidade do sistema. O SQL Server recusa com
/// <c>"Cannot update identity column 'RowId'."</c>, medido contra a base da Yokotobi.
///
/// O provider InMemory não aplica essa regra, então o teste não é do SaveChanges: é do
/// change tracker, que é onde o defeito nasce e onde a correção age.
/// </summary>
public class BaseServiceUpdateTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractTemplateService Service() => new(_db.Context, NullLogger<ContractTemplateService>.Instance);

    private static ContractTemplate Template() => new()
    {
        Name = "Modelo", Title = "Contrato", ContractType = ContractTemplateScope.Purchase,
        BodyHtml = "<p>{{numero}}</p>",
    };

    [Fact]
    public async Task Update_does_not_mark_store_generated_columns_as_modified()
    {
        var created = await Service().CreateAsync(Template());
        created.BodyHtml = "<p>{{numero}} alterado</p>";

        await Service().UpdateAsync(created.Key, created);

        // Depois do SaveChanges o tracker normaliza tudo para Unchanged, então a asserção é
        // feita sobre uma nova marcação — a mesma que o UpdateAsync aplica.
        var entry = _db.Context.Entry(created);
        BaseServiceMarker.MarkForUpdate(entry);

        var geradas = entry.Properties
            .Where(p => p.Metadata.ValueGenerated != ValueGenerated.Never)
            .ToList();

        Assert.NotEmpty(geradas);
        Assert.All(geradas, p => Assert.False(p.IsModified, $"{p.Metadata.Name} não pode entrar no UPDATE"));
    }

    [Fact]
    public async Task Update_still_persists_the_changed_columns()
    {
        var created = await Service().CreateAsync(Template());
        created.BodyHtml = "<p>{{numero}} alterado</p>";
        created.Title = "Outro título";

        await Service().UpdateAsync(created.Key, created);

        var recarregado = await _db.Context.ContractTemplates.AsNoTracking()
            .SingleAsync(x => x.Key == created.Key);

        Assert.Equal("<p>{{numero}} alterado</p>", recarregado.BodyHtml);
        Assert.Equal("Outro título", recarregado.Title);
    }

    [Fact]
    public async Task Update_marks_the_ordinary_columns_as_modified()
    {
        var created = await Service().CreateAsync(Template());
        created.Title = "Outro título";

        var entry = _db.Context.Entry(created);
        BaseServiceMarker.MarkForUpdate(entry);

        Assert.True(entry.Property(nameof(ContractTemplate.Title)).IsModified);
        Assert.True(entry.Property(nameof(ContractTemplate.BodyHtml)).IsModified);
    }
}
