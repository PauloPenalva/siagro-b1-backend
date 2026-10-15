# Títulos firmes a receber do documento de saída — plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use `superpowers:subagent-driven-development` (recomendada) ou `superpowers:executing-plans` para implementar este plano tarefa a tarefa. Os passos usam checkbox (`- [ ]`) para acompanhamento.

**Objetivo:** confirmar um documento de saída cria títulos FIRMES a receber, um por linha × contrato, derivados do ledger de alocações de venda. Eles são compostos no provisório a receber do contrato, que é onde se recebe; sobra por dação. Devolução cria título de devolução de venda com encontro de contas. Estornar, cancelar e realocar acompanham. O encerramento do contrato de venda passa a exigir tudo quitado.

**Arquitetura:**

- Um serviço novo, `FinancialDocumentsSalesFirmSyncService`, compara o **desejado** (linha confirmada que passa em G1–G4, valor da linha distribuído pelas participações do ledger) com os títulos **ativos** da linha. Ele cria, recalcula (solto), cancela (solto) ou recusa (composto ou baixado).
- O serviço é chamado depois de cada mudança de estado ou de ledger da linha:
  - confirmar;
  - Expedir e Faturar;
  - estornar confirmação;
  - cancelar;
  - realocar e desfazer realocação.
- Os serviços do financeiro deixam de checar só "firme de compra". O gate passa a ser "firme de contrato" (compra ou venda), e "mesmo contrato" compara o par `(PurchaseContractKey, SalesContractKey)`.
- O encerramento do contrato de venda ganha as travas da compra, com chave de corte própria.

**Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), ASP.NET Core OData v4, xUnit; OpenUI5 1.141 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-15-sales-firm-receivables-design.md`. É a autoridade: D1–D7, regras G1–G4 e 1–24, mensagens S1–S12. Contexto: `2026-10-13-purchase-firm-payables-design.md` (o mesmo desenho na compra) e `2026-10-14-advance-application-design.md`.

## Restrições globais

### Repositórios, branch e commits

- **Dois repositórios, branch `feature/sales-firm-receivables` nos dois** (os dois já existem):
  - backend: `C:\Projetos\SiagroB1\siagro-b1-backend`, Tasks 1–7. O HEAD tem o spec.
  - frontend: `C:\Projetos\SiagroB1\siagro-b1-frontend`, Tasks 8–9.
- Rode `git branch --show-current` antes de cada `git add`. **Nunca push. Nunca merge.**
- Todo arquivo novo leva `git add <path>` logo depois de criado. O último passo de cada task é **stage**. O **controlador** commita com a mensagem dada.
- Formato da mensagem de commit:
  - assunto `tipo(escopo): descrição pt-BR, imperativo, minúscula, sem ponto`;
  - escopo `financial` (Tasks 1–4, 7), `sales-contract` (Tasks 5–6) ou `financial` no frontend;
  - corpo com o PORQUÊ;
  - trailer `DB: AddSalesFirmReceivables` **só** na Task 1, a única migration;
  - fim com `Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>`, que o script do controlador preenche.

### Convenções e escopo

- Identificadores em inglês. Texto ao usuário em pt-BR. Comentários em pt-BR.
- Os textos de recusa da **compra não mudam**. A venda usa textos próprios onde o da compra cita "conciliação fiscal".

### Mensagens exatas (copiar literalmente)

| # | Texto |
|---|---|
| S1 | `O título {firme} do documento de saída {InvoiceNumber} já está composto no título {provisório}: desfaça a composição antes de cancelar ou estornar o documento.` |
| S2 | `O título {firme} do documento de saída {InvoiceNumber} já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.` |
| S3 | `O título {firme} da linha realocada já está composto no título {provisório}: desfaça a composição antes de realocar.` |
| S4 | `O título {firme} da linha realocada já tem baixa por dação ou encontro de contas: estorne-a antes de realocar.` |
| S5 | `Título firme do documento de saída nasce da confirmação: cancele ou estorne o documento de saída para cancelá-lo.` |
| S6 | `O título {código} não é firme nem devolução de documento de saída.` |
| S7 | `Dação só se aplica a título firme do documento de saída.` |
| S8 | `O título {código} não é um título firme do documento de saída.` |
| S9 | `Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.` |
| S10 | `O título {código} não está quitado: o contrato só encerra com todos os títulos quitados.` |
| S11 | `O adiantamento {código} tem {crédito N2 pt-BR} de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.` |
| S12 | `Desfaça a composição do título {código} antes de estornar a fixação.` |

**Motivos de cancelamento:**

- `Documento de saída cancelado ou estornado`, quando o documento sai de Confirmado/Devolvido;
- `Volume realocado para outro contrato`;
- `Natureza não gera financeiro` (`PurchaseFirmPayableRules.NatureWithoutFinancialReason`);
- legado no encerramento: `Encerramento do contrato` (`PurchaseContractsCloseService.LegacyCancellationReason`).

### Armadilhas conhecidas

- `ODataActionParameters` pode chegar **nulo**.
- DTO de action ou function: `[JsonPropertyName("PascalCase")]` em **toda** propriedade.
- `Edm.Decimal` chega como **string** no frontend: aplicar `Number(x)` antes de comparar.
- Enum em expression binding: `${path: 'X', targetType: 'any'}`.
- `--` dentro de comentário XML mata o fragmento sem erro. Todo XML alterado **tem de ser parseado**: `python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" <arquivos>`.
- EF InMemory não confere tradução de LINQ nem índice único. As consultas novas usam comparações simples, e a unicidade é conferida no metadado do modelo.
- `FinancialDocumentsCancelService.Cancel` é `internal static`: usar dentro da Application. Infra não enxerga.
- Construtores com dependência nova: o parâmetro novo entra **por último, opcional** (`= null`), como `gate`/`refusalComplete` do `SalesInvoicesConfirmService`.
  - Assim as ~25 construções nos testes não mudam.
  - O DI do ASP.NET injeta o serviço porque ele **é registrado** na Task 3.
  - Sem o serviço (testes antigos), o gancho fica inativo.
- `yarn test` inteiro **nunca passa**. Gates do frontend: `yarn ts-typecheck && yarn lint && yarn ui5lint`. QUnit pelo navegador.
- Migrations:
  - Só `dotnet ef migrations add`, que não toca o banco.
  - **Nunca** `database update` fora da Task 10, e lá só com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito e conferido (`CEAGUI_SIAGRO_DEV`).
  - Nunca o profile `db-migration`.
- Processos: matar **só por PID** (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`).

### Comandos de teste

- Backend, na raiz: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.
- Suíte inteira: `dotnet test SiagroB1.Application.Tests`. Hoje são 4706 verdes.

## Decisões do plano (lacunas do spec)

1. **O ledger é lido com o que está pendente no contexto.** A sincronização soma as linhas do ledger do banco (rastreadas) mais as `Added` do ChangeTracker, e tira as `Deleted`. Funciona chamada antes ou depois do `SaveChanges` do chamador, e em `Deferred`.
2. **Recusa antes de escrever.** A sincronização calcula o plano de todas as linhas e confere solto/composto/baixado de todo título que mudaria **antes** de qualquer escrita.
3. **Estornar a confirmação de uma devolução que compensou o firme de origem é recusado (S2).** É o espelho da regra 7 da compra: o operador estorna o encontro de contas antes. Fica no Review Focus 4.
4. **Pré-checagem no estorno e no cancelamento.**
   - `SalesInvoicesReverseConfirmService` e `SalesInvoicesCancelService.EnsureBusinessRulesAsync` chamam `EnsureCanUndoAsync` **antes** da transação ou da SEFAZ.
   - A sincronização dentro da transação repete a checagem e cancela os soltos.
5. **Backlog (regra 19).**
   - DryRun só conta as linhas sem título ativo.
   - A execução sincroniza só essas linhas, uma por `SaveChanges`.
   - A ordem é por data e número do documento, para a origem ter firme antes da devolução.
6. **A coluna "NF" da seção Financeiro do contrato de venda** mostra `TaxDocumentNumber`: o número interno quando não há NF.
7. **O filtro Natureza da lista de Receber reaproveita o Select da lista de Pagar.** O item de devolução vem do modelo `pf` (chave e texto), porque `core:ListItem` não tem `visible`.

## Review Focus

1. **Confirmar de novo depois de estornar a confirmação.** O título antigo fica cancelado e nasce um novo ativo. O índice filtrado ignora cancelados, sem duplicar nem estourar.
   - Teste `Reconfirming_after_reversal_creates_a_new_firm` (Task 4).
2. **Cancelar documento autorizado, cujo firme foi composto depois da emissão.** O ensaio antes da SEFAZ (`EnsureCanCancelAsync`) recusa com S1, e nada sai para a SEFAZ.
   - Teste `Pre_sefaz_dry_run_refuses_a_document_with_a_composed_firm` (Task 4).
3. **Realocar parte do volume de uma linha cujo firme está composto.** Recusa S3, sem nenhuma alocação nem título gravado.
   - Teste `Reallocating_a_composed_firm_is_refused_and_nothing_is_written` (Task 5).
4. **Devolução cuja origem está composta.** A devolução nasce em aberto, sem encontro de contas, e o operador a compõe negativa.
   - Teste `Return_against_a_composed_origin_stays_open` (Task 3).
   - O estorno da confirmação de uma devolução já compensada é recusado com S2.
   - Teste `Reversing_a_return_that_netted_the_origin_is_refused` (Task 4).
5. **Linha dividida em três contratos com dízima.** A soma dos títulos é exatamente o `GrandTotal`. O resíduo vai para a maior participação e, no empate, para o contrato da linha.
   - Teste `Distribute_puts_the_rounding_residue_on_the_largest_share` (Task 3).

---

## Task 1: Modelo — natureza de devolução de venda, documento de saída no título, gates e índice

**Arquivos:**

| Ação | Caminho |
|---|---|
| Modificar | `SiagroB1.Domain/Enums/FinancialDocumentNature.cs` |
| Modificar | `SiagroB1.Domain/Entities/FinancialDocument.cs` |
| Modificar | `SiagroB1.Infra/Context/AppDbContext.cs`, índice `IX_FINANCIAL_DOCUMENTS_FirmOrigin`, ~linha 567 |
| Criar (gerado) | `SiagroB1.Migrations/AppContext/<timestamp>_AddSalesFirmReceivables.cs` (+ `.Designer.cs`, snapshot) |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/FinancialSalesReceivableModelTests.cs` |

**Interfaces:**

- Produz:
  - `FinancialDocumentNature.SalesReturn = 5`;
  - `FinancialDocument.SalesInvoiceKey : Guid?`;
  - `FinancialDocument.IsSalesFirmReceivable : bool` (`[NotMapped]`);
  - `FinancialDocument.IsContractFirmDocument : bool` (`[NotMapped]`);
  - `FinancialDocument.HasSameContractAs(FinancialDocument other) : bool`.
- `IsBlockedForSettlement` passa a usar `IsContractFirmDocument`.

- [ ] **Passo 1: escrever o teste que falha**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Modelo dos firmes a receber (spec 2026-10-15): natureza nova, gates e índice de idempotência.</summary>
public class FinancialSalesReceivableModelTests
{
    private static FinancialDocument Document(FinancialDocumentNature nature, FinancialDocumentOrigin origin) => new()
    {
        Key = Guid.NewGuid(), Code = "FN000001", CardCode = "C0001", Direction = FinancialDirection.Receivable,
        Nature = nature, OriginType = origin, NetAmount = 100m, DueDate = new DateTime(2026, 12, 31),
    };

    [Fact]
    public void Sales_return_is_appended_with_a_fixed_number()
    {
        Assert.Equal(5, (int)FinancialDocumentNature.SalesReturn);
        Assert.Equal(4, (int)FinancialDocumentOrigin.SalesInvoice);
    }

    [Theory]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice, true, true)]
    [InlineData(FinancialDocumentNature.SalesReturn, FinancialDocumentOrigin.SalesInvoice, true, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation, false, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseInvoice, false, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, false, false)]
    [InlineData(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, false, false)]
    [InlineData(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.SalesContractPriceFixation, false, false)]
    public void Gates_recognise_the_sales_firm_and_keep_the_purchase_ones(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, bool sales, bool contractFirm)
    {
        var document = Document(nature, origin);

        Assert.Equal(sales, document.IsSalesFirmReceivable);
        Assert.Equal(contractFirm, document.IsContractFirmDocument);
    }

    [Fact]
    public void Sales_firm_and_sales_return_are_blocked_for_direct_settlement()
    {
        Assert.True(Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice).IsBlockedForSettlement);
        Assert.True(Document(FinancialDocumentNature.SalesReturn, FinancialDocumentOrigin.SalesInvoice).IsBlockedForSettlement);
        // O título a receber do washout segue livre (regressão).
        Assert.False(Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout).IsBlockedForSettlement);
    }

    [Fact]
    public void Same_contract_compares_the_purchase_and_sales_pair()
    {
        var contract = Guid.NewGuid();
        var a = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice);
        var b = Document(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.SalesContractPriceFixation);
        a.SalesContractKey = contract;
        b.SalesContractKey = contract;
        Assert.True(a.HasSameContractAs(b));

        b.SalesContractKey = Guid.NewGuid();
        Assert.False(a.HasSameContractAs(b));

        // Compra × venda com a "mesma" chave nunca casa; sem contrato nenhum também não.
        b.SalesContractKey = null;
        b.PurchaseContractKey = contract;
        Assert.False(a.HasSameContractAs(b));
        a.SalesContractKey = null;
        b.PurchaseContractKey = null;
        Assert.False(a.HasSameContractAs(b));
    }

    [Fact]
    public void Firm_origin_index_includes_the_sales_contract_and_the_sales_return()
    {
        using var context = TestDb.CreateContext();
        var index = context.Model.FindEntityType(typeof(FinancialDocument))!.GetIndexes()
            .Single(i => i.Name == "IX_FINANCIAL_DOCUMENTS_FirmOrigin");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(FinancialDocument.OriginType), nameof(FinancialDocument.OriginKey), nameof(FinancialDocument.SalesContractKey) },
            index.Properties.Select(p => p.Name).ToArray());
        Assert.Equal("[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void Edm_exposes_the_sales_invoice_key_but_not_the_gates()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var document = builder.GetEdmModel().EntityContainer.FindEntitySet("FinancialDocuments").EntityType;

        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.SalesInvoiceKey)));
        Assert.Null(document.FindProperty(nameof(FinancialDocument.IsSalesFirmReceivable)));
        Assert.Null(document.FindProperty(nameof(FinancialDocument.IsContractFirmDocument)));
    }
}
```

> Antes de rodar, confira dois pontos:
>
> - Se `TestDb` não tiver `CreateContext()`, use `TestDb.CreateUnitOfWork().Context`.
> - `FinancialDocumentStatus.Canceled` precisa valer **3**. Confira em `SiagroB1.Domain/Enums/FinancialDocumentStatus.cs`. Se valer outro número, use o número real na string do filtro, porque o filtro é montado com `(int)` desse enum.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialSalesReceivableModelTests"`

Esperado: erro de compilação (`SalesReturn`, `SalesInvoiceKey`, `IsSalesFirmReceivable` e `HasSameContractAs` não existem).

- [ ] **Passo 3: implementar**

Em `FinancialDocumentNature.cs`, depois de `PurchaseReturn = 4` (acrescente a vírgula):

```csharp
    PurchaseReturn = 4,

    /// <summary>
    /// Título de DEVOLUÇÃO de venda (spec 2026-10-15, firmes a receber): nasce da confirmação de um documento de saída tipo
    /// Devolução, um por linha × contrato. Compensa o firme da linha de origem (encontro de contas) ou entra NEGATIVO na
    /// composição do provisório a receber.
    /// </summary>
    SalesReturn = 5
```

Em `FinancialDocument.cs`, logo depois de `PurchaseInvoiceKey`:

```csharp
    /// <summary>
    /// Documento de saída do firme a receber (spec 2026-10-15). Sem FK, como <see cref="OriginKey"/>: o título sobrevive
    /// ao registro que narra.
    /// </summary>
    public Guid? SalesInvoiceKey { get; set; }
```

Troque o corpo de `IsBlockedForSettlement`. O braço `_ => IsPurchaseFirmPayable,` vira:

```csharp
        _ => IsContractFirmDocument,
```

Atualize o comentário: "Firme e devolução de contrato (compra ou venda): sempre — saem só por composição, dação ou encontro de contas."

Depois de `IsPurchaseFirmPayable`:

