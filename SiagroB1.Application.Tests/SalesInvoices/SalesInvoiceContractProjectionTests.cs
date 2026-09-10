using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Derivadas do contrato de venda no cabeçalho do documento de saída (GAC-1170). O grid
/// "Documentos de Saída" do detalhe da carga binda escalares, e o contrato mora na LINHA —
/// estas propriedades fazem a ponte. São puras: dependem só de <c>Items</c> estar carregado.
/// </summary>
public class SalesInvoiceContractProjectionTests
{
    private static SalesContract Contract(string code, string complement, decimal freight) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        Complement = complement,
        FreightCostStandard = freight,
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "2026",
    };

    private static SalesInvoice InvoiceWith(params SalesContract?[] contracts)
    {
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C0001" };

        foreach (var contract in contracts)
        {
            invoice.AddItem(new SalesInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                Quantity = 10_000m,
                SalesContractKey = contract?.Key,
                SalesContract = contract,
            });
        }

        return invoice;
    }

    /// <summary>Caso da nota de carga: uma linha, um contrato.</summary>
    [Fact]
    public void A_single_contract_is_reported_as_is()
    {
        var invoice = InvoiceWith(Contract("CV000123", "COMPL A", 50m));

        Assert.Equal("CV000123", invoice.SalesContractCode);
        Assert.Equal("COMPL A", invoice.SalesContractComplement);
        Assert.Equal(50m, invoice.SalesContractFreightCostStandard);
    }

    /// <summary>
    /// Duas linhas do MESMO contrato não viram "CV1 / CV1": a distinção é por contrato, não
    /// por linha, e o frete continua tendo um valor único.
    /// </summary>
    [Fact]
    public void Two_lines_of_the_same_contract_collapse_into_one()
    {
        var contract = Contract("CV000123", "COMPL A", 50m);
        var invoice = InvoiceWith(contract, contract);

        Assert.Equal("CV000123", invoice.SalesContractCode);
        Assert.Equal(50m, invoice.SalesContractFreightCostStandard);
    }

    /// <summary>
    /// Documento avulso com contratos diferentes: códigos e complementos são unidos, mas o
    /// frete standard fica NULO — escolher um dos dois seria informação errada na tela.
    /// </summary>
    [Fact]
    public void Different_contracts_are_joined_and_the_freight_is_null()
    {
        var invoice = InvoiceWith(
            Contract("CV000123", "COMPL A", 50m),
            Contract("CV000456", "COMPL B", 80m));

        Assert.Equal("CV000123 / CV000456", invoice.SalesContractCode);
        Assert.Equal("COMPL A / COMPL B", invoice.SalesContractComplement);
        Assert.Null(invoice.SalesContractFreightCostStandard);
    }

    /// <summary>
    /// Sem contrato na linha — ou com Items não carregado, que é o caso de toda consulta sem
    /// o Include — as três voltam nulas em vez de estourar.
    /// </summary>
    [Fact]
    public void No_contract_yields_nulls()
    {
        var withoutContract = InvoiceWith((SalesContract?)null);
        var withoutItems = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C0001" };

        Assert.Null(withoutContract.SalesContractCode);
        Assert.Null(withoutContract.SalesContractComplement);
        Assert.Null(withoutContract.SalesContractFreightCostStandard);

        Assert.Null(withoutItems.SalesContractCode);
        Assert.Null(withoutItems.SalesContractFreightCostStandard);
    }

    /// <summary>Complemento em branco não polui a junção nem vira " / ".</summary>
    [Fact]
    public void Blank_complements_are_skipped()
    {
        var invoice = InvoiceWith(
            Contract("CV000123", string.Empty, 50m),
            Contract("CV000456", "COMPL B", 50m));

        Assert.Equal("COMPL B", invoice.SalesContractComplement);
    }
}
