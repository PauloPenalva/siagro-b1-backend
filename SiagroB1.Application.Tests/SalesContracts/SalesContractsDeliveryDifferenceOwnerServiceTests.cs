using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Regra de designação do dono da diferença de entrega: exatamente UMA linha por item de
/// nota carrega a quebra inteira. Três passos, idempotentes — mantém o dono atual se o
/// contrato dele ainda tem volume líquido; senão elege a linha mais antiga entre as que
/// estão em contrato com líquido positivo; e, se nenhum contrato tem líquido positivo,
/// mantém a linha mais antiga do item.
/// </summary>
public class SalesContractsDeliveryDifferenceOwnerServiceTests
{
    private static readonly Guid ItemKey = Guid.NewGuid();

    private static SalesContractAllocation Line(
        Guid contractKey, decimal volume, int rowId, bool owner = false,
        SalesContractAllocationOrigin origin = SalesContractAllocationOrigin.Billing) => new()
    {
        Key = Guid.NewGuid(),
        RowId = rowId,
        SalesContractKey = contractKey,
        SalesInvoiceItemKey = ItemKey,
        Volume = volume,
        Origin = origin,
        OwnsDeliveryDifference = owner,
    };

    private static SalesContractAllocation OwnerOf(IEnumerable<SalesContractAllocation> lines) =>
        lines.Single(l => l.OwnsDeliveryDifference);

    [Fact]
    public void EnsureOwner_NoOwnerYet_MarksOldestLine()
    {
        // Caminho do faturamento: a única linha do item nasce dona.
        var a = Guid.NewGuid();
        var billing = Line(a, 100m, rowId: 1);
        var lines = new[] { billing };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(billing, OwnerOf(lines));
    }

    [Fact]
    public void EnsureOwner_OwnerContractStillHasVolume_KeepsOwner()
    {
        // Realocação parcial: A cede 40 de 100 e continua com 60 → o dono não se mexe.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var billing = Line(a, 100m, rowId: 1, owner: true);
        var lines = new[]
        {
            billing,
            Line(a, -40m, rowId: 2, origin: SalesContractAllocationOrigin.Reallocation),
            Line(b, 40m, rowId: 3, origin: SalesContractAllocationOrigin.Reallocation),
        };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(billing, OwnerOf(lines));
    }

    [Fact]
    public void EnsureOwner_OwnerContractEmptied_MovesOwnershipToTheVolume()
    {
        // Troca cruzada: A cede as 100 inteiras e fica com líquido 0 → a titularidade
        // acompanha o volume para B, em vez de deixar A consumindo quebra pura.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var destination = Line(b, 100m, rowId: 3, origin: SalesContractAllocationOrigin.Reallocation);
        var lines = new[]
        {
            Line(a, 100m, rowId: 1, owner: true),
            Line(a, -100m, rowId: 2, origin: SalesContractAllocationOrigin.Reallocation),
            destination,
        };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(destination, OwnerOf(lines));
    }

    [Fact]
    public void EnsureOwner_ReversalRemovedTheOwnerLine_FallsBackToBilling()
    {
        // Estorno da realocação: as linhas do par sumiram e sobrou só a do faturamento,
        // que estava sem a flag porque o dono era a linha apagada. Reelege sem bookkeeping.
        var a = Guid.NewGuid();
        var billing = Line(a, 100m, rowId: 1);
        var lines = new[] { billing };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(billing, OwnerOf(lines));
    }

    [Fact]
    public void EnsureOwner_NoContractWithPositiveNet_KeepsOldestLine()
    {
        // Item integralmente devolvido: ninguém tem líquido positivo. Regra 3 evita item
        // sem dono (que faria a quebra desaparecer do recálculo).
        var a = Guid.NewGuid();
        var billing = Line(a, 100m, rowId: 1);
        var lines = new[]
        {
            billing,
            Line(a, -100m, rowId: 2, origin: SalesContractAllocationOrigin.Return),
        };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(billing, OwnerOf(lines));
    }

    [Fact]
    public void EnsureOwner_MoreThanOneOwner_LeavesExactlyOne()
    {
        // Auto-correção: estado inconsistente converge para um dono só.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lines = new[]
        {
            Line(a, 60m, rowId: 1, owner: true),
            Line(b, 40m, rowId: 2, owner: true),
        };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Single(lines, l => l.OwnsDeliveryDifference);
    }

    [Fact]
    public void EnsureOwner_PendingLine_CountsAsNewestNotOldest()
    {
        // Linha ainda não persistida tem RowId 0. Ordenar cru a tornaria a "mais antiga" e
        // roubaria a titularidade da linha do faturamento.
        var a = Guid.NewGuid();
        var billing = Line(a, 100m, rowId: 7, owner: true);
        var lines = new[]
        {
            billing,
            Line(a, -30m, rowId: 0, origin: SalesContractAllocationOrigin.Reallocation),
            Line(Guid.NewGuid(), 30m, rowId: 0, origin: SalesContractAllocationOrigin.Reallocation),
        };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(billing, OwnerOf(lines));
    }

    [Fact]
    public void EnsureOwner_IsIdempotent()
    {
        // Roda em todo hook do ledger: aplicar duas vezes não pode mudar o resultado.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lines = new[]
        {
            Line(a, 100m, rowId: 1, owner: true),
            Line(a, -100m, rowId: 2, origin: SalesContractAllocationOrigin.Reallocation),
            Line(b, 100m, rowId: 3, origin: SalesContractAllocationOrigin.Reallocation),
        };

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);
        var first = OwnerOf(lines);
        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(lines);

        Assert.Same(first, OwnerOf(lines));
    }
}