```csharp
    /// <summary>
    /// Firme ou devolução nascidos da confirmação do documento de saída (spec 2026-10-15, firmes a receber). Só em
    /// memória (o EF não traduz) e fora do EDM.
    /// </summary>
    [NotMapped]
    public bool IsSalesFirmReceivable =>
        OriginType == FinancialDocumentOrigin.SalesInvoice &&
        Nature is FinancialDocumentNature.Firm or FinancialDocumentNature.SalesReturn;

    /// <summary>Firme ou devolução de CONTRATO, de compra ou de venda: sai só por composição, dação ou encontro de contas.</summary>
    [NotMapped]
    public bool IsContractFirmDocument => IsPurchaseFirmPayable || IsSalesFirmReceivable;

    /// <summary>
    /// Mesmo contrato (spec 2026-10-15, regra 13): compara o PAR compra/venda, então um título de compra nunca casa com
    /// um de venda. Sem contrato nenhum, não casa.
    /// </summary>
    public bool HasSameContractAs(FinancialDocument other) =>
        (PurchaseContractKey ?? SalesContractKey) is not null &&
        PurchaseContractKey == other.PurchaseContractKey &&
        SalesContractKey == other.SalesContractKey;
```

Em `AppDbContext.cs`, troque a definição do índice `IX_FINANCIAL_DOCUMENTS_FirmOrigin`:

```csharp
            // Spec 2026-10-15: o firme a receber é por linha × CONTRATO (a realocação desmembra), então o contrato de venda
            // entra na chave. Na compra ele é sempre NULL, e o índice único do SQL Server trata NULLs como iguais: a
            // unicidade da compra não muda.
            .HasIndex(x => new { x.OriginType, x.OriginKey, x.SalesContractKey }, "IX_FINANCIAL_DOCUMENTS_FirmOrigin")
            .IsUnique()
            .HasFilter($"[Nature] IN ({(int)FinancialDocumentNature.Firm}, {(int)FinancialDocumentNature.PurchaseReturn}, {(int)FinancialDocumentNature.SalesReturn}) " +
                       $"AND [Status] <> {(int)FinancialDocumentStatus.Canceled} " +
                       "AND [OriginKey] IS NOT NULL");
```

Mantenha o resto da cadeia como estava. Leia as linhas ao redor antes de editar.

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialSalesReceivableModelTests|FullyQualifiedName~FinancialDocumentCompositionModelTests"`

Esperado: PASS.

- [ ] **Passo 5: gerar a migration**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddSalesFirmReceivables --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Confira no `Up()` gerado três coisas:

1. `AddColumn<Guid>("SalesInvoiceKey", "FINANCIAL_DOCUMENTS", nullable: true)`.
2. `DropIndex("IX_FINANCIAL_DOCUMENTS_FirmOrigin")`.
3. `CreateIndex` com as colunas `OriginType, OriginKey, SalesContractKey`, `unique: true` e o filtro novo.

O `Down()` precisa fazer o inverso. Nada mais deve aparecer: se aparecer, pare e reporte.

- [ ] **Passo 6: build, modelo pendente e suíte**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet test SiagroB1.Application.Tests
```

Esperado: build OK, "No changes have been made to the model" e todos verdes (4706 + os novos).

- [ ] **Passo 7: stage**

```bash
git add SiagroB1.Domain/Enums/FinancialDocumentNature.cs SiagroB1.Domain/Entities/FinancialDocument.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/Financials/FinancialSalesReceivableModelTests.cs
```

O controlador commita com:

```
feat(financial): modelo do título firme a receber do documento de saída

Natureza nova SalesReturn (5), chave do documento de saída no título e os
gates IsSalesFirmReceivable/IsContractFirmDocument. O índice de
idempotência do firme passa a incluir o contrato de venda: o firme a
receber é por linha × contrato, porque a realocação desmembra a linha.
Na compra o contrato de venda é sempre NULL e a unicidade não muda.

DB: AddSalesFirmReceivables

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 2: Financeiro generalizado — composição, dação, encontro de contas, guard, adiantamento e cancelamento valem para venda

**Arquivos (todos em `SiagroB1.Application/Services/Financials/`):**

| Ação | Arquivo |
|---|---|
| Modificar | `FinancialDocumentsComposeService.cs` (`EnsureComposable`) |
| Modificar | `FinancialDocumentsGetCompositionService.cs` |
| Modificar | `FinancialDocumentsDationService.cs` |
| Modificar | `FinancialDocumentsNettingService.cs` (`ExecuteAsync` e o método novo `EnqueueAutoForSalesAsync`) |
| Modificar | `FinancialDocumentsSettlementGuardService.cs` |
| Modificar | `FinancialDocumentsApplyAdvanceService.cs` |
| Modificar | `FinancialDocumentsGetAdvanceCandidatesService.cs` |
| Modificar | `FinancialDocumentsCancelService.cs` |

| Teste | Caminho |
|---|---|
| Criar | `SiagroB1.Application.Tests/Financials/SalesReceivableDocs.cs` (seed de documentos crus) |
| Criar | `SiagroB1.Application.Tests/Financials/FinancialSalesReceivableServicesTests.cs` |

**Interfaces:**

- Consome (Task 1): `IsSalesFirmReceivable`, `IsContractFirmDocument`, `HasSameContractAs`, `FinancialDocumentNature.SalesReturn`.
- Produz: `FinancialDocumentsNettingService.EnqueueAutoForSalesAsync(FinancialDocument returnDocument, Guid? originLineKey, string userName) : Task`, ENQUEUE-ONLY.

- [ ] **Passo 1: seed de documentos crus**

`SalesReceivableDocs.cs`:

```csharp
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Títulos de venda gravados diretamente (sem a sincronização da Task 3): contrato VC-1 do cliente C0001, provisório a
/// receber FN-PROV-V1 de 10.000,00 e a conta CX01. Para exercitar os serviços do financeiro generalizados.
/// </summary>
internal sealed class SalesReceivableDocs
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public AppDbContext Context => Db.Context;
    public SalesContract Contract { get; private set; } = null!;
    public FinancialDocument Provisional { get; private set; } = null!;
    private int _next = 1;

    public static async Task<SalesReceivableDocs> CreateAsync(decimal provisionalAmount = 10_000m)
    {
        var docs = new SalesReceivableDocs();
        docs.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        docs.Contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);
        docs.Contract.Code = "VC-1";
        docs.Context.SalesContracts.Add(docs.Contract);
        await docs.Context.SaveChangesAsync();
        docs.Provisional = await docs.AddAsync(FinancialDocumentNature.Provisional,
            FinancialDocumentOrigin.SalesContractPriceFixation, provisionalAmount, code: "FN-PROV-V1");
        return docs;
    }

    public async Task<FinancialDocument> AddAsync(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, decimal amount,
        Guid? salesContractKey = null, Guid? purchaseContractKey = null, string? code = null, Guid? originKey = null,
        FinancialDirection direction = FinancialDirection.Receivable)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code ?? $"FN-V{_next++:000}", CardCode = "C0001", BranchCode = "01",
            Direction = direction, Nature = nature, Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 12, 31), NetAmount = amount, Currency = CurrencyType.Brl,
            OriginType = origin, OriginKey = originKey ?? Guid.NewGuid(),
            SalesContractKey = purchaseContractKey is null ? salesContractKey ?? Contract.Key : null,
            PurchaseContractKey = purchaseContractKey,
            CreatedAt = DateTime.Now.AddMinutes(_next),
        };
        Context.FinancialDocuments.Add(document);
        await Context.SaveChangesAsync();
        return document;
    }

    public Task<FinancialDocument> FirmAsync(decimal amount, Guid? contractKey = null, Guid? originKey = null) =>
        AddAsync(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice, amount, contractKey, originKey: originKey);

    public Task<FinancialDocument> ReturnAsync(decimal amount, Guid? contractKey = null) =>
        AddAsync(FinancialDocumentNature.SalesReturn, FinancialDocumentOrigin.SalesInvoice, amount, contractKey);

    public async Task<FinancialDocument> ReloadAsync(Guid key) =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(Context.FinancialDocuments), d => d.Key == key);
}
```

(Se preferir, troque o `ReloadAsync` por `using Microsoft.EntityFrameworkCore;` e `Context.FinancialDocuments.AsNoTracking().SingleAsync(...)`.)

- [ ] **Passo 2: escrever os testes que falham**

`FinancialSalesReceivableServicesTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15, regras 12–14: o financeiro dos firmes de compra vale para os firmes a receber.</summary>
public class FinancialSalesReceivableServicesTests
{
    private static FinancialDocumentsComposeService Compose(SalesReceivableDocs d) =>
        new(d.Db, new FinancialDocumentChangeLogService(d.Context));

    [Fact]
    public async Task Sales_firm_is_composed_into_the_sales_provisional_and_received_there()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(9_000m);

        await Compose(d).ExecuteAsync(d.Provisional.Key, [firm.Key], "ana");
        await new FinancialDocumentsSettleService(d.Db)
            .ExecuteAsync(d.Provisional.Key, "CX01", 9_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        var provisional = await d.ReloadAsync(d.Provisional.Key);
        Assert.Equal(9_000m, provisional.ComposedAmount);
        Assert.Equal(9_000m, provisional.SettledAmount);
        Assert.Equal(0m, (await d.ReloadAsync(firm.Key)).OpenAmount);
    }

    [Fact]
    public async Task Sales_return_enters_the_composition_negative()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(9_000m);
        var refund = await d.ReturnAsync(1_000m);

        await Compose(d).ExecuteAsync(d.Provisional.Key, [refund.Key, firm.Key], "ana");

        Assert.Equal(8_000m, (await d.ReloadAsync(d.Provisional.Key)).ComposedAmount);
    }

    [Fact]
    public async Task Purchase_firm_does_not_compose_into_a_sales_provisional()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var purchaseFirm = await d.AddAsync(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseInvoice, 500m,
            purchaseContractKey: Guid.NewGuid(), direction: FinancialDirection.Payable);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(d).ExecuteAsync(d.Provisional.Key, [purchaseFirm.Key], "ana"));

        Assert.Equal($"O título {purchaseFirm.Code} é de outro contrato.", error.Message);
    }

    [Fact]
    public async Task Sales_firm_of_another_contract_does_not_compose()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var other = await d.FirmAsync(500m, contractKey: Guid.NewGuid());

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(d).ExecuteAsync(d.Provisional.Key, [other.Key], "ana"));

        Assert.Equal($"O título {other.Code} é de outro contrato.", error.Message);
    }

    [Fact]
    public async Task Sales_advance_is_not_composable()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(d).ExecuteAsync(d.Provisional.Key, [advance.Key], "ana"));

        Assert.Equal($"O título {advance.Code} não é firme nem devolução de documento de saída.", error.Message);
    }

    [Fact]
    public async Task Sales_firm_cannot_be_settled_in_cash()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => new FinancialDocumentsSettleService(d.Db)
            .ExecuteAsync(firm.Key, "CX01", 500m, DateTime.Today, 0m, 0m, 0m, null, null, "ana"));

        Assert.Equal("Título firme não recebe baixa direta: componha no provisório ou baixe por dação.", error.Message);
    }

    [Fact]
    public async Task Dation_settles_the_sales_firm_and_refuses_other_sales_documents()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(500m);
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m);
        var dation = new FinancialDocumentsDationService(d.Db);

        await dation.ExecuteAsync(firm.Key, 500m, DateTime.Today, "sobra faturada acima do contrato", "ana");
        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            dation.ExecuteAsync(advance.Key, 100m, DateTime.Today, "x", "ana"));

        Assert.Equal(FinancialDocumentStatus.Settled, (await d.ReloadAsync(firm.Key)).Status);
        Assert.Equal("Dação só se aplica a título firme do documento de saída.", error.Message);
    }

    [Fact]
    public async Task Manual_netting_pairs_the_sales_return_with_a_loose_firm_of_the_same_contract()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(900m);
        var refund = await d.ReturnAsync(300m);

        await new FinancialDocumentsNettingService(d.Db).ExecuteAsync(refund.Key, firm.Key, "ana");

        Assert.Equal(300m, (await d.ReloadAsync(refund.Key)).SettledAmount);
        Assert.Equal(300m, (await d.ReloadAsync(firm.Key)).SettledAmount);
        Assert.Equal(2, await d.Context.FinancialSettlements.CountAsync(s => s.Origin == FinancialSettlementOrigin.Netting));
    }

    [Fact]
    public async Task Manual_netting_refuses_a_sales_document_that_is_not_a_firm()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var refund = await d.ReturnAsync(300m);
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsNettingService(d.Db).ExecuteAsync(refund.Key, advance.Key, "ana"));

        Assert.Equal($"O título {advance.Code} não é um título firme do documento de saída.", error.Message);
    }

    [Fact]
    public async Task Auto_netting_for_sales_pairs_the_return_with_the_loose_origin_line_firm_of_the_same_contract()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var originLine = Guid.NewGuid();
        var firm = await d.FirmAsync(900m, originKey: originLine);
        var otherContractFirm = await d.FirmAsync(900m, contractKey: Guid.NewGuid(), originKey: originLine);
        var refund = await d.ReturnAsync(300m);

        await new FinancialDocumentsNettingService(d.Db).EnqueueAutoForSalesAsync(refund, originLine, "ana");
        await d.Db.SaveChangesAsync();

        Assert.Equal(300m, (await d.ReloadAsync(firm.Key)).SettledAmount);
        Assert.Equal(0m, (await d.ReloadAsync(otherContractFirm.Key)).SettledAmount);
        Assert.Equal(300m, (await d.ReloadAsync(refund.Key)).SettledAmount);
    }

    [Fact]
    public async Task Composition_candidates_of_the_sales_provisional_are_its_sales_firms_and_returns()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(900m);
        var refund = await d.ReturnAsync(100m);
        await d.FirmAsync(900m, contractKey: Guid.NewGuid());
        await d.AddAsync(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, 50m);

        var result = await new FinancialDocumentsGetCompositionService(d.Db).ExecuteAsync(d.Provisional.Key);

        Assert.Equal(new[] { firm.Key, refund.Key }.OrderBy(k => k), result.Candidates.Select(c => c.DocumentKey).OrderBy(k => k));
    }

    [Fact]
    public async Task Sales_advance_is_applied_to_the_sales_provisional()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(9_000m);
        await Compose(d).ExecuteAsync(d.Provisional.Key, [firm.Key], "ana");
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 2_000m);
        await new FinancialDocumentsSettleService(d.Db)
            .ExecuteAsync(advance.Key, "CX01", 2_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        var candidates = await new FinancialDocumentsGetAdvanceCandidatesService(d.Db).ExecuteAsync(d.Provisional.Key);
        await new FinancialDocumentsApplyAdvanceService(d.Db, new FinancialDocumentChangeLogService(d.Context))
            .ExecuteAsync(d.Provisional.Key, advance.Key, 2_000m, DateTime.Today, "ana");

        Assert.Equal(advance.Key, Assert.Single(candidates).DocumentKey);
        Assert.Equal(2_000m, (await d.ReloadAsync(d.Provisional.Key)).SettledAmount);
        Assert.Equal(2_000m, (await d.ReloadAsync(advance.Key)).AppliedAmount);
    }

    [Fact]
    public async Task Sales_firm_cannot_be_cancelled_on_the_financial_screen()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(d.Db).ExecuteAsync(firm.Key, "teste", "ana"));

        Assert.Equal(
            "Título firme do documento de saída nasce da confirmação: cancele ou estorne o documento de saída para cancelá-lo.",
            error.Message);
    }
}
```

> As assinaturas de `FinancialDocumentsSettleService.ExecuteAsync`, `FinancialDocumentsDationService.ExecuteAsync`, `FinancialDocumentsCancelService(IUnitOfWork)` e `FinancialDocumentsApplyAdvanceService.ExecuteAsync` são as atuais. Confira-as nos testes vizinhos: `FinancialDocumentsValueComplementTests`, `FinancialDocumentsDationTests`, `AdvanceApplicationSeed` e `FinancialDocumentsCancelServiceTests`. Ajuste a ordem dos argumentos se divergir.

- [ ] **Passo 3: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialSalesReceivableServicesTests"`

Esperado: falha de compilação (`EnqueueAutoForSalesAsync`), e depois falhas de asserção, porque os gates ainda são só de compra.

- [ ] **Passo 4: implementar**

**`FinancialDocumentsComposeService.EnsureComposable`.** As duas primeiras checagens viram:

```csharp
        if (!document.IsContractFirmDocument)
            throw new ApplicationException(provisional.SalesContractKey is null
                ? $"O título {document.Code} não é firme nem devolução da conciliação fiscal."
                : $"O título {document.Code} não é firme nem devolução de documento de saída.");

        // Spec 2026-10-15, regra 13: o PAR compra/venda — um título de compra nunca casa com um de venda.
        if (!document.HasSameContractAs(provisional))
            throw new ApplicationException($"O título {document.Code} é de outro contrato.");
```

