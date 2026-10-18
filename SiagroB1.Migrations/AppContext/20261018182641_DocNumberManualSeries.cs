using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Séries de numeração no estilo SAP B1: cada tipo de documento passa a ter uma série
    /// "Automático" (padrão, contador) e uma "Manual" (código digitado), exceto Romaneio (5) e
    /// Financeiro (12), que não admitem numeração manual. Também cria a série Automático de
    /// Cliente (16), Fornecedor (17) e Produto (18), seguindo o maior código numérico já usado.
    /// Só dados: o modelo não muda. Roda nas bases de produção, mantidas à mão — por isso cada
    /// passo é idempotente e tolera duplicidades, padrão inativo e tipos sem série; o que não
    /// dá para resolver sem decidir pelo usuário só é avisado com PRINT.
    /// </summary>
    public partial class DocNumberManualSeries : Migration
    {
        // Tipos tratados (os já existentes + Cliente/Fornecedor/Produto). 3, 6 e 15 não usam DOC_NUMBERS por aqui.
        private const string AllTypes = "1,2,4,5,7,8,9,10,11,12,13,14,16,17,18";

        // Tipos que admitem a série Manual (todos menos Romaneio 5 e Financeiro 12).
        private const string ManualTypes = "1,2,4,7,8,9,10,11,13,14,16,17,18";

        private const string ManualTypeRows = "(1),(2),(4),(7),(8),(9),(10),(11),(13),(14),(16),(17),(18)";

        private const string LegacyTypeRows = "(1),(2),(4),(5),(7),(8),(9),(10),(11),(12),(13),(14)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Passo 0 — flag IsManual legado. Antes desta versão o IsManual não tinha efeito: a série
            // padrão numerava como automática, qualquer que fosse o nome ou a flag, e assim deve
            // continuar (inclusive uma padrão chamada "Manual": vira IsManual = 0, é renomeada para
            // Automático no passo 4 e ganha uma Manual de verdade no passo 5 — sem perder o contador).
            // Romaneio e Financeiro não admitem série manual. Uma linha NÃO padrão chamada "Manual"
            // vira a série manual do tipo (só pode haver uma por tipo); a manual nunca é padrão — o
            // UPDATE ... WHERE [Default] = 1 do GetDocNumberByObjectCodeAsync não pode cair nela.
            migrationBuilder.Sql($"""
                UPDATE DOC_NUMBERS SET IsManual = 0
                WHERE IsManual = 1 AND TransactionCode IN (5, 12);

                UPDATE DOC_NUMBERS SET IsManual = 0
                WHERE IsManual = 1 AND [Default] = 1
                  AND TransactionCode IN ({AllTypes});

                UPDATE d SET IsManual = 1, [Default] = 0
                FROM DOC_NUMBERS d
                WHERE d.Name = 'Manual' AND d.TransactionCode IN ({ManualTypes})
                  AND d.IsManual = 0 AND d.[Default] = 0
                  AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS o
                                  WHERE o.TransactionCode = d.TransactionCode
                                    AND o.IsManual = 1 AND o.[Key] <> d.[Key]);

                UPDATE DOC_NUMBERS SET [Default] = 0
                WHERE IsManual = 1 AND [Default] = 1;
                """);

            // Passo 1 — mais de uma série padrão no tipo: fica a de maior NextNumber (empate: menor Key).
            migrationBuilder.Sql($"""
                ;WITH Ranked AS (
                    SELECT [Key], [Default],
                           ROW_NUMBER() OVER (PARTITION BY TransactionCode
                                              ORDER BY NextNumber DESC, [Key]) AS Seq
                    FROM DOC_NUMBERS
                    WHERE [Default] = 1 AND TransactionCode IN ({AllTypes})
                )
                UPDATE Ranked SET [Default] = 0 WHERE Seq > 1;
                """);

            // Passo 2 — tipo com séries mas sem padrão: promove a não manual de maior NextNumber,
            // preferindo as ativas (uma inativa só é promovida quando é a única automática do tipo,
            // para não recomeçar a contagem). A série padrão nunca fica inativa: o cadastro recusa
            // salvar padrão + inativa, e a série legada precisa continuar editável.
            migrationBuilder.Sql($"""
                ;WITH Candidates AS (
                    SELECT d.[Key], d.[Default],
                           ROW_NUMBER() OVER (PARTITION BY d.TransactionCode
                                              ORDER BY d.Inactive, d.NextNumber DESC, d.[Key]) AS Seq
                    FROM DOC_NUMBERS d
                    WHERE d.TransactionCode IN ({AllTypes}) AND d.IsManual = 0
                      AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS x
                                      WHERE x.TransactionCode = d.TransactionCode AND x.[Default] = 1)
                )
                UPDATE Candidates SET [Default] = 1 WHERE Seq = 1;

                UPDATE DOC_NUMBERS SET Inactive = 0
                WHERE [Default] = 1 AND Inactive = 1 AND TransactionCode IN ({AllTypes});
                """);

            // Passo 3 — tipo de documento sem nenhuma série automática: cria a Automático do zero.
            migrationBuilder.Sql($"""
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                SELECT NEWID(), t.Code, N'Automático', 1, 0, 1, 1, '', '', NULL, 0, 0, '6'
                FROM (VALUES {LegacyTypeRows}) AS t(Code)
                WHERE NOT EXISTS (SELECT 1 FROM DOC_NUMBERS d
                                  WHERE d.TransactionCode = t.Code AND d.IsManual = 0)
                  AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS d
                                  WHERE d.TransactionCode = t.Code AND d.Name = N'Automático');
                """);

            // Passo 6 (antes do 5, para a Manual nunca existir sozinha) — Cliente, Fornecedor e
            // Produto: a contagem segue do maior código no formato da série (prefixo + só dígitos).
            // TRY_CAST porque o SQL Server pode avaliar a conversão antes do filtro (ex.: C12AB).
            migrationBuilder.Sql("""
                DECLARE @customerNext INT = 1 + ISNULL((
                    SELECT MAX(TRY_CAST(SUBSTRING(CardCode, 2, 15) AS INT))
                    FROM BUSINESS_PARTNERS
                    WHERE CardType = 'C' AND CardCode LIKE 'C%'
                      AND LEN(CardCode) BETWEEN 2 AND 10
                      AND SUBSTRING(CardCode, 2, 15) NOT LIKE '%[^0-9]%'), 0);

                DECLARE @supplierNext INT = 1 + ISNULL((
                    SELECT MAX(TRY_CAST(SUBSTRING(CardCode, 2, 15) AS INT))
                    FROM BUSINESS_PARTNERS
                    WHERE CardType = 'S' AND CardCode LIKE 'F%'
                      AND LEN(CardCode) BETWEEN 2 AND 10
                      AND SUBSTRING(CardCode, 2, 15) NOT LIKE '%[^0-9]%'), 0);

                DECLARE @itemNext INT = 1 + ISNULL((
                    SELECT MAX(TRY_CAST(ItemCode AS INT))
                    FROM ITEMS
                    WHERE ItemCode NOT LIKE '%[^0-9]%'
                      AND LEN(ItemCode) BETWEEN 1 AND 9), 0);

                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                SELECT NEWID(), s.Code, N'Automático', 1, s.NextNumber - 1, s.NextNumber, 1,
                       s.Prefix, '', NULL, 0, 0, '6'
                FROM (VALUES (16, 'C', @customerNext),
                             (17, 'F', @supplierNext),
                             (18, '',  @itemNext)) AS s(Code, Prefix, NextNumber)
                WHERE NOT EXISTS (SELECT 1 FROM DOC_NUMBERS d
                                  WHERE d.TransactionCode = s.Code AND d.IsManual = 0)
                  AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS d
                                  WHERE d.TransactionCode = s.Code AND d.Name = N'Automático');
                """);

            // Passo 4 — a série padrão passa a se chamar "Automático", a menos que outra série do
            // tipo já tenha esse nome (índice único TransactionCode + Name): aí só avisa.
            migrationBuilder.Sql($"""
                UPDATE d SET Name = N'Automático'
                FROM DOC_NUMBERS d
                WHERE d.[Default] = 1 AND d.TransactionCode IN ({AllTypes})
                  AND d.Name <> N'Automático'
                  AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS o
                                  WHERE o.TransactionCode = d.TransactionCode
                                    AND o.Name = N'Automático' AND o.[Key] <> d.[Key]);

                DECLARE @notRenamed NVARCHAR(MAX) = N'';
                SELECT @notRenamed = @notRenamed + CAST(d.TransactionCode AS NVARCHAR(10)) + N' '
                FROM DOC_NUMBERS d
                WHERE d.[Default] = 1 AND d.TransactionCode IN ({AllTypes})
                  AND d.Name <> N'Automático';
                IF @notRenamed <> N''
                    PRINT N'DocNumberManualSeries: série padrão não renomeada (já existe outra "Automático") nos tipos: ' + @notRenamed;
                """);

            // Passo 5 — série Manual (contadores zerados, sem tamanho) onde o tipo admite e ainda
            // não tem nenhuma série manual; só ao lado de uma série padrão.
            migrationBuilder.Sql($"""
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                SELECT NEWID(), t.Code, 'Manual', 0, 0, 0, 0, '', '', NULL, 0, 1, NULL
                FROM (VALUES {ManualTypeRows}) AS t(Code)
                WHERE EXISTS (SELECT 1 FROM DOC_NUMBERS d
                              WHERE d.TransactionCode = t.Code AND d.[Default] = 1)
                  AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS d
                                  WHERE d.TransactionCode = t.Code
                                    AND (d.IsManual = 1 OR d.Name = 'Manual'));
                """);

            // Avisos finais: o que ficou fora do padrão e precisa de decisão do usuário no cadastro.
            migrationBuilder.Sql($"""
                DECLARE @skipped NVARCHAR(MAX) = N'';
                SELECT @skipped = @skipped + CAST(d.TransactionCode AS NVARCHAR(10)) + N' '
                FROM DOC_NUMBERS d
                WHERE d.Name = 'Manual' AND d.IsManual = 0 AND d.TransactionCode IN ({AllTypes});
                IF @skipped <> N''
                    PRINT N'DocNumberManualSeries: série "Manual" mantida como automática (padrão não renomeada, tipo sem numeração manual ou com outra série manual) nos tipos: ' + @skipped;

                DECLARE @manyManual NVARCHAR(MAX) = N'';
                SELECT @manyManual = @manyManual + CAST(TransactionCode AS NVARCHAR(10)) + N' '
                FROM DOC_NUMBERS
                WHERE IsManual = 1 AND TransactionCode IN ({AllTypes})
                GROUP BY TransactionCode
                HAVING COUNT(*) > 1;
                IF @manyManual <> N''
                    PRINT N'DocNumberManualSeries: mais de uma série manual nos tipos: ' + @manyManual;

                DECLARE @oddManual NVARCHAR(MAX) = N'';
                SELECT @oddManual = @oddManual + CAST(TransactionCode AS NVARCHAR(10)) + N' '
                FROM DOC_NUMBERS
                WHERE IsManual = 1 AND Name <> 'Manual' AND TransactionCode IN ({AllTypes});
                IF @oddManual <> N''
                    PRINT N'DocNumberManualSeries: série manual com nome diferente de "Manual" nos tipos: ' + @oddManual;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Migration só de dados: renomear de volta, despromover ou apagar séries que já
            // numeraram documentos não é reversível com segurança. Nada a desfazer.
        }
    }
}
