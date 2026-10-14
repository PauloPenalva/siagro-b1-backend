using System.Reflection;
using System.Text.RegularExpressions;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A aba "Entregas" do contrato de compra travava (timeout de 30 s) atrás de qualquer escrita em andamento:
/// o banco roda READ COMMITTED com RCSI desligado e só uma tabela tinha NOLOCK. Estes testes garantem que
/// TODA tabela referenciada (FROM/JOIN, inclusive nas subconsultas do OUTER APPLY) leva <c>WITH (NOLOCK)</c>.
/// As constantes são privadas e o projeto não tem InternalsVisibleTo, então são lidas por reflexão.
/// </summary>
public class PurchaseContractsGetAllocationsByContractSqlTests
{
    private static readonly Regex TableReference = new(
        @"\b(FROM|JOIN)\s+(\w+)(?:\s+(?!WITH\b|ON\b|LEFT\b|JOIN\b|WHERE\b|OUTER\b)(\w+))?\s*(WITH\s*\(\s*NOLOCK\s*\))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ReadSql(string field) =>
        (string)typeof(PurchaseContractsGetAllocationsByContractService)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    // Remove comentários de linha (-- ...) para não confundir "FROM"/"JOIN" citados em texto.
    private static string StripComments(string sql) => Regex.Replace(sql, @"--[^\r\n]*", "");

    [Theory]
    [InlineData("SQL")]
    [InlineData("LinksSql")]
    public void Every_table_reference_has_nolock_hint(string field)
    {
        var sql = StripComments(ReadSql(field));
        var matches = TableReference.Matches(sql);

        Assert.NotEmpty(matches);
        var missing = matches.Where(m => !m.Groups[4].Success).Select(m => m.Groups[2].Value + " " + m.Groups[3].Value).ToList();
        Assert.True(missing.Count == 0, $"{field}: tabelas sem WITH (NOLOCK): {string.Join(", ", missing)}");
    }

    [Fact]
    public void Main_query_references_all_expected_tables()
    {
        var sql = StripComments(ReadSql("SQL"));
        // 1 FROM + 12 LEFT JOIN + 3 do 1o OUTER APPLY (PFR, PII, PI) + PA2 = 17 referências.
        Assert.Equal(17, TableReference.Matches(sql).Count);
    }
}