O resto fica igual. A ordenação "firme primeiro" já trata `SalesReturn` como devolução, porque compara `Nature == Firm`.

**`FinancialDocumentsGetCompositionService`.**

No `switch` de candidatos:

```csharp
                FinancialDocumentNature.Provisional => await ComposeCandidatesAsync(document),
                FinancialDocumentNature.PurchaseReturn when document.IsPurchaseFirmPayable => await NettingCandidatesAsync(document),
                FinancialDocumentNature.SalesReturn when document.IsSalesFirmReceivable => await NettingCandidatesAsync(document),
                _ => [],
```

Em `ComposeCandidatesAsync`, troque `OpenFirmPayablesAsync(provisional.PurchaseContractKey)` por `OpenContractFirmsAsync(provisional)`.

Em `NettingCandidatesAsync`, troque `OpenFirmPayablesAsync(refund.PurchaseContractKey)` por `OpenContractFirmsAsync(refund)`.

Substitua o método `OpenFirmPayablesAsync` por:

```csharp
    /// <summary>
    /// Firmes e devoluções do MESMO contrato do título-âncora, não cancelados e com saldo. Compra: conciliação e complemento
    /// de valor (D11). Venda (spec 2026-10-15): documento de saída. O título a receber do washout (Firm, outra origem)
    /// não entra.
    /// </summary>
    private async Task<List<FinancialDocument>> OpenContractFirmsAsync(FinancialDocument anchor)
    {
        var documents = db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled && d.NetAmount > d.SettledAmount);

        if (anchor.PurchaseContractKey is { } purchaseKey)
            documents = documents.Where(d => d.PurchaseContractKey == purchaseKey &&
                                             (d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation ||
                                              d.OriginType == FinancialDocumentOrigin.PurchaseInvoice) &&
                                             (d.Nature == FinancialDocumentNature.Firm || d.Nature == FinancialDocumentNature.PurchaseReturn));
        else if (anchor.SalesContractKey is { } salesKey)
            documents = documents.Where(d => d.SalesContractKey == salesKey &&
                                             d.OriginType == FinancialDocumentOrigin.SalesInvoice &&
                                             (d.Nature == FinancialDocumentNature.Firm || d.Nature == FinancialDocumentNature.SalesReturn));
        else
            // Sem contrato não há candidato: comparar com chave nula casaria títulos sem contrato.
            return [];

        return await documents.OrderBy(d => d.DocumentDate).ThenBy(d => d.Code).ToListAsync();
    }
```

**`FinancialDocumentsDationService`.**

```csharp
        if (!document.IsContractFirmDocument || document.Nature != FinancialDocumentNature.Firm)
            throw new ApplicationException(document.SalesContractKey is null
                ? "Dação só se aplica a título firme da conciliação fiscal."
                : "Dação só se aplica a título firme do documento de saída.");
```

**`FinancialDocumentsNettingService.ExecuteAsync`.** As três primeiras checagens viram:

```csharp
        if (!refund.IsContractFirmDocument ||
            refund.Nature is not (FinancialDocumentNature.PurchaseReturn or FinancialDocumentNature.SalesReturn))
            throw new ApplicationException("Compensar com firme só vale para título de devolução.");

        var firm = await context.FinancialDocuments.FirstOrDefaultAsync(d => d.Key == firmKey)
                   ?? throw new NotFoundException("Documento financeiro não encontrado.");
        if (!firm.IsContractFirmDocument || firm.Nature != FinancialDocumentNature.Firm)
            throw new ApplicationException(refund.IsSalesFirmReceivable
                ? $"O título {firm.Code} não é um título firme do documento de saída."
                : $"O título {firm.Code} não é um título firme da conciliação fiscal.");

        if (!firm.HasSameContractAs(refund))
            throw new ApplicationException($"O título {firm.Code} é de outro contrato.");
```

Acrescente o método, depois de `EnqueueAutoAsync`:

```csharp
    /// <summary>
    /// Spec 2026-10-15 (regra 9), ENQUEUE-ONLY: a devolução de venda recém-nascida compensa os firmes ATIVOS da LINHA de
    /// origem no MESMO contrato de venda, soltos e com saldo, do mais antigo (CreatedAt, depois RowId). Sem origem, ou
    /// origem composta ou quitada, a devolução fica em aberto para o operador.
    /// </summary>
    public async Task EnqueueAutoForSalesAsync(FinancialDocument returnDocument, Guid? originLineKey, string userName)
    {
        if (originLineKey is null || returnDocument.Nature != FinancialDocumentNature.SalesReturn ||
            returnDocument.SalesContractKey is null)
            return;

        var context = db.Context;
        var origins = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice &&
                        d.Nature == FinancialDocumentNature.Firm &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.OriginKey == originLineKey &&
                        d.SalesContractKey == returnDocument.SalesContractKey)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.RowId)
            .ToListAsync();

        foreach (var firm in origins)
        {
            if (returnDocument.OpenAmount <= 0m)
                break;

            // A instância rastreada pode ter sido cancelada neste mesmo SaveChanges.
            if (firm.Status == FinancialDocumentStatus.Canceled)
                continue;

            if (firm.OpenAmount <= 0m || firm.Currency != returnDocument.Currency ||
                PurchaseFirmPayableRules.ProvisionalOf(context, firm.Key) is not null)
                continue;

            await EnqueuePairAsync(returnDocument, firm, Math.Min(returnDocument.OpenAmount, firm.OpenAmount), userName);
        }
    }
```

**`FinancialDocumentsSettlementGuardService.EnsureCanSettle`.** Troque `if (document.IsPurchaseFirmPayable)` por `if (document.IsContractFirmDocument)` e mantenha a mensagem. No comentário, troque "firme e devolução da conciliação" por "firme e devolução de contrato (compra ou venda)".

**`FinancialDocumentsApplyAdvanceService`.**

- Apague o comentário "Só compra: …".
- A checagem `if (provisional.PurchaseContractKey is null)` vira `if (provisional.PurchaseContractKey is null && provisional.SalesContractKey is null)`, com a mesma mensagem.
- A checagem de mesmo contrato vira:

```csharp
        if (!advance.HasSameContractAs(provisional))
            throw new ApplicationException(
                $"Adiantamento {advance.Code} não pertence ao contrato do título {provisional.Code}.");
```

**`FinancialDocumentsGetAdvanceCandidatesService`.**

```csharp
        if (provisional.Nature != FinancialDocumentNature.Provisional ||
            provisional.Status == FinancialDocumentStatus.Canceled ||
            (provisional.PurchaseContractKey is null && provisional.SalesContractKey is null))
            return [];

        var available = provisional.AvailableToSettleAmount;

        var advances = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.PurchaseContractKey == provisional.PurchaseContractKey &&
                        d.SalesContractKey == provisional.SalesContractKey &&
                        d.Nature == FinancialDocumentNature.Advance &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.Currency == provisional.Currency &&
                        d.SettledAmount - d.AppliedAmount > 0m)
            .OrderBy(d => d.DocumentDate).ThenBy(d => d.Code)
            .ToListAsync();
```

**`FinancialDocumentsCancelService.ExecuteAsync`.** Logo depois de "já está cancelado", antes das checagens de compra:

```csharp
        // Spec 2026-10-15 (regra 4): o firme a receber nasce e morre com o documento de saída — cancelar à mão deixaria a
        // linha confirmada sem título, e a próxima sincronização criaria outro.
        if (document.IsSalesFirmReceivable)
            throw new ApplicationException(
                "Título firme do documento de saída nasce da confirmação: cancele ou estorne o documento de saída para cancelá-lo.");
```

- [ ] **Passo 5: rodar e ver passar, com a regressão do financeiro**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Financials"`

Esperado: PASS, sem nenhum teste antigo da compra quebrado.

- [ ] **Passo 6: stage**

```bash
git add SiagroB1.Application/Services/Financials SiagroB1.Application.Tests/Financials/SalesReceivableDocs.cs SiagroB1.Application.Tests/Financials/FinancialSalesReceivableServicesTests.cs
```

O controlador commita com:

```
feat(financial): composição, dação e encontro de contas valem para o firme a receber

O financeiro dos firmes de compra passa a reconhecer o firme e a devolução
nascidos do documento de saída: compor no provisório a receber, dação,
encontro de contas (manual e o automático por linha de origem), trava de
baixa direta, compensação de adiantamento de venda e a recusa de
cancelar na tela. "Mesmo contrato" compara o par compra/venda; os textos
da compra não mudam.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 3: Sincronização do firme a receber (o coração)

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Application/Services/Financials/FinancialDocumentsSalesFirmSyncService.cs` |
| Modificar | `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, junto de `FinancialDocumentsFirmSyncService` (~linha 667): `services.AddScoped<FinancialDocumentsSalesFirmSyncService>();` |
| Modificar | `SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs` (fábrica `SalesFirmSync`) |
| Criar | `SiagroB1.Application.Tests/Financials/SalesFirmReceivableSeed.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesFirmSyncTests.cs` |

**Interfaces:**

- Consome:
  - `FinancialDocumentsFirmSyncService.CreatesFinancialDocumentAsync(int?)`;
  - `FinancialDocumentsNettingService.EnqueueAutoForSalesAsync` (Task 2);
  - `PurchaseFirmPayableRules.ProvisionalOf`;
  - `FinancialDocumentsCancelService.Cancel`;
  - `FinancialDocumentsRecalculateBalanceService.RecalculateAsync`.
- Produz:
  - `public enum SalesFirmSyncTrigger { Document, Reallocation }`;
  - `FinancialDocumentsSalesFirmSyncService(IUnitOfWork, DocNumberSequenceService, FinancialDocumentChangeLogService, FinancialDocumentsFirmSyncService, FinancialDocumentsNettingService)`;
  - `Task EnqueueSyncAsync(IReadOnlyCollection<Guid> salesInvoiceItemKeys, string userName, SalesFirmSyncTrigger trigger = SalesFirmSyncTrigger.Document)`, ENQUEUE-ONLY;
  - `Task EnsureCanUndoAsync(SalesInvoice invoice)`, só leitura, recusa S1/S2;
  - `internal static Dictionary<Guid, decimal> Distribute(decimal value, Guid lineContractKey, IReadOnlyList<(Guid ContractKey, decimal Volume)> volumes)`;
  - as constantes `DocumentUndoneReason` e `ReallocatedReason`.
- Testes: `FinancialDocumentTestServices.SalesFirmSync(IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE")` e `SalesFirmReceivableSeed`.

- [ ] **Passo 1: fábrica e seed de teste**

Em `FinancialDocumentTestServices.cs`, acrescente:

```csharp
    /// <summary>Sync do firme a receber (spec 2026-10-15), com numeração em memória e a natureza lida pelo UsageService local.</summary>
    public static FinancialDocumentsSalesFirmSyncService SalesFirmSync(
        IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE")
    {
        var sequence = numbers ?? new FakeDocNumberSequenceService();
        return new(db, sequence, new FinancialDocumentChangeLogService(db.Context), FirmSync(db, sequence, erp),
            new FinancialDocumentsNettingService(db));
    }
```

`SalesFirmReceivableSeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Cenário dos firmes a receber (spec 2026-10-15): contrato de venda VC-1 (C0001, aprovado), provisório a receber
/// FN-PROV-V1, conta CX01 e duas naturezas de saída que consomem saldo (uma gera financeiro, outra não). Documentos de
/// saída AVULSOS de uma linha: 1.000 × R$ 90,00 = 90.000,00.
/// </summary>
internal sealed class SalesFirmReceivableSeed
{
    public const decimal DefaultLineAmount = 90_000m;

    public UnitOfWork Db { get; }
    public AppDbContext Context => Db.Context;
    public FakeDocNumberSequenceService Numbers { get; } = new();
    public string Erp { get; private init; } = "STANDALONE";
    public SalesContract Contract { get; private set; } = null!;
    public FinancialDocument Provisional { get; private set; } = null!;
    public int FinancialUsageCode { get; private set; }
    public int NonFinancialUsageCode { get; private set; }

    private SalesFirmReceivableSeed(UnitOfWork db) => Db = db;

    public static async Task<SalesFirmReceivableSeed> CreateAsync(decimal provisionalAmount = 100_000m, string erp = "STANDALONE")
    {
        var seed = new SalesFirmReceivableSeed(TestDb.CreateUnitOfWork()) { Erp = erp };
        seed.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        seed.FinancialUsageCode = (await usages.CreateAsync(Usage("Venda de produção", createsFinancial: true))).Code;
        seed.NonFinancialUsageCode = (await usages.CreateAsync(Usage("Saída sem financeiro", createsFinancial: false))).Code;
        seed.Contract = await seed.AddContractAsync("VC-1");
        seed.Provisional = await seed.AddProvisionalAsync(seed.Contract, provisionalAmount, "FN-PROV-V1");
        return seed;
    }

    private static UsageModel Usage(string name, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
        ContractBalanceEffect = ContractBalanceEffect.Consume, RequiresContract = true, RequiresQuantity = true,
        CreatesFinancialDocument = createsFinancial,
    };

    public async Task<SalesContract> AddContractAsync(string code)
    {
        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);
        contract.Code = code;
        Context.SalesContracts.Add(contract);
        await Context.SaveChangesAsync();
        return contract;
    }

    public async Task<FinancialDocument> AddProvisionalAsync(SalesContract contract, decimal amount, string code,
        DateTime? createdAt = null)
    {
        var provisional = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "C0001", BranchCode = "01",
            Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Provisional,
            Status = FinancialDocumentStatus.Open, DueDate = new DateTime(2026, 12, 31), NetAmount = amount,
            OriginType = FinancialDocumentOrigin.SalesContractPriceFixation, OriginKey = Guid.NewGuid(),
            OriginDocNumber = contract.Code, SalesContractKey = contract.Key, Currency = CurrencyType.Brl,
            CreatedAt = createdAt ?? DateTime.Now,
        };
        Context.FinancialDocuments.Add(provisional);
        await Context.SaveChangesAsync();
        return provisional;
    }

    /// <summary>Documento de saída AVULSO de uma linha, gravado. Por padrão Pendente, com a natureza que gera financeiro.</summary>
    public async Task<(SalesInvoice Invoice, SalesInvoiceItem Line)> AddInvoiceAsync(
        decimal quantity = 1_000m, decimal unitPrice = 90m, SalesContract? contract = null, bool withContract = true,
        int? usageCode = null, bool withUsage = true, InvoiceStatus status = InvoiceStatus.Pending,
        SalesInvoiceType type = SalesInvoiceType.Normal, Guid? originInvoiceKey = null, Guid? originItemKey = null,
        string number = "DS000001")
    {
        var target = contract ?? Contract;
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(status, type, originKey: originInvoiceKey);
        invoice.BranchCode = "01";
        invoice.InvoiceNumber = number;
        invoice.CardName = "CLIENTE TESTE";
        invoice.TaxDocumentNumber = "7001";
        invoice.TaxDocumentSeries = "1";
        invoice.InvoiceDate = new DateTime(2026, 10, 15);
        var line = SalesContractsAllocationTestSupport.NewItem(
            invoice, withContract ? target.Key : null, releaseKey: null, quantity, unitPrice, originItemKey);
        line.UsageCode = withUsage ? usageCode ?? FinancialUsageCode : null;
        if (type == SalesInvoiceType.Return)
            invoice.NetWeight = quantity;   // a confirmação da devolução confere peso × itens
        Context.SalesInvoices.Add(invoice);
        await Context.SaveChangesAsync();
        return (invoice, line);
    }

    /// <summary>Linha de ledger gravada (sem passar pelos serviços de alocação).</summary>
    public async Task AllocateAsync(SalesInvoiceItem line, SalesContract contract, decimal volume,
        SalesContractAllocationOrigin origin = SalesContractAllocationOrigin.Billing)
    {
        Context.SalesContractsAllocations.Add(
            SalesContractsAllocationTestSupport.NewAllocation(contract.Key, line.Key!.Value, volume, origin: origin));
        await Context.SaveChangesAsync();
    }

    public FinancialDocumentsSalesFirmSyncService Sync() => FinancialDocumentTestServices.SalesFirmSync(Db, Numbers, Erp);

    public async Task SyncAsync(SalesInvoiceItem line, SalesFirmSyncTrigger trigger = SalesFirmSyncTrigger.Document)
    {
        await Sync().EnqueueSyncAsync([line.Key!.Value], "ana", trigger);
        await Db.SaveChangesAsync();
    }

    public Task<List<FinancialDocument>> TitlesOfAsync(SalesInvoiceItem line, bool includeCanceled = false) =>
        Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == line.Key &&
                        (includeCanceled || d.Status != FinancialDocumentStatus.Canceled))
            .OrderBy(d => d.Code)
            .ToListAsync();

    public Task ComposeAsync(FinancialDocument document, FinancialDocument? provisional = null) =>
        new FinancialDocumentsComposeService(Db, new FinancialDocumentChangeLogService(Context))
            .ExecuteAsync((provisional ?? Provisional).Key, [document.Key], "ana");

    public async Task<FinancialDocument> ReloadAsync(Guid key) =>
        await Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);

    /// <summary>Muda o status do documento direto no banco (para exercitar a sincronização sem os serviços).</summary>
    public async Task SetStatusAsync(SalesInvoice invoice, InvoiceStatus status)
    {
        var tracked = await Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        tracked.InvoiceStatus = status;
        await Context.SaveChangesAsync();
    }
}
```

> `UsageModel` precisa aceitar `ContractBalanceEffect`, `RequiresContract` e `RequiresQuantity` (`SalesInvoicesFiscalAdjustmentTests` já usa os três). Se o `UsageService.CreateAsync` recusar a natureza de saída por algum campo obrigatório, complete-o a partir de `SalesInvoicesFiscalAdjustmentTests.SeedUsageAsync`.

- [ ] **Passo 2: escrever os testes que falham**

`FinancialDocumentsSalesFirmSyncTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15: o firme a receber por linha × contrato, derivado do ledger, e o que muda nele.</summary>
public class FinancialDocumentsSalesFirmSyncTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    // ---- Distribuição (modelo do spec) ----

    [Fact]
    public void Distribute_without_volume_puts_the_whole_value_on_the_line_contract()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(500m, A, []);
        Assert.Equal(500m, Assert.Single(result).Value);
        Assert.Equal(A, result.Keys.Single());

        var zero = FinancialDocumentsSalesFirmSyncService.Distribute(500m, A, [(B, 0m)]);
        Assert.Equal(A, zero.Keys.Single());
    }

    [Fact]
    public void Distribute_follows_the_ledger_shares()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(1_000m, A, [(A, 600m), (B, 400m)]);
        Assert.Equal(600m, result[A]);
        Assert.Equal(400m, result[B]);
    }

    [Fact]
    public void Distribute_puts_the_rounding_residue_on_the_largest_share()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(100m, A, [(A, 1m), (B, 1m), (C, 1m)]);

        Assert.Equal(100m, result.Values.Sum());
        Assert.Equal(33.34m, result[A]);   // empate: o contrato da linha leva o resíduo
        Assert.Equal(33.33m, result[B]);
        Assert.Equal(33.33m, result[C]);
    }

    [Fact]
    public void Distribute_ignores_contracts_whose_net_volume_is_not_positive()
    {
        // Linha toda realocada para B: A fica com 0 líquido.
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(1_000m, A, [(A, 0m), (B, 1_000m)]);
        Assert.Equal(B, result.Keys.Single());
    }

    [Fact]
    public void Distribute_of_a_return_uses_the_negative_volumes_as_shares()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(300m, A, [(A, -200m), (B, -100m)]);
        Assert.Equal(200m, result[A]);
        Assert.Equal(100m, result[B]);
    }

    // ---- Nascimento (G1–G4) ----

    [Fact]
    public async Task Confirmed_line_with_contract_and_financial_usage_creates_one_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);

        await seed.SyncAsync(line);

        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        Assert.Equal(FinancialDirection.Receivable, firm.Direction);
        Assert.Equal(FinancialDocumentNature.Firm, firm.Nature);
        Assert.Equal(FinancialDocumentOrigin.SalesInvoice, firm.OriginType);
        Assert.Equal(SalesFirmReceivableSeed.DefaultLineAmount, firm.NetAmount);
        Assert.Equal(seed.Contract.Key, firm.SalesContractKey);
        Assert.Null(firm.PurchaseContractKey);
        Assert.Equal("VC-1", firm.OriginDocNumber);
        Assert.Equal(invoice.Key, firm.SalesInvoiceKey);
        Assert.Equal("7001-1", firm.TaxDocumentNumber);
        Assert.Equal("C0001", firm.CardCode);
        Assert.Equal("CLIENTE TESTE", firm.CardName);
        Assert.Equal("01", firm.BranchCode);
        Assert.Equal(new DateTime(2026, 10, 15), firm.DueDate);
        Assert.Equal(CurrencyType.Brl, firm.Currency);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal("ST-0001", firm.Code);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("noContract")]
    [InlineData("nonFinancialUsage")]
    [InlineData("noUsage")]
    [InlineData("custody")]
    [InlineData("remittance")]
    public async Task Lines_that_fail_the_rules_create_nothing(string scenario)
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(
            status: scenario == "pending" ? InvoiceStatus.Pending : InvoiceStatus.Confirmed,
            withContract: scenario != "noContract",
            usageCode: scenario == "nonFinancialUsage" ? seed.NonFinancialUsageCode : null,
            withUsage: scenario != "noUsage");
        if (scenario is "custody" or "remittance")
        {
            var tracked = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
            if (scenario == "custody") tracked.CustodyRole = StorageCustodyRole.StorageEntry;
            else tracked.ShipmentReleaseKey = Guid.NewGuid();
            await seed.Context.SaveChangesAsync();
        }

        await seed.SyncAsync(line);

        Assert.Empty(await seed.TitlesOfAsync(line, includeCanceled: true));
    }

    [Fact]
    public async Task Sapb1_mode_creates_the_firm_even_when_the_usage_has_no_flag()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync(erp: "SAPB1");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed, usageCode: seed.NonFinancialUsageCode);

        await seed.SyncAsync(line);

        Assert.Single(await seed.TitlesOfAsync(line));
    }

    [Fact]
    public async Task Syncing_twice_does_not_duplicate()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        await seed.SyncAsync(line);
        await seed.SyncAsync(line);

        Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true));
        Assert.Equal(1, seed.Numbers.NumberCalls);
    }

    // ---- Realocação (D7) ----

    [Fact]
    public async Task Ledger_split_between_two_contracts_creates_one_firm_per_contract()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.AllocateAsync(line, seed.Contract, -400m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 400m, SalesContractAllocationOrigin.Reallocation);

        await seed.SyncAsync(line, SalesFirmSyncTrigger.Reallocation);

        var titles = await seed.TitlesOfAsync(line);
        Assert.Equal(54_000m, titles.Single(t => t.SalesContractKey == seed.Contract.Key).NetAmount);
        Assert.Equal(36_000m, titles.Single(t => t.SalesContractKey == other.Key).NetAmount);
        Assert.Equal("VC-2", titles.Single(t => t.SalesContractKey == other.Key).OriginDocNumber);
    }

    [Fact]
    public async Task Reallocation_recalculates_the_loose_firm_and_logs_the_old_value()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));

        await seed.AllocateAsync(line, seed.Contract, -400m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 400m, SalesContractAllocationOrigin.Reallocation);
        await seed.SyncAsync(line, SalesFirmSyncTrigger.Reallocation);

        Assert.Equal(54_000m, (await seed.ReloadAsync(firm.Key)).NetAmount);
        var log = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().SingleAsync(l => l.FinancialDocumentKey == firm.Key);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("90.000,00", log.OldValue);
        Assert.Equal("54.000,00", log.NewValue);
    }

    [Fact]
    public async Task Whole_line_reallocated_cancels_the_loose_firm_with_the_reallocation_reason()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));

        await seed.AllocateAsync(line, seed.Contract, -1_000m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 1_000m, SalesContractAllocationOrigin.Reallocation);
        await seed.SyncAsync(line, SalesFirmSyncTrigger.Reallocation);

        var cancelled = await seed.ReloadAsync(firm.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Volume realocado para outro contrato", cancelled.CancellationReason);
        Assert.Equal(other.Key, Assert.Single(await seed.TitlesOfAsync(line)).SalesContractKey);
    }

    [Fact]
    public async Task Reallocation_of_a_composed_firm_is_refused_before_any_write()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);
        await seed.AllocateAsync(line, seed.Contract, -400m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 400m, SalesContractAllocationOrigin.Reallocation);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.Sync().EnqueueSyncAsync([line.Key!.Value], "ana", SalesFirmSyncTrigger.Reallocation));

        Assert.Equal(
            $"O título {firm.Code} da linha realocada já está composto no título FN-PROV-V1: desfaça a composição antes de realocar.",
            error.Message);
        Assert.DoesNotContain(seed.Context.ChangeTracker.Entries<FinancialDocument>(), e => e.State == EntityState.Added);
    }

    // ---- Estorno / cancelamento do documento ----

    [Fact]
    public async Task Document_leaving_confirmed_cancels_the_loose_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));

        await seed.SetStatusAsync(invoice, InvoiceStatus.Pending);
        await seed.SyncAsync(line);

        var cancelled = await seed.ReloadAsync(firm.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Documento de saída cancelado ou estornado", cancelled.CancellationReason);
        Assert.Equal("ana", cancelled.CanceledBy);
    }

    [Fact]
    public async Task Document_leaving_confirmed_with_a_composed_firm_is_refused()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);
        await seed.SetStatusAsync(invoice, InvoiceStatus.Pending);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.Sync().EnqueueSyncAsync([line.Key!.Value], "ana"));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já está composto no título FN-PROV-V1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Document_with_a_dation_on_the_firm_is_refused_on_undo()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await new FinancialDocumentsDationService(seed.Db).ExecuteAsync(firm.Key, 100m, DateTime.Today, "acordo", "ana");

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.Sync().EnsureCanUndoAsync(invoice));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Usage_that_stops_creating_financial_cancels_the_loose_firm_on_the_next_sync()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        var tracked = await seed.Context.SalesInvoicesItems.SingleAsync(i => i.Key == line.Key);
        tracked.UsageCode = seed.NonFinancialUsageCode;
        await seed.Context.SaveChangesAsync();

        await seed.SyncAsync(line);

        Assert.Equal("Natureza não gera financeiro", (await seed.ReloadAsync(firm.Key)).CancellationReason);
    }

    // ---- Devolução (D3) ----

    [Fact]
    public async Task Confirmed_return_creates_a_sales_return_that_nets_the_loose_origin_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(originLine, seed.Contract, 1_000m);
        await seed.SyncAsync(originLine);
        var firm = Assert.Single(await seed.TitlesOfAsync(originLine));
        var (_, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, status: InvoiceStatus.Confirmed,
            type: SalesInvoiceType.Return, originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");
        await seed.AllocateAsync(returnLine, seed.Contract, -100m, SalesContractAllocationOrigin.Return);

        await seed.SyncAsync(returnLine);

        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));
        Assert.Equal(FinancialDocumentNature.SalesReturn, refund.Nature);
        Assert.Equal(9_000m, refund.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, refund.Status);
        Assert.Equal(9_000m, (await seed.ReloadAsync(firm.Key)).SettledAmount);
    }

    [Fact]
    public async Task Return_against_a_composed_origin_stays_open()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(originLine, seed.Contract, 1_000m);
        await seed.SyncAsync(originLine);
        await seed.ComposeAsync(Assert.Single(await seed.TitlesOfAsync(originLine)));
        var (_, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, status: InvoiceStatus.Confirmed,
            type: SalesInvoiceType.Return, originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");
        await seed.AllocateAsync(returnLine, seed.Contract, -100m, SalesContractAllocationOrigin.Return);

        await seed.SyncAsync(returnLine);

        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));
        Assert.Equal(0m, refund.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, refund.Status);
    }

    [Fact]
    public async Task Origin_marked_returned_keeps_its_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(originLine);
        await seed.SetStatusAsync(origin, InvoiceStatus.Returned);

        await seed.SyncAsync(originLine);

        Assert.Single(await seed.TitlesOfAsync(originLine));
    }
}
```

- [ ] **Passo 3: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsSalesFirmSyncTests"`

Esperado: falha de compilação (o serviço não existe).

- [ ] **Passo 4: implementar o serviço**

`FinancialDocumentsSalesFirmSyncService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Quem pediu a sincronização: decide o texto da recusa (S1/S2 no documento, S3/S4 na realocação).</summary>
public enum SalesFirmSyncTrigger
{
    Document,
    Reallocation,
}

/// <summary>
/// Título FIRME a receber do documento de saída (spec 2026-10-15): um por LINHA × CONTRATO, derivado do ledger de
/// alocações de venda — a realocação desmembra a linha entre contratos, e o firme acompanha (D7). A cada mudança de
/// estado ou de ledger da linha, compara o DESEJADO com os títulos ATIVOS da linha: cria, recalcula (solto) ou cancela
/// (solto). Composto, ou com dação ou encontro de contas, que precisaria mudar recusa a operação inteira, ANTES de
/// qualquer escrita.
///
/// ENQUEUE-ONLY: quem chama grava. O ledger é lido junto com o que está pendente no contexto (Added entra, Deleted
/// sai), então a ordem em relação ao SaveChanges do chamador não importa. A numeração usa conexão própria (autocommit)
/// e roda dentro da transação do chamador, como a do próprio documento de saída no Expedir e Faturar: um rollback deixa
/// buraco na numeração dos títulos, aceito.
/// </summary>
public class FinancialDocumentsSalesFirmSyncService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentChangeLogService changeLog,
    FinancialDocumentsFirmSyncService firmSync,
    FinancialDocumentsNettingService netting)
{
    public const string DocumentUndoneReason = "Documento de saída cancelado ou estornado";
    public const string ReallocatedReason = "Volume realocado para outro contrato";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record Desired(Dictionary<Guid, decimal> Amounts, string CancelReason);

    private sealed record LinePlan(SalesInvoiceItem Line, Desired Desired, List<FinancialDocument> Active);

    public async Task EnqueueSyncAsync(
        IReadOnlyCollection<Guid> salesInvoiceItemKeys, string userName,
        SalesFirmSyncTrigger trigger = SalesFirmSyncTrigger.Document)
    {
        var keys = salesInvoiceItemKeys.Distinct().ToList();
        if (keys.Count == 0) return;

        var context = db.Context;
        var lines = await context.SalesInvoicesItems
            .Include(i => i.SalesInvoice)
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToListAsync();

        var plans = new List<LinePlan>();
        foreach (var line in lines)
            plans.Add(new LinePlan(line, await DesiredAsync(line), await ActiveOfAsync(line.Key!.Value)));

        // Regra: recusa ANTES de qualquer escrita — todo título que mudaria precisa estar solto.
        foreach (var plan in plans)
            foreach (var title in plan.Active)
            {
                var stays = plan.Desired.Amounts.TryGetValue(title.SalesContractKey ?? Guid.Empty, out var amount) &&
                            amount == title.NetAmount;
                if (!stays)
                    await EnsureLooseAsync(title, plan.Line.SalesInvoice!.InvoiceNumber, trigger);
            }

        foreach (var plan in plans)
            await ApplyAsync(plan, userName);
    }

    /// <summary>
    /// Só leitura (cancelamento antes da SEFAZ, estorno antes da transação): todo título ativo do documento precisa estar
    /// solto — composto recusa S1, com dação ou encontro de contas recusa S2.
    /// </summary>
    public async Task EnsureCanUndoAsync(SalesInvoice invoice)
    {
        var context = db.Context;
        var lineKeys = (await context.SalesInvoicesItems.AsNoTracking()
                .Where(i => i.SalesInvoiceKey == invoice.Key)
                .Select(i => i.Key)
                .ToListAsync())
            .Where(k => k != null)
            .Select(k => k!.Value)
            .ToList();
        if (lineKeys.Count == 0) return;

        var titles = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice &&
                        d.OriginKey != null && lineKeys.Contains(d.OriginKey.Value) &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        foreach (var title in titles)
            await EnsureLooseAsync(title, invoice.InvoiceNumber, SalesFirmSyncTrigger.Document);
    }

    /// <summary>
    /// Modelo do spec: sem volume no ledger, o valor inteiro vai para o contrato da linha; com volume, cada contrato leva
    /// a sua participação (Σ assinada ÷ total), contrato com participação ≤ 0 fica de fora, e o resíduo do arredondamento
    /// vai para a maior participação (empate: o contrato da linha). Devolução tem todos os volumes negativos: o quociente
    /// fica positivo.
    /// </summary>
    internal static Dictionary<Guid, decimal> Distribute(
        decimal value, Guid lineContractKey, IReadOnlyList<(Guid ContractKey, decimal Volume)> volumes)
    {
        var total = volumes.Sum(v => v.Volume);
        if (total == 0m || volumes.All(v => v.Volume == 0m))
            return value > 0m ? new Dictionary<Guid, decimal> { [lineContractKey] = value } : [];

        var shares = volumes
            .GroupBy(v => v.ContractKey)
            .Select(g => (ContractKey: g.Key, Share: g.Sum(v => v.Volume) / total))
            .Where(s => s.Share > 0m)
            .ToList();
        if (shares.Count == 0) return [];

        var amounts = shares.ToDictionary(
            s => s.ContractKey, s => decimal.Round(value * s.Share, 2, MidpointRounding.ToEven));

        var residue = value - amounts.Values.Sum();
        if (residue != 0m)
        {
            var target = shares
                .OrderByDescending(s => s.Share)
                .ThenByDescending(s => s.ContractKey == lineContractKey)
                .First().ContractKey;
            amounts[target] += residue;
        }

        return amounts.Where(a => a.Value > 0m).ToDictionary(a => a.Key, a => a.Value);
    }

    private async Task<Desired> DesiredAsync(SalesInvoiceItem line)
    {
        var invoice = line.SalesInvoice!;

        // G1–G3: confirmado (ou devolvido, que mantém), sem custódia nem remessa para depósito, com contrato.
        if (invoice.InvoiceStatus is not (InvoiceStatus.Confirmed or InvoiceStatus.Returned) ||
            invoice.CustodyRole != StorageCustodyRole.None || invoice.ShipmentReleaseKey != null ||
            line.SalesContractKey is null)
            return new Desired([], DocumentUndoneReason);

        // G4: "gera financeiro" da natureza (SAPB1 conta como gera).
        if (!await firmSync.CreatesFinancialDocumentAsync(line.UsageCode))
            return new Desired([], PurchaseFirmPayableRules.NatureWithoutFinancialReason);

        var volumes = await LedgerVolumesAsync(line.Key!.Value);
        return new Desired(Distribute(line.GrandTotal, line.SalesContractKey.Value, volumes), ReallocatedReason);
    }

    /// <summary>Σ do ledger por contrato: o gravado (rastreado) menos o Deleted, mais o Added ainda não gravado.</summary>
    private async Task<List<(Guid ContractKey, decimal Volume)>> LedgerVolumesAsync(Guid lineKey)
    {
        var context = db.Context;
        var saved = await context.SalesContractsAllocations
            .Where(a => a.SalesInvoiceItemKey == lineKey)
            .ToListAsync();

        var entries = context.ChangeTracker.Entries<SalesContractAllocation>()
            .Where(e => e.Entity.SalesInvoiceItemKey == lineKey)
            .ToList();
        var deleted = entries.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity).ToHashSet();
        var added = entries.Where(e => e.State == EntityState.Added).Select(e => e.Entity);

        return saved.Where(a => !deleted.Contains(a))
            .Concat(added.Where(a => !saved.Contains(a)))
            .GroupBy(a => a.SalesContractKey)
            .Select(g => (g.Key, g.Sum(a => a.Volume)))
            .ToList();
    }

    private async Task<List<FinancialDocument>> ActiveOfAsync(Guid lineKey) =>
        (await db.Context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == lineKey &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync())
        // A instância rastreada pode ter sido cancelada neste mesmo SaveChanges.
        .Where(d => d.Status != FinancialDocumentStatus.Canceled)
        .ToList();

    private async Task EnsureLooseAsync(FinancialDocument title, string? invoiceNumber, SalesFirmSyncTrigger trigger)
    {
        var context = db.Context;
        if (PurchaseFirmPayableRules.ProvisionalOf(context, title.Key) is { } provisionalKey)
        {
            var provisionalCode = await context.FinancialDocuments.AsNoTracking()
                .Where(d => d.Key == provisionalKey).Select(d => d.Code).FirstOrDefaultAsync();
            throw new DefaultException(trigger == SalesFirmSyncTrigger.Reallocation
                ? $"O título {title.Code} da linha realocada já está composto no título {provisionalCode}: desfaça a composição antes de realocar."
                : $"O título {title.Code} do documento de saída {invoiceNumber} já está composto no título {provisionalCode}: desfaça a composição antes de cancelar ou estornar o documento.");
        }

        if (title.SettledAmount != 0m)
            throw new DefaultException(trigger == SalesFirmSyncTrigger.Reallocation
                ? $"O título {title.Code} da linha realocada já tem baixa por dação ou encontro de contas: estorne-a antes de realocar."
                : $"O título {title.Code} do documento de saída {invoiceNumber} já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.");
    }

    private async Task ApplyAsync(LinePlan plan, string userName)
    {
        var context = db.Context;
        var now = DateTime.Now;

        foreach (var title in plan.Active)
        {
            if (!plan.Desired.Amounts.TryGetValue(title.SalesContractKey ?? Guid.Empty, out var amount))
            {
                FinancialDocumentsCancelService.Cancel(title, plan.Desired.CancelReason, userName);
                continue;
            }

            if (title.NetAmount == amount) continue;

            changeLog.Register(title.Key, FinancialDocumentChangeLogFields.NetAmount,
                title.NetAmount.ToString("N2", PtBr), amount.ToString("N2", PtBr), userName);
            title.NetAmount = amount;
            title.UpdatedAt = now;
            title.UpdatedBy = userName;
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, title.Key);
        }

        var existing = plan.Active.Select(t => t.SalesContractKey ?? Guid.Empty).ToHashSet();
        var missing = plan.Desired.Amounts.Keys.Where(k => !existing.Contains(k)).ToList();
        if (missing.Count == 0) return;

        var line = plan.Line;
        var invoice = line.SalesInvoice!;
        var codes = await context.SalesContracts.AsNoTracking()
            .Where(c => missing.Contains(c.Key))
            .Select(c => new { c.Key, c.Code })
            .ToDictionaryAsync(c => c.Key, c => c.Code);
        var nature = invoice.InvoiceType == SalesInvoiceType.Return
            ? FinancialDocumentNature.SalesReturn
            : FinancialDocumentNature.Firm;
        var issueDate = (invoice.InvoiceDate ?? DateTime.Today).Date;
        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);

        foreach (var contractKey in missing)
        {
            var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
            var code = await docNumberSequence.GetDocNumber(docNumberKey);

            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = code,
                DocNumberKey = docNumberKey,
                BranchCode = invoice.BranchCode,
                Direction = FinancialDirection.Receivable,
                Nature = nature,
                Status = FinancialDocumentStatus.Open,
                CardCode = invoice.CardCode,
                CardName = invoice.CardName,
                // Firme não é cobrado direto: o vencimento que vale é o do provisório.
                DocumentDate = issueDate,
                DueDate = issueDate,
                Currency = CurrencyType.Brl,
                NetAmount = plan.Desired.Amounts[contractKey],
                OriginType = FinancialDocumentOrigin.SalesInvoice,
                OriginKey = line.Key,
                OriginDocNumber = codes.GetValueOrDefault(contractKey),
                SalesContractKey = contractKey,
                SalesInvoiceKey = invoice.Key,
                TaxDocumentNumber = taxDocument,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName,
            };

            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);

            // Regra 9: a devolução nascida compensa o firme solto da linha de origem, no mesmo contrato.
            if (nature == FinancialDocumentNature.SalesReturn)
                await netting.EnqueueAutoForSalesAsync(document, line.SalesInvoiceItemOriginKey, userName);
        }
    }
}
```

> Se `SalesInvoiceItem.Key` não for `Guid?`, ajuste o `Where(i => i.Key != null && keys.Contains(i.Key.Value))` para `keys.Contains(i.Key)`. A grafia precisa seguir o tipo real.
>
> Se `changeLog.Register` tiver outra assinatura, use a de `FinancialDocumentsFirmSyncService.EnqueueSyncAsync`.

No DI (`ServiceCollectionExtensions.cs`), logo abaixo de `services.AddScoped<FinancialDocumentsFirmSyncService>();`:

```csharp
        services.AddScoped<FinancialDocumentsSalesFirmSyncService>();
```

- [ ] **Passo 5: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsSalesFirmSyncTests|FullyQualifiedName~FinancialSalesReceivableServicesTests"`

Esperado: PASS.

- [ ] **Passo 6: build e suíte**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
```

Esperado: tudo verde.

- [ ] **Passo 7: stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsSalesFirmSyncService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs SiagroB1.Application.Tests/Financials/SalesFirmReceivableSeed.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesFirmSyncTests.cs
```

O controlador commita com:

```
feat(financial): sincronização do título firme a receber por linha e contrato

O firme a receber nasce, muda e morre com a linha do documento de saída:
um por linha × contrato, com o valor da linha distribuído pelas
participações do ledger de alocações (a realocação desmembra). Composto
ou com dação/encontro de contas, o que precisaria mudar recusa antes de
qualquer escrita. A devolução nasce como SalesReturn e compensa o firme
solto da linha de origem.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 4: Ganchos no documento de saída — confirmar, Expedir e Faturar, estornar, cancelar

**Arquivos:**

| Ação | Caminho |
|---|---|
| Modificar | `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs` |
| Modificar | `SiagroB1.Application/Services/ShipmentBilling/ShipmentBillingCreateSalesInvoiceService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs` |
| Modificar | `SiagroB1.Application.Tests/Financials/SalesFirmReceivableSeed.cs` (fábricas `Confirm()`, `ReverseConfirm()`, `Cancel()`) |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/SalesFirmReceivableHooksTests.cs` |
| Teste (modificar) | `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadBillingServiceTests.cs` (um teste novo + `Service()` aceitando o sync) |

**Interfaces:**

- Consome (Task 3): `EnqueueSyncAsync`, `EnsureCanUndoAsync`.
- Produz: o parâmetro opcional `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null`, **o último**, nos quatro construtores.

- [ ] **Passo 1: fábricas no seed**

Acrescente a `SalesFirmReceivableSeed`, com os `using` de `SiagroB1.Application.Services.SalesContracts`, `.SalesInvoices`, `.SalesShipmentReleases`, `.ShipmentLoads`, `.ShipmentReleases` e `SiagroB1.Commons.Resources`:

```csharp
    public SalesInvoicesConfirmService Confirm() => new(Db,
        new SalesShipmentReleasesRecalculateShippedService(Context),
        new SalesContractsAllocationCreateService(Db, new SalesContractsFixedVolumeService(Context)),
        new SalesContractsAllocationCreateForReturnService(Db, new SalesContractsFixedVolumeService(Context)),
        new SalesInvoicesUsageGuardService(new UsageService(Db, NullLogger<UsageService>.Instance)),
        new SalesContractsAllocationCreateForFiscalAdjustmentService(Db, new SalesContractsFixedVolumeService(Context)),
        new ShipmentLoadsBalanceHookService(Context, new ShipmentLoadsMovementLogService(Context)),
        new ShipmentLoadsClosureHookService(Context, new ShipmentLoadsChangeLogService(Context)),
        new FakeStringLocalizer<Resource>(),
        salesFirmSync: Sync());

    public SalesInvoicesReverseConfirmService ReverseConfirm() => new(Db,
        new SalesContractsAllocationDeleteForInvoiceService(Db),
        new ShipmentReleasesRecalculateShippedService(Context),
        new ShipmentLoadsBalanceHookService(Context, new ShipmentLoadsMovementLogService(Context)),
        new ShipmentLoadsClosureHookService(Context, new ShipmentLoadsChangeLogService(Context)),
        new FakeStringLocalizer<Resource>(),
        salesFirmSync: Sync());

    public SalesInvoicesCancelService Cancel() => new(Db,
        new SalesShipmentReleasesRecalculateShippedService(Context),
        new SalesContractsAllocationDeleteForInvoiceService(Db),
        new ShipmentLoadsBalanceHookService(Context, new ShipmentLoadsMovementLogService(Context)),
        NullLogger<SalesInvoicesCancelService>.Instance,
        salesFirmSync: Sync());

    /// <summary>Documento avulso Pendente confirmado pelo serviço REAL (ledger FiscalAdjustment + firme).</summary>
    public async Task<(SalesInvoice Invoice, SalesInvoiceItem Line)> ConfirmedAsync(
        decimal quantity = 1_000m, decimal unitPrice = 90m, string number = "DS000001")
    {
        var (invoice, line) = await AddInvoiceAsync(quantity, unitPrice, number: number);
        await Confirm().ExecuteAsync(invoice.Key, "ana");
        return (invoice, line);
    }
```

- [ ] **Passo 2: escrever os testes que falham**

`SalesFirmReceivableHooksTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15, pontos de chamada 1, 3 e 4: confirmar, estornar e cancelar o documento de saída.</summary>
public class SalesFirmReceivableHooksTests
{
    [Fact]
    public async Task Confirming_an_avulso_document_creates_the_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();

        var (_, line) = await seed.ConfirmedAsync();

        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        Assert.Equal(SalesFirmReceivableSeed.DefaultLineAmount, firm.NetAmount);
    }

    [Fact]
    public async Task Reversing_the_confirmation_cancels_the_loose_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();

        await seed.ReverseConfirm().ExecuteAsync(invoice.Key, "bia");

        var firm = Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true));
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Documento de saída cancelado ou estornado", firm.CancellationReason);
    }

    [Fact]
    public async Task Reversing_with_a_composed_firm_is_refused_and_the_document_stays_confirmed()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);

        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.ReverseConfirm().ExecuteAsync(invoice.Key, "bia"));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já está composto no título FN-PROV-V1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
        var saved = await seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
    }

    [Fact]
    public async Task Reconfirming_after_reversal_creates_a_new_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();
        await seed.ReverseConfirm().ExecuteAsync(invoice.Key, "bia");

        await seed.Confirm().ExecuteAsync(invoice.Key, "ana");

        var titles = await seed.TitlesOfAsync(line, includeCanceled: true);
        Assert.Equal(2, titles.Count);
        Assert.Single(titles, t => t.Status != FinancialDocumentStatus.Canceled);
    }

    [Fact]
    public async Task Cancelling_the_document_cancels_the_loose_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();

        await seed.Cancel().ExecuteAsync(invoice.Key, "bia");

        Assert.Equal(FinancialDocumentStatus.Canceled,
            Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true)).Status);
    }

    [Fact]
    public async Task Pre_sefaz_dry_run_refuses_a_document_with_a_composed_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);

        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Cancel().EnsureCanCancelAsync(invoice.Key));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já está composto no título FN-PROV-V1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Confirming_a_return_creates_the_sales_return_netted_against_the_origin()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(originLine));
        var (returnInvoice, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, type: SalesInvoiceType.Return,
            originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");

        await seed.Confirm().ExecuteAsync(returnInvoice.Key, "ana");

        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));
        Assert.Equal(FinancialDocumentNature.SalesReturn, refund.Nature);
        Assert.Equal(9_000m, refund.NetAmount);
        Assert.Equal(9_000m, (await seed.ReloadAsync(firm.Key)).SettledAmount);
    }

    [Fact]
    public async Task Reversing_a_return_that_netted_the_origin_is_refused()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.ConfirmedAsync();
        var (returnInvoice, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, type: SalesInvoiceType.Return,
            originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");
        await seed.Confirm().ExecuteAsync(returnInvoice.Key, "ana");
        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.ReverseConfirm().ExecuteAsync(returnInvoice.Key, "bia"));

        Assert.Equal(
            $"O título {refund.Code} do documento de saída DS000002 já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.",
            error.Message);
    }
}
```

> Se a confirmação avulsa recusar a natureza do seed (guard de natureza), complete o `Usage(...)` do seed com o que o `SalesInvoicesUsageGuardService` exigir. O modelo está em `SalesInvoicesFiscalAdjustmentTests`, que confirma avulsos com `ContractBalanceEffect.Consume`.
>
> Se o estorno de uma devolução avulsa passar por um ramo (legado/novo) que exige romaneio, troque o último teste pelo cancelamento da devolução com NF-e (`IsNfeReturn`), que vai pelo cancelamento. Registre a troca no relatório.

Em `ShipmentLoadBillingServiceTests`:

- `Service(...)` ganha o parâmetro `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null`, repassado como último argumento do construtor.
- Acrescente o teste:

```csharp
    [Fact]
    public async Task Billing_the_load_creates_the_firm_receivable()
    {
        var (load, contract, release, _) = await SeedAsync();
        var usage = await new UsageService(_db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            CreatesFinancialDocument = true,
        });
        var invoice = InvoiceFor(load, contract, release, 40_000m);
        invoice.Items.Single().UsageCode = usage.Code;

        await Service(salesFirmSync: FinancialDocumentTestServices.SalesFirmSync(_db)).ExecuteAsync(invoice, "tester");

        var firm = await _db.Context.FinancialDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialDocumentNature.Firm, firm.Nature);
        Assert.Equal(3_600_000m, firm.NetAmount);
        Assert.Equal(contract.Key, firm.SalesContractKey);
        Assert.Equal(invoice.Key, firm.SalesInvoiceKey);
    }
```

Acrescente os `using` de `SiagroB1.Application.Services.Financials`, `SiagroB1.Domain.Models` e `SiagroB1.Application.Tests.Support`.

- [ ] **Passo 3: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesFirmReceivableHooksTests|FullyQualifiedName~ShipmentLoadBillingServiceTests"`

Esperado: falha de compilação (`salesFirmSync` não existe nos construtores).

- [ ] **Passo 4: implementar os ganchos**

**`SalesInvoicesConfirmService`.**

- Último parâmetro do construtor, depois de `refusalComplete`: `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null`. Acrescente `using SiagroB1.Application.Services.Financials;`.
- No `try`, logo depois do primeiro `await db.SaveChangesAsync();`, o que vem depois de `invoice.InvoiceStatus = InvoiceStatus.Confirmed;` e do recálculo do devolvido:

```csharp
            // Spec 2026-10-15 (ponto 1): firmes a receber das linhas. Depois do SaveChanges acima — o ledger já está
            // gravado — e na mesma transação: uma recusa desfaz a confirmação.
            if (salesFirmSync is not null)
            {
                await salesFirmSync.EnqueueSyncAsync(
                    invoice.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList(), userName);
                await db.SaveChangesAsync();
            }
```

**`ShipmentBillingCreateSalesInvoiceService`.** Último parâmetro do construtor, depois do `logger`: `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null`. Logo depois de `await recalcShipped.RecalculateAsync(releaseKey);`:

```csharp
            // Spec 2026-10-15 (ponto 2): este caminho grava Confirmed direto, sem o SalesInvoicesConfirmService.
            if (salesFirmSync is not null)
            {
                await salesFirmSync.EnqueueSyncAsync(
                    salesInvoice.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList(), username);
                await db.SaveChangesAsync();
            }
```

**`SalesInvoicesReverseConfirmService`.** Último parâmetro: `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null`.

Antes de `await db.BeginTransactionAsync();`:

```csharp
        // Spec 2026-10-15: firme composto, ou com dação/encontro de contas, recusa antes de mexer em qualquer coisa.
        if (salesFirmSync is not null)
            await salesFirmSync.EnsureCanUndoAsync(invoice);
```

Logo depois do `await db.SaveChangesAsync();` que segue `allocationDelete.ExecuteAsync(...)`:

```csharp
            // Spec 2026-10-15 (ponto 3): o documento voltou a Pendente — os títulos soltos das linhas são cancelados.
            if (salesFirmSync is not null)
            {
                await salesFirmSync.EnqueueSyncAsync(
                    invoice.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList(), userName);
                await db.SaveChangesAsync();
            }
```

**`SalesInvoicesCancelService`.** Último parâmetro do construtor, depois de `refusalComplete`: `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null`.

No fim de `EnsureBusinessRulesAsync`:

```csharp
        // Spec 2026-10-15 (ponto 4): roda no ensaio antes da SEFAZ e no cancelamento.
        if (salesFirmSync is not null)
            await salesFirmSync.EnsureCanUndoAsync(invoice);
```

No `CancelAsync`, logo depois do primeiro `await db.SaveChangesAsync();` do `try`, o que vem depois de `SalesInvoicesRecalculateReturnedService`:

```csharp
            // Spec 2026-10-15 (ponto 4): documento cancelado — os títulos soltos das linhas são cancelados.
            if (salesFirmSync is not null)
            {
                var lineKeys = await db.Context.SalesInvoicesItems.AsNoTracking()
                    .Where(i => i.SalesInvoiceKey == existingInvoice.Key)
                    .Select(i => i.Key)
                    .ToListAsync();
                await salesFirmSync.EnqueueSyncAsync(
                    lineKeys.Where(k => k != null).Select(k => k!.Value).ToList(), userName);
                await db.SaveChangesAsync();
            }
```

(O cancelamento carrega o documento sem `Items`, por isso busca as chaves.)

- [ ] **Passo 5: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesFirmReceivableHooksTests|FullyQualifiedName~ShipmentLoadBillingServiceTests|FullyQualifiedName~SalesInvoices|FullyQualifiedName~ShipmentLoads|FullyQualifiedName~Nfe"`

Esperado: PASS, com a regressão das suítes de saída intacta.

- [ ] **Passo 6: suíte**

`dotnet test SiagroB1.Application.Tests`: todos verdes.

- [ ] **Passo 7: stage**

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs SiagroB1.Application/Services/ShipmentBilling/ShipmentBillingCreateSalesInvoiceService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs SiagroB1.Application.Tests/Financials/SalesFirmReceivableSeed.cs SiagroB1.Application.Tests/Financials/SalesFirmReceivableHooksTests.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadBillingServiceTests.cs
```

O controlador commita com:

```
feat(financial): documento de saída confirmado gera o firme a receber

Confirmar (inclusive a devolução e a autorização da NF-e de devolução),
Expedir e Faturar, estornar a confirmação e cancelar sincronizam os
títulos das linhas. Firme composto ou com dação/encontro de contas
recusa o estorno e o cancelamento antes da transação e antes da SEFAZ.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 5: Realocação acompanha (D7)

**Arquivos:**

| Ação | Caminho |
|---|---|
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsReallocationCreateService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsReallocationDeleteService.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/SalesFirmReceivableReallocationTests.cs` |

**Interfaces:**

- Consome (Task 3): `EnqueueSyncAsync(..., SalesFirmSyncTrigger.Reallocation)`.
- Produz: o parâmetro opcional `FinancialDocumentsSalesFirmSyncService? salesFirmSync = null` como último do construtor nos dois serviços.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15 D7: realocar desmembra o firme solto; desfazer junta de volta; composto recusa.</summary>
public class SalesFirmReceivableReallocationTests
{
    private static SalesContractsReallocationCreateService Create(SalesFirmReceivableSeed seed) =>
        new(seed.Db, new SalesShipmentReleaseMovementGuardService(seed.Context), salesFirmSync: seed.Sync());

    private static SalesContractsReallocationDeleteService Delete(SalesFirmReceivableSeed seed) =>
        new(seed.Db, new TestLogger<SalesContractsReallocationDeleteService>(), salesFirmSync: seed.Sync());

    [Fact]
    public async Task Reallocating_part_of_the_line_splits_the_firm_and_undoing_joins_it_back()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.ConfirmedAsync();

        await Create(seed).ExecuteWithTransactionAsync(line.Key!.Value, seed.Contract.Key, other.Key, null, 400m, "ana");

        var titles = await seed.TitlesOfAsync(line);
        Assert.Equal(54_000m, titles.Single(t => t.SalesContractKey == seed.Contract.Key).NetAmount);
        Assert.Equal(36_000m, titles.Single(t => t.SalesContractKey == other.Key).NetAmount);

        var pairLine = await seed.Context.SalesContractsAllocations.AsNoTracking()
            .FirstAsync(a => a.Origin == SalesContractAllocationOrigin.Reallocation);
        await Delete(seed).ExecuteWithTransactionAsync(pairLine.Key, "ana");

        var after = await seed.TitlesOfAsync(line);
        Assert.Equal(90_000m, Assert.Single(after).NetAmount);
        Assert.Equal(seed.Contract.Key, after.Single().SalesContractKey);
    }

    [Fact]
    public async Task Reallocating_a_composed_firm_is_refused_and_nothing_is_written()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(seed).ExecuteWithTransactionAsync(line.Key!.Value, seed.Contract.Key, other.Key, null, 400m, "ana"));

        Assert.Equal(
            $"O título {firm.Code} da linha realocada já está composto no título FN-PROV-V1: desfaça a composição antes de realocar.",
            error.Message);
        Assert.False(await seed.Context.SalesContractsAllocations.AsNoTracking()
            .AnyAsync(a => a.Origin == SalesContractAllocationOrigin.Reallocation));
        Assert.Single(await seed.TitlesOfAsync(line));
    }
}
```

> `ExecuteWithTransactionAsync` envolve a exceção em `DefaultException(e.Message)`, então a mensagem chega igual. A transação do InMemory não desfaz nada, mas a recusa vem antes das escritas. O teste confere justamente isso.
>
> Se o contrato de destino recusar por saldo ("Saldo do contrato de destino insuficiente"), dê `TotalVolume` e o `AvaiableVolume` adequado ao `VC-2` no seed: `AvaiableVolume` é persistido. Defina-o explicitamente.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesFirmReceivableReallocationTests"`

Esperado: falha de compilação (`salesFirmSync`).

- [ ] **Passo 3: implementar**

**`SalesContractsReallocationCreateService`.**

- Construtor: `(IUnitOfWork db, SalesShipmentReleaseMovementGuardService movementGuard, FinancialDocumentsSalesFirmSyncService? salesFirmSync = null)`.
- No fim de `ExecuteAsync`, **antes** de `if (commitMode == CommitMode.Auto) await db.SaveChangesAsync();`:

```csharp
        // Spec 2026-10-15 D7: o firme a receber acompanha o volume. A sincronização lê o ledger com as linhas ainda
        // pendentes no contexto e recusa (composto/baixado) antes de qualquer escrita.
        if (salesFirmSync is not null)
            await salesFirmSync.EnqueueSyncAsync([salesInvoiceItemKey], userName, SalesFirmSyncTrigger.Reallocation);
```

Acrescente `using SiagroB1.Application.Services.Financials;`.

**`SalesContractsReallocationDeleteService`.**

- Construtor: `(IUnitOfWork db, ILogger<SalesContractsReallocationDeleteService> logger, FinancialDocumentsSalesFirmSyncService? salesFirmSync = null)`.
- Antes do `await db.SaveChangesAsync();` final de `ExecuteAsync`:

```csharp
        // Spec 2026-10-15 D7: desfazer a realocação junta o firme de volta (as linhas removidas já estão Deleted).
        if (salesFirmSync is not null)
            await salesFirmSync.EnqueueSyncAsync([itemKey], userName, SalesFirmSyncTrigger.Reallocation);
```

**Atenção à ordem na criação:** a recusa precisa vir **antes** de qualquer escrita. A chamada fica depois do `AddRangeAsync(pending)`, mas a sincronização recusa antes de mexer nos títulos. Nenhum `SaveChanges` acontece antes dela, e o rollback da transação descarta as alocações pendentes.

O teste `Reallocating_a_composed_firm_is_refused_and_nothing_is_written` só passa se o `ExecuteWithTransactionAsync` não gravar nada. Com InMemory, o `RollbackAsync` não limpa o ChangeTracker, mas também não houve `SaveChanges`. A asserção lê com `AsNoTracking`, ou seja, do "banco".

- [ ] **Passo 4: rodar e ver passar, com a regressão da realocação**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesFirmReceivableReallocationTests|FullyQualifiedName~SalesContractsReallocation|FullyQualifiedName~SalesContractsReconciliation"`

Esperado: PASS.

- [ ] **Passo 5: stage**

```bash
git add SiagroB1.Application/Services/SalesContracts/SalesContractsReallocationCreateService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsReallocationDeleteService.cs SiagroB1.Application.Tests/Financials/SalesFirmReceivableReallocationTests.cs
```

O controlador commita com:

```
feat(sales-contract): realocar desmembra o firme a receber da linha

Realocar volume faturado entre contratos de venda leva junto a parte do
firme a receber (ao preço faturado); desfazer a realocação junta de
volta. Firme composto ou com dação/encontro de contas recusa a
realocação antes de qualquer escrita.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 6: Contrato de venda — encerrar com tudo quitado, corte legado, estorno de fixação

**Arquivos:**

| Ação | Caminho |
|---|---|
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsCloseService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationsCancelService.cs` |
| Teste (modificar) | `SiagroB1.Application.Tests/SalesContracts/SalesContractsCloseFixationGuardTests.cs`, `SalesContractsCloseNegativeBalanceGuardTests.cs` (construção) |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/SalesContractsCloseFinancialTests.cs` |

**Interfaces:**

- Produz:
  - construtor `SalesContractsCloseService(AppDbContext context, SalesContractsFixedVolumeService fixedVolumeService, ContractNotificationOutboxService notificationOutbox, IConfiguration configuration, ILogger<SalesContractsCloseService> logger)`;
  - `public const string CutoverDateKey = "Financial:SalesFirmReceivablesCutoverDate";`.
- O `FinancialDocumentsCancelService` sai do construtor: o encerramento não cancela mais nada além do legado, que usa o `Cancel` estático.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15, regras 15–18: encerrar contrato de venda só com tudo quitado; corte legado; estorno de fixação.</summary>
public class SalesContractsCloseFinancialTests
{
    private static SalesContractsCloseService Close(SalesFirmReceivableSeed seed, string? cutover = null) =>
        new(seed.Context, new SalesContractsFixedVolumeService(seed.Context), TestNotificationOutbox.For(seed.Context),
            TestConfiguration.With((SalesContractsCloseService.CutoverDateKey, cutover)),
            NullLogger<SalesContractsCloseService>.Instance);

    [Fact]
    public async Task Open_firm_blocks_the_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        await seed.ConfirmedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal("Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.", error.Message);
    }

    [Fact]
    public async Task Unsettled_provisional_blocks_the_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync(provisionalAmount: 90_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal("O título FN-PROV-V1 não está quitado: o contrato só encerra com todos os títulos quitados.", error.Message);
    }

    [Fact]
    public async Task Fully_received_contract_closes_and_keeps_the_provisional()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync(provisionalAmount: 90_000m);
        var (_, line) = await seed.ConfirmedAsync();
        await seed.ComposeAsync(Assert.Single(await seed.TitlesOfAsync(line)));
        await new FinancialDocumentsSettleService(seed.Db)
            .ExecuteAsync(seed.Provisional.Key, "CX01", 90_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished,
            (await seed.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == seed.Contract.Key)).Status);
        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
    }

    [Fact]
    public async Task Untouched_provisional_before_the_cutover_is_cancelled_on_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var legacy = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        legacy.CreatedAt = new DateTime(2026, 9, 1);
        await seed.Context.SaveChangesAsync();

        await Close(seed, cutover: "2026-10-15").ExecuteAsync(seed.Contract.Key, "ana");

        var cancelled = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Encerramento do contrato", cancelled.CancellationReason);
    }

    [Fact]
    public async Task Without_the_cutover_key_nothing_is_exempt()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var legacy = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        legacy.CreatedAt = new DateTime(2026, 9, 1);
        await seed.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));
    }

    [Fact]
    public async Task Advance_with_credit_blocks_the_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var tracked = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        tracked.CreatedAt = new DateTime(2026, 9, 1);
        seed.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN-ADV-V1", CardCode = "C0001", Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Advance, Status = FinancialDocumentStatus.Settled, NetAmount = 1_000m,
            SettledAmount = 1_000m, AppliedAmount = 0m, DueDate = new DateTime(2026, 12, 31),
            OriginType = FinancialDocumentOrigin.Manual, SalesContractKey = seed.Contract.Key, Currency = CurrencyType.Brl,
        });
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Close(seed, cutover: "2026-10-15").ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(
            "O adiantamento FN-ADV-V1 tem 1.000,00 de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.",
            error.Message);
    }
}
```

O estorno de fixação ganha um teste no mesmo arquivo. Construa o serviço como em `SalesContractsPriceFixationsCancel*Tests` (procure com `grep -rn "SalesContractsPriceFixationsCancelService(" SiagroB1.Application.Tests`) e semeie:

- uma fixação `Confirmed` do contrato;
- o provisório com `OriginType = SalesContractPriceFixation`, `OriginKey = fixation.Key` e `ComposedAmount = 100m`.

```csharp
    [Fact]
    public async Task Reversing_a_fixation_whose_provisional_is_composed_is_refused()
    {
        // ... seed (fixação Confirmed + provisório dela com ComposedAmount 100) ...
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "ana"));
        Assert.Equal($"Desfaça a composição do título {provisional.Code} antes de estornar a fixação.", error.Message);
    }
```

O implementador completa o seed a partir do teste vizinho que já constrói esse serviço. É o único ponto aberto desta task, e precisa ser registrado no relatório.

Nos dois testes antigos (`SalesContractsCloseFixationGuardTests` e `SalesContractsCloseNegativeBalanceGuardTests`), a construção passa a ser:

```csharp
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context), TestNotificationOutbox.For(_db.Context),
            TestConfiguration.Empty(), NullLogger<SalesContractsCloseService>.Instance);
```

Se algum desses testes semear um provisório aberto e esperar o encerramento passar, ele agora recusa (S10). Ajuste o teste para semear o provisório quitado, ou nenhum. Registre no relatório.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsCloseFinancialTests"`

Esperado: falha de compilação (construtor e `CutoverDateKey`).

- [ ] **Passo 3: implementar**

**`SalesContractsCloseService`.**

- Construtor e campos: troque `FinancialDocumentsCancelService financialDocuments` por `IConfiguration configuration, ILogger<SalesContractsCloseService> logger`. Acrescente os `using` `System.Globalization`, `Microsoft.Extensions.Configuration` e `Microsoft.Extensions.Logging`.
- Logo no começo da classe:

```csharp
    /// <summary>Spec 2026-10-15 (regra 16): data de corte PRÓPRIA da venda (yyyy-MM-dd) — o deploy da venda tem outra data.</summary>
    public const string CutoverDateKey = "Financial:SalesFirmReceivablesCutoverDate";
```

O `ExecuteAsync` passa a ser:

```csharp
        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        await GuardFinancialDocumentsAsync(contract, userName);

        await GuardAdvanceCreditAsync(contract);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Closed, userName);

        // Spec 2026-10-15 (regra 15): o encerramento NÃO cancela mais o provisório — ele só passa com tudo quitado.
        await context.SaveChangesAsync();
```

O bloco `financialDocuments.EnqueueCancelByContractAsync(...)` e o comentário dele são removidos.

Métodos novos (espelho de `PurchaseContractsCloseService`, linhas 57–154):

```csharp
    /// <summary>Regra 15: nenhum firme/devolução de venda com saldo, e todo provisório não cancelado QUITADO.</summary>
    private async Task GuardFinancialDocumentsAsync(SalesContract contract, string userName)
    {
        var hasOpenFirm = await context.FinancialDocuments.AnyAsync(x =>
            x.SalesContractKey == contract.Key &&
            x.OriginType == FinancialDocumentOrigin.SalesInvoice &&
            (x.Nature == FinancialDocumentNature.Firm || x.Nature == FinancialDocumentNature.SalesReturn) &&
            x.Status != FinancialDocumentStatus.Canceled &&
            x.NetAmount > x.SettledAmount);

        if (hasOpenFirm)
            throw new ApplicationException(
                "Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.");

        await CancelLegacyUntouchedProvisionalsAsync(contract, userName);

        var unsettled = await context.FinancialDocuments
            .Where(x => x.SalesContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.Status != FinancialDocumentStatus.Settled)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Key, x.Code })
            .ToListAsync();
        // O SQL não vê o cancelamento do legado acima, ainda só em memória: reconfere pela instância rastreada.
        var unsettledCode = unsettled
            .Where(x => context.FinancialDocuments.Local.FirstOrDefault(d => d.Key == x.Key) is not { Status: FinancialDocumentStatus.Canceled })
            .Select(x => x.Code)
            .FirstOrDefault();

        if (unsettledCode is not null)
            throw new ApplicationException(
                $"O título {unsettledCode} não está quitado: o contrato só encerra com todos os títulos quitados.");
    }

    /// <summary>Regra 15: adiantamento de venda com crédito não aplicado barra o encerramento (mesmo texto da compra).</summary>
    private async Task GuardAdvanceCreditAsync(SalesContract contract)
    {
        var advance = await context.FinancialDocuments.AsNoTracking()
            .Where(x => x.SalesContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Advance &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.SettledAmount - x.AppliedAmount > 0m)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.SettledAmount, x.AppliedAmount })
            .FirstOrDefaultAsync();

        if (advance is null)
            return;

        var credit = decimal.Round(advance.SettledAmount - advance.AppliedAmount, 2, MidpointRounding.ToEven);
        throw new ApplicationException(
            $"O adiantamento {advance.Code} tem {credit.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.");
    }

    /// <summary>
    /// Regra 16: provisório criado ANTES da data de corte, sem composição e sem baixa, é cancelado no encerramento como
    /// antes desta fase. Sem a chave (ou inválida), nada é isento.
    /// </summary>
    private async Task CancelLegacyUntouchedProvisionalsAsync(SalesContract contract, string userName)
    {
        var raw = configuration[CutoverDateKey];
        if (string.IsNullOrWhiteSpace(raw))
            return;

        if (!DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cutover))
        {
            logger.LogWarning("{Key} inválida ({Value}): nenhum provisório é isento no encerramento.", CutoverDateKey, raw);
            return;
        }

        var legacy = await context.FinancialDocuments
            .Where(x => x.SalesContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.Status != FinancialDocumentStatus.Settled &&
                        x.CreatedAt != null && x.CreatedAt < cutover &&
                        x.ComposedAmount == 0m && x.SettledAmount == 0m)
            .ToListAsync();

        foreach (var provisional in legacy)
        {
            FinancialDocumentsCancelService.Cancel(provisional, PurchaseContractsCloseService.LegacyCancellationReason, userName);
            logger.LogInformation(
                "Encerramento do contrato de venda {Contract}: provisório {Document} anterior à data de corte {Cutover:yyyy-MM-dd} cancelado.",
                contract.Code, provisional.Code, cutover);
        }
    }
```

Acrescente `using SiagroB1.Application.Services.PurchaseContracts;` para a constante. Se `CreatedAt` não for anulável, tire o `x.CreatedAt != null &&`, como na compra.

**`SalesContractsPriceFixationsCancelService.ExecuteAsync`.** Logo depois da checagem "Contrato precisa estar aprovado…", antes de `BeginTransactionAsync`:

```csharp
        // Spec 2026-10-15 (regra 18): o provisório desta fixação com composição ou baixa barra o estorno. Desfaz-se a
        // composição primeiro — os firmes não são apagados, voltam a ficar soltos.
        var provisional = await context.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.SalesContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (provisional is not null && (provisional.ComposedAmount != 0m || provisional.SettledAmount != 0m))
            throw new ApplicationException(
                $"Desfaça a composição do título {provisional.Code} antes de estornar a fixação.");
```

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsCloseFinancialTests|FullyQualifiedName~SalesContracts"`

Esperado: PASS. O build também precisa passar: o DI resolve `IConfiguration` e `ILogger<>` sozinho.

- [ ] **Passo 5: suíte**

`dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`: todos verdes.

- [ ] **Passo 6: stage**

```bash
git add SiagroB1.Application/Services/SalesContracts/SalesContractsCloseService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationsCancelService.cs SiagroB1.Application.Tests/SalesContracts/SalesContractsCloseFixationGuardTests.cs SiagroB1.Application.Tests/SalesContracts/SalesContractsCloseNegativeBalanceGuardTests.cs SiagroB1.Application.Tests/Financials/SalesContractsCloseFinancialTests.cs
```

O controlador commita com:

```
feat(sales-contract): encerrar contrato de venda exige títulos quitados

O encerramento deixa de cancelar o provisório a receber: recusa com
firme ou devolução em aberto, provisório não quitado ou adiantamento com
crédito. O provisório legado intocado (anterior a
Financial:SalesFirmReceivablesCutoverDate) é cancelado como antes.
Estornar a fixação recusa provisório composto ou baixado.

Atenção: no deploy, configurar Financial:SalesFirmReceivablesCutoverDate
no Web; sem ela, todo contrato de venda com provisório aberto deixa de
encerrar.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 7: Backlog dos documentos de saída já confirmados (regra 19)

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Domain/Dtos/SalesFirmBacklogResultDto.cs` |
| Criar | `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateSalesFirmBacklogService.cs` |
| Criar | `SiagroB1.Web/Actions/Financials/FinancialDocumentsGenerateSalesFirmBacklogController.cs` |
| Modificar | `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, depois do bloco `FinancialDocumentsGenerateBacklog` (~linha 1710) |
| Modificar | `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/FinancialDocumentsGenerateSalesFirmBacklogTests.cs` |

**Interfaces:**

- `FinancialDocumentsGenerateSalesFirmBacklogService(IUnitOfWork db, FinancialDocumentsSalesFirmSyncService sync)`.
- `Task<SalesFirmBacklogResultDto> ExecuteAsync(string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName)`.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15 regra 19: firmes dos documentos de saída confirmados antes do deploy.</summary>
public class FinancialDocumentsGenerateSalesFirmBacklogTests
{
    private static FinancialDocumentsGenerateSalesFirmBacklogService Service(SalesFirmReceivableSeed seed) =>
        new(seed.Db, seed.Sync());

    private static readonly DateTime From = new(2026, 10, 1);
    private static readonly DateTime To = new(2026, 10, 31);

    [Fact]
    public async Task Dry_run_counts_the_lines_without_firm_and_writes_nothing()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        var result = await Service(seed).ExecuteAsync(null, From, To, dryRun: true, "ana");

        Assert.True(result.DryRun);
        Assert.Equal(1, result.EligibleLines);
        Assert.Equal(1, result.LinesWithoutFirm);
        Assert.Equal(0, result.Generated);
        Assert.False(await seed.Context.FinancialDocuments.AnyAsync(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice));
    }

    [Fact]
    public async Task Run_generates_the_missing_firms_and_is_idempotent()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        var first = await Service(seed).ExecuteAsync(null, From, To, dryRun: false, "ana");
        var second = await Service(seed).ExecuteAsync(null, From, To, dryRun: false, "ana");

        Assert.Equal(1, first.Generated);
        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.SkippedAlreadyGenerated);
        Assert.Single(await seed.TitlesOfAsync(line));
    }

    [Fact]
    public async Task Lines_outside_the_window_branch_or_of_finished_contracts_are_ignored()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var finished = await seed.AddContractAsync("VC-9");
        var tracked = await seed.Context.SalesContracts.SingleAsync(c => c.Key == finished.Key);
        tracked.Status = ContractStatus.Finished;
        await seed.Context.SaveChangesAsync();
        await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed, contract: finished);
        var (outside, _) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed, number: "DS000009");
        var trackedInvoice = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == outside.Key);
        trackedInvoice.InvoiceDate = new DateTime(2026, 9, 30);
        await seed.Context.SaveChangesAsync();

        var result = await Service(seed).ExecuteAsync("01", From, To, dryRun: false, "ana");

        Assert.Equal(0, result.EligibleLines);
    }

    [Fact]
    public void Edm_has_the_backlog_action()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.Contains(model.SchemaElements, e => e.Name == "FinancialDocumentsGenerateSalesFirmBacklog");
    }
}
```

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsGenerateSalesFirmBacklogTests"`

Esperado: falha de compilação.

- [ ] **Passo 3: implementar**

`SalesFirmBacklogResultDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>Resultado do backlog dos firmes a receber (spec 2026-10-15, regra 19).</summary>
public class SalesFirmBacklogResultDto
{
    [JsonPropertyName("DryRun")] public bool DryRun { get; set; }

    /// <summary>Linhas confirmadas da janela, de contrato aprovado.</summary>
    [JsonPropertyName("EligibleLines")] public int EligibleLines { get; set; }

    /// <summary>Elegíveis sem nenhum título ativo — as que a execução sincroniza.</summary>
    [JsonPropertyName("LinesWithoutFirm")] public int LinesWithoutFirm { get; set; }

    /// <summary>Títulos criados (uma linha realocada gera mais de um).</summary>
    [JsonPropertyName("Generated")] public int Generated { get; set; }

    [JsonPropertyName("SkippedAlreadyGenerated")] public int SkippedAlreadyGenerated { get; set; }
}
```

`FinancialDocumentsGenerateSalesFirmBacklogService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Spec 2026-10-15, regra 19: documento de saída confirmado ANTES do deploy não tem firme, e sem ele o contrato em
/// andamento não consegue quitar o provisório. Mesmo molde de <see cref="FinancialDocumentsGenerateBacklogService"/>:
/// deliberado, por filial e janela, com DryRun. Só linhas SEM título ativo são sincronizadas, uma por SaveChanges, na
/// ordem do documento (a origem ganha firme antes da devolução que a compensa). Fora de transação.
/// </summary>
public class FinancialDocumentsGenerateSalesFirmBacklogService(IUnitOfWork db, FinancialDocumentsSalesFirmSyncService sync)
{
    public async Task<SalesFirmBacklogResultDto> ExecuteAsync(
        string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName)
    {
        var context = db.Context;
        var result = new SalesFirmBacklogResultDto { DryRun = dryRun };

        var lines = await context.SalesInvoicesItems.AsNoTracking()
            .Where(i => (i.SalesInvoice!.InvoiceStatus == InvoiceStatus.Confirmed ||
                         i.SalesInvoice.InvoiceStatus == InvoiceStatus.Returned) &&
                        i.SalesInvoice.InvoiceDate >= fromDate && i.SalesInvoice.InvoiceDate <= toDate &&
                        (branchCode == null || i.SalesInvoice.BranchCode == branchCode) &&
                        i.SalesContractKey != null && i.SalesContract!.Status == ContractStatus.Approved)
            .OrderBy(i => i.SalesInvoice!.InvoiceDate).ThenBy(i => i.SalesInvoice!.InvoiceNumber)
            .Select(i => i.Key)
            .ToListAsync();

        foreach (var key in lines.Where(k => k != null).Select(k => k!.Value))
        {
            result.EligibleLines++;

            var hasTitle = await context.FinancialDocuments.AnyAsync(d =>
                d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == key &&
                d.Status != FinancialDocumentStatus.Canceled);
            if (hasTitle)
            {
                result.SkippedAlreadyGenerated++;
                continue;
            }

            result.LinesWithoutFirm++;
            if (dryRun) continue;

            await sync.EnqueueSyncAsync([key], userName);
            result.Generated += context.ChangeTracker.Entries<FinancialDocument>().Count(e => e.State == EntityState.Added);
            await db.SaveChangesAsync();
        }

        return result;
    }
}
```

O controller copia o molde de `FinancialDocumentsGenerateBacklogController`, com rota `odata/FinancialDocumentsGenerateSalesFirmBacklog` e o serviço novo:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsGenerateSalesFirmBacklogController(
    FinancialDocumentsGenerateSalesFirmBacklogService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsGenerateSalesFirmBacklog")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("FromDate", out var fromObj) || fromObj is null)
                return BadRequest("Informe a data inicial.");

            if (!parameters.TryGetValue("ToDate", out var toObj) || toObj is null)
                return BadRequest("Informe a data final.");

            // Sem DryRun explícito, simula: gerar por engano é o erro caro aqui.
            var dryRun = !parameters.TryGetValue("DryRun", out var dryObj)
                         || dryObj is null
                         || Convert.ToBoolean(dryObj);

            parameters.TryGetValue("BranchCode", out var branchObj);

            var result = await service.ExecuteAsync(
                branchCode: branchObj?.ToString(),
                fromDate: DateTime.Parse(fromObj.ToString()!),
                toDate: DateTime.Parse(toObj.ToString()!),
                dryRun: dryRun,
                userName: User.Identity?.Name ?? "Unknown");

            return Ok(result);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`ODataConfigurations.cs`, logo depois do bloco do `financialBacklog`:

```csharp
        // Spec 2026-10-15 (regra 19): firmes a receber dos documentos de saída confirmados antes do deploy.
        var salesFirmBacklog = modelBuilder.Action("FinancialDocumentsGenerateSalesFirmBacklog");
        salesFirmBacklog.Parameter<string>("BranchCode").Optional();
        salesFirmBacklog.Parameter<string>("FromDate");
        salesFirmBacklog.Parameter<string>("ToDate");
        salesFirmBacklog.Parameter<bool>("DryRun");
        salesFirmBacklog.Returns<SalesFirmBacklogResultDto>();
```

DI: `services.AddScoped<FinancialDocumentsGenerateSalesFirmBacklogService>();`, junto do `FinancialDocumentsGenerateBacklogService`.

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsGenerateSalesFirmBacklogTests|FullyQualifiedName~FinancialEdmModelTests"`

Esperado: PASS.

- [ ] **Passo 5: build e suíte**

`dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`: todos verdes.

- [ ] **Passo 6: stage**

```bash
git add SiagroB1.Domain/Dtos/SalesFirmBacklogResultDto.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateSalesFirmBacklogService.cs SiagroB1.Web/Actions/Financials/FinancialDocumentsGenerateSalesFirmBacklogController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsGenerateSalesFirmBacklogTests.cs
```

O controlador commita com:

```
feat(financial): backlog dos firmes a receber de documentos já confirmados

Documento de saída confirmado antes do deploy não tem firme, e sem ele o
contrato em andamento não quita o provisório. A action
FinancialDocumentsGenerateSalesFirmBacklog sincroniza, por filial e
janela, as linhas sem título (DryRun por padrão, idempotente).

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 8 (frontend): título — devolução de venda, documento de saída, dação/compensação e filtros do Receber

**Arquivos (em `C:\Projetos\SiagroB1\siagro-b1-frontend\webapp`):**

| Ação | Caminho |
|---|---|
| Modificar | `model/formatter.ts` (`formatFinancialNature`, `isSalesFirmReceivable` novo, `formatFinancialDocumentCancelable`) |
| Modificar | `helpers/FinancialDocumentFilterHelpers.ts` (`pendingResolutionFilter(direction)`) |
| Modificar | `controller/financialDocuments/FinancialDocumentsBaseController.ts` (`setPayablesFilters`, filtro e mapa do Excel) |
| Modificar | `controller/accountsReceivable/Main.controller.ts` |
| Modificar | `controller/accountsPayable/Main.controller.ts`, se ele chamar `setPayablesFilters(true)` (manter o comportamento) |
| Modificar | `view/financialDocuments/fragments/Filterbar.fragment.xml` |
| Modificar | `view/financialDocuments/fragments/Settlements.fragment.xml` |
| Modificar | `view/financialDocuments/fragments/Form.fragment.xml` |
| Modificar | `controller/financialDocuments/Detail.controller.ts` (`onOpenSalesInvoice`) |
| Teste (modificar) | `test/unit/helpers/FinancialDocumentFilterHelpers.qunit.ts`, `test/unit/model/formatter.qunit.ts` |

**Interfaces:**

- `pendingResolutionFilter(direction: "Payable" | "Receivable" = "Payable"): string`.
- `formatter.isSalesFirmReceivable(nature?: string, originType?: string): boolean`.
- `setPayablesFilters(payables: boolean, returnNature = "PurchaseReturn", returnText = "Devolução")`.

- [ ] **Passo 1: testes QUnit que falham**

Em `FinancialDocumentFilterHelpers.qunit.ts`, acrescente:

```ts
QUnit.test("Receber: firme e devolução de venda do documento de saída, não cancelados, com saldo", function (assert) {
	assert.strictEqual(
		pendingResolutionFilter("Receivable"),
		"OriginType eq 'SalesInvoice'"
		+ " and (Nature eq 'Firm' or Nature eq 'SalesReturn')"
		+ " and Status ne 'Canceled' and NetAmount gt SettledAmount");
});
```

O teste antigo, sem argumento, continua igual: `Payable` é o padrão.

Em `formatter.qunit.ts`, perto das linhas 91–97:

```ts
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "Firm", "SalesInvoice", "0"), false);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "SalesReturn", "SalesInvoice", "0"), false);
	assert.strictEqual(formatter.isSalesFirmReceivable("Firm", "SalesInvoice"), true);
	assert.strictEqual(formatter.isSalesFirmReceivable("Firm", "PurchaseInvoice"), false);
	assert.strictEqual(formatter.formatFinancialNature("SalesReturn"), "Devolução de venda");
```

- [ ] **Passo 2: implementar**

**`FinancialDocumentFilterHelpers.ts`:**

```ts
/**
 * "Com saldo a resolver": firme ou devolução que ainda não foi toda composta, compensada ou baixada por dação.
 * Pagar (spec 2026-10-13): conciliação fiscal ou complemento de valor (D11, origem PurchaseInvoice). Receber
 * (spec 2026-10-15): documento de saída. OpenAmount é [NotMapped] e não vai ao SQL, então a comparação é pelas
 * colunas reais. A origem exclui o título a receber do washout (também Firm).
 */
export function pendingResolutionFilter(direction: "Payable" | "Receivable" = "Payable"): string {
	if (direction === "Receivable") {
		return "OriginType eq 'SalesInvoice'"
			+ " and (Nature eq 'Firm' or Nature eq 'SalesReturn')"
			+ " and Status ne 'Canceled' and NetAmount gt SettledAmount";
	}
	return "(OriginType eq 'PurchaseFiscalReconciliation' or OriginType eq 'PurchaseInvoice')"
		+ " and (Nature eq 'Firm' or Nature eq 'PurchaseReturn')"
		+ " and Status ne 'Canceled' and NetAmount gt SettledAmount";
}
```

**`formatter.ts`:**

- Em `formatFinancialNature`, acrescente `m.set("SalesReturn", "Devolução de venda");`.
- Depois de `isPurchaseFirmPayable`:

```ts
  /** Firme ou devolução nascidos do documento de saída (spec 2026-10-15): sai só por composição, dação ou encontro de contas. */
  isSalesFirmReceivable: (nature?: string, originType?: string) =>
    (nature === "Firm" || nature === "SalesReturn") && originType === "SalesInvoice",
```

- Em `formatFinancialDocumentCancelable`, troque `&& !formatter.isPurchaseFirmPayable(nature, originType)` por `&& !formatter.isPurchaseFirmPayable(nature, originType) && !formatter.isSalesFirmReceivable(nature, originType)`.

**`FinancialDocumentsBaseController.ts`:**

```ts
  /**
   * Flags lidas pela barra de filtros compartilhada (`pf>`): Natureza firme/devolução e "Com saldo a resolver" existem
   * no Pagar e no Receber (spec 2026-10-15); o item de devolução muda de chave e texto conforme o lado. O modelo precisa
   * existir nas três listas, senão o binding de `visible` não resolve e o item aparece em todas.
   */
  protected setPayablesFilters(payables: boolean, returnNature = "PurchaseReturn", returnText = "Devolução"): void {
    this.getView().setModel(new JSONModel({ payables, returnNature, returnText }), "pf");
  }
```

- No `applyFilters`, o ramo `PendingResolution` vira `filters.push(\`(${pendingResolutionFilter(this.direction === "Receivable" ? "Receivable" : "Payable")})\`);`.
- No mapa do Excel (~linha 176), acrescente `"SalesReturn": "Devolução de venda",`.

**`accountsReceivable/Main.controller.ts`:** `this.setPayablesFilters(true, "SalesReturn", "Devolução de venda");`.

**`Filterbar.fragment.xml`:**

- No grupo `naturePayable`, troque `<core:ListItem key="PurchaseReturn" text="Devolução"/>` por `<core:ListItem key="{pf>/returnNature}" text="{pf>/returnText}"/>`.
- Atualize o comentário acima para: `Pagar e Receber: Firme e Devolução existem nos dois lados; a chave e o texto da devolução vêm do modelo pf.` Sem `--` no comentário.

**`Settlements.fragment.xml`:**

- Botão "Baixar por dação": o trecho das origens vira `(${path: 'OriginType', targetType: 'any'} === 'PurchaseFiscalReconciliation' || ${path: 'OriginType', targetType: 'any'} === 'PurchaseInvoice' || ${path: 'OriginType', targetType: 'any'} === 'SalesInvoice')`.
- Botão "Compensar com firme": `(${path: 'Nature', targetType: 'any'} === 'PurchaseReturn' || ${path: 'Nature', targetType: 'any'} === 'SalesReturn')`.
- O `&&` continua escapado como `&amp;&amp;`.

**`Form.fragment.xml`:** depois do `FormContainer` "Documento fiscal" (o de compra), acrescente:

```xml
      <f:FormContainer title="Documento de saída"
        visible="{= ${path: 'OriginType', targetType: 'any'} === 'SalesInvoice' }">
        <f:formElements>
          <f:FormElement label="NF">
            <f:fields><Text text="{TaxDocumentNumber}"/></f:fields>
          </f:FormElement>
          <f:FormElement label="Documento de saída">
            <f:fields>
              <Link text="Abrir documento de saída" enabled="{= !!${path: 'SalesInvoiceKey', targetType: 'any'} }" press=".onOpenSalesInvoice"/>
            </f:fields>
          </f:FormElement>
          <f:FormElement label="Composto em" visible="{= !!${composition>/composedInKey} }">
            <f:fields>
              <Link text="{composition>/composedInCode}" press=".onOpenComposedIn"/>
            </f:fields>
          </f:FormElement>
        </f:formElements>
      </f:FormContainer>
```

**`financialDocuments/Detail.controller.ts`:** depois de `onOpenPurchaseInvoice`:

```ts
  onOpenSalesInvoice(): void {
    const key = this.documentContext()?.getProperty("SalesInvoiceKey") as string;
    if (key) {
      this.navTo("salesInvoicesDetail", { id: key });
    }
  }
```

> Se o controller carrega a composição (`composition>/composedInKey`) só para origens de compra, procure a condição (`grep -n "composedIn" controller/financialDocuments/Detail.controller.ts`) e inclua `SalesInvoice`.

- [ ] **Passo 3: gates**

Na raiz do frontend, com o dev server parado:

```bash
python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" webapp/view/financialDocuments/fragments/Filterbar.fragment.xml webapp/view/financialDocuments/fragments/Settlements.fragment.xml webapp/view/financialDocuments/fragments/Form.fragment.xml
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Esperado: parse sem saída e os três gates limpos.

QUnit pelo navegador, com `yarn start`: `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests`. Todos verdes. Pare o dev server depois, **por PID**.

- [ ] **Passo 4: stage**

```bash
git -C C:/Projetos/SiagroB1/siagro-b1-frontend add webapp/model/formatter.ts webapp/helpers/FinancialDocumentFilterHelpers.ts webapp/controller/financialDocuments webapp/controller/accountsReceivable/Main.controller.ts webapp/controller/accountsPayable/Main.controller.ts webapp/view/financialDocuments/fragments webapp/test/unit
```

O controlador commita com:

```
feat(financial): título a receber mostra o documento de saída e aceita dação

Na tela do título, o firme a receber ganha o bloco do documento de saída
(com link), "Baixar por dação" e, na devolução de venda, "Compensar com
firme". A lista de Contas a Receber ganha os filtros Natureza (com
"Devolução de venda") e "Com saldo a resolver".

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 9 (frontend): seção "Financeiro" no contrato de venda

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `webapp/view/salesContracts/fragments/SalesContractFinancialDocuments.fragment.xml` |
| Modificar | `webapp/view/salesContracts/Detail.view.xml` (seção nova depois de "Entregas") |
| Modificar | `webapp/controller/salesContracts/Detail.controller.ts` |

- [ ] **Passo 1: fragmento**

Copie `view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml` para o caminho novo e:

- tire a coluna e a célula "Romaneio" (`StorageTransactionCode`);
- mude o comentário de abertura para: `Seção Financeiro do contrato de venda (spec 2026-10-15, firmes a receber): provisórios, firmes, devoluções de venda e adiantamentos, pela function FinancialDocumentsGetByContract, no JSONModel "contractFinancial". Somente leitura. A coluna NF mostra TaxDocumentNumber, que traz o número interno quando o documento não tem NF.` Sem `--`.

Rode `git add` no fragmento novo.

- [ ] **Passo 2: view**

Em `Detail.view.xml`, logo depois do `</uxap:ObjectPageSection>` da seção "Entregas", copie a estrutura da seção "Financeiro" da compra (`view/purchaseContracts/Detail.view.xml:259-266`, com as mesmas `subSections`/`blocks`) e troque o fragmento:

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Financeiro">
        <uxap:subSections>
          <uxap:ObjectPageSubSection>
            <uxap:blocks>
              <core:Fragment fragmentName="siagrob1.view.salesContracts.fragments.SalesContractFinancialDocuments" type="XML" />
            </uxap:blocks>
          </uxap:ObjectPageSubSection>
        </uxap:subSections>
      </uxap:ObjectPageSection>
```

Siga a estrutura exata da compra. Se ela não usar `subSections`/`blocks` desse jeito, copie a dela.

- [ ] **Passo 3: controller**

Em `salesContracts/Detail.controller.ts`, acrescente os imports:

```ts
import { Link$PressEvent } from "sap/m/Link";
import { odataCollection, sendJson } from "siagrob1/helpers/FetchHelpers";
import { FinancialDocumentByContract } from "siagrob1/types/FinancialDocumentByContract";
```

Campo da classe: `private _financialContractKey: string;`.

No `onInit`, antes de `initContractDrafts()`: `this.getView().setModel(new JSONModel({ rows: [] }), "contractFinancial");`.

No `detailRouteMatched`, junto de `this.getInvoices(id);`: `void this.loadContractFinancial(id);`.

Métodos:

```ts
  /** Seção "Financeiro" (spec 2026-10-15, firmes a receber): provisórios, firmes, devoluções e adiantamentos do contrato. */
  private async loadContractFinancial(contractKey: string): Promise<void> {
    this._financialContractKey = contractKey;
    const model = this.getModel("contractFinancial") as JSONModel;
    model.setData({ rows: [] });

    const result = await sendJson("GET",
      `${this.api.financialDocumentsGetByContract}(ContractType='Sales',ContractKey=${contractKey})`);
    if (contractKey !== this._financialContractKey) return;

    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    model.setData({ rows: odataCollection<FinancialDocumentByContract>(result.data) });
  }

  onOpenContractFinancialDocument(ev: Link$PressEvent): void {
    const row = ev.getSource().getBindingContext("contractFinancial").getObject() as FinancialDocumentByContract;
    this.navTo("financialDocumentsDetail", { id: row.Key });
  }
```

> Se `this.api` não existir no `SalesContractsBaseController`, use o mesmo acesso que a compra usa (procure `financialDocumentsGetByContract` em `controller/purchaseContracts`).
>
> Se a seção precisar recarregar ao voltar do título, siga o que a compra faz. O padrão é recarregar no `detailRouteMatched`, e isso já cobre.

- [ ] **Passo 4: gates**

```bash
python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" webapp/view/salesContracts/Detail.view.xml webapp/view/salesContracts/fragments/SalesContractFinancialDocuments.fragment.xml
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Esperado: tudo limpo.

- [ ] **Passo 5: stage**

```bash
git -C C:/Projetos/SiagroB1/siagro-b1-frontend add webapp/view/salesContracts/fragments/SalesContractFinancialDocuments.fragment.xml webapp/view/salesContracts/Detail.view.xml webapp/controller/salesContracts/Detail.controller.ts
```

O controlador commita com:

```
feat(financial): seção Financeiro no contrato de venda

O contrato de venda passa a mostrar, como o de compra, o provisório, os
firmes e devoluções de venda e os adiantamentos, com link para o título.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 10: Verificação no navegador (CEAGUI dev)

**Feita pelo controlador.** Não há código novo; uma correção que surgir vira commit próprio na task de origem.

- [ ] **Passo 1: migration no CEAGUI dev**

Confira o ambiente e aplique:

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

O `list` mostra `AddSalesFirmReceivables` como pendente e a connection string aponta `CEAGUI_SIAGRO_DEV`, sem expor a senha. Só então rode o `update`.

- [ ] **Passo 2: subir a stack**

Conforme a memória `run-stack-locally`: profile `ceagui` no Web e no Gateway, `yarn start:dev` no frontend.

- [ ] **Passo 3: roteiro (spec, "Navegador")**

1. Contrato de venda aprovado com provisório a receber: **Expedir e Faturar** uma carga.
   - O título firme aparece na seção Financeiro do contrato.
   - O título mostra "Documento de saída", com o link funcionando.
2. No provisório: **Compor** o firme e **Baixar** no provisório. O disponível para baixa acompanha.
3. Firme com sobra: **Baixar por dação**.
4. Devolução do cliente (Recusa de carga ou devolução avulsa): o título de devolução de venda nasce compensado com o firme solto. Se o firme estiver composto, a devolução fica em aberto e entra negativa na composição.
5. Conciliação de saldos de venda: **realocar** parte de uma nota. O firme desmembra. Com firme composto, a realocação recusa com S3.
6. **Estornar a confirmação** de um documento com firme composto: recusa com S1.
7. **Encerrar** o contrato com provisório não quitado: recusa com S10. Quitado: encerra.
8. Contas a Receber: filtro Natureza "Devolução de venda" e "Com saldo a resolver".

- [ ] **Passo 4: limpeza**

- Desfaça ou cancele o que foi criado só para teste, quando a tela permitir.
- Derrube os processos **por PID** e confira as portas de novo.
- Anote no ledger os códigos criados (títulos, documentos).

---

## Autorrevisão

**Cobertura do spec:**

| Regra do spec | Task |
|---|---|
| Modelo e índice | 1 |
| G1–G4 e regras 1–2 | 3 |
| Regra 3 | 4 |
| Regra 4 | 2 |
| Regras 5–6 | 5 |
| Regras 7–11 | 2 e 3 |
| Regras 12–14 | 2 |
| Regras 15–18 | 6 (a 17 não muda código: o gerador já é idempotente) |
| Regra 19 | 7 |
| Regras 20–24 | 8 e 9 |
| Mensagens S1–S12 | todas nas Restrições globais e nos testes |
| Roteiro do navegador | 10 |

**Tipos:** `SalesFirmSyncTrigger`, `EnqueueSyncAsync(keys, user, trigger)`, `EnsureCanUndoAsync(invoice)`, `Distribute(...)`, `EnqueueAutoForSalesAsync(...)` e `HasSameContractAs(...)` têm o mesmo nome e a mesma assinatura em todas as tasks.

**Pontos que o implementador pode ter de completar (registrados, não placeholders):**

- a natureza de saída aceita pelo guard de natureza (Task 4);
- o seed do estorno de fixação a partir do teste vizinho (Task 6);
- o `AvaiableVolume` do contrato de destino na realocação (Task 5).

Os três dependem do estado exato de cadastros que os testes vizinhos já montam.
