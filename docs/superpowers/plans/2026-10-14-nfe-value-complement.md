# NF-e própria de complemento de valor (documento de entrada) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No modo NF-e (STANDALONE, regra ativa, Emissão Própria), o Documento de Entrada do tipo Complemento de valor passa a ser emitido como NF-e complementar nossa (finNFe=2): cada linha aponta um item de uma entrada própria autorizada do mesmo produtor e filial, herda produto/unidade/natureza (e o contrato, quando o original tem; sem contrato no original, o operador escolhe — D5), e o `NFref` do cabeçalho leva as chaves distintas das originais; a original não é cancelada enquanto houver complemento ativo, e o complemento não tem "Devolver".

**Architecture:** O vínculo mora na LINHA (`PURCHASE_INVOICE_ITEMS.ComplementedItemKey`, auto-relação Restrict). A regra única das linhas (`PurchaseInvoiceValueComplement.EnsureAsync`, já chamada pelos quatro caminhos que gravam linha e pela confirmação) ganha C1–C5/C7/C10, herda os campos do original na linha nova (decidido pelo estado do EF: Detached/Added herda, rastreada recusa divergência) e, DEPOIS de C3, aplica a guarda de contrato existente ao contrato escolhido pelo operador (original sem contrato). A emissão reaproveita a da entrada própria: os três bloqueios atuais (tipo emitível, tributação, regra da linha) passam a aceitar o complemento próprio; a prontidão recarrega as originais (C6) e devolve as chaves para o montador, que escolhe `NfePurpose.Complement` → `FinalidadeNFe.fnComplementar`. A tela ganha a function `PurchaseInvoicesGetComplementCandidates` e um diálogo de seleção próprio (JSONModel).

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), ASP.NET Core OData v4, Zeus.Net.NFe.NFCe, xUnit; OpenUI5 1.141 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-14-nfe-value-complement-design.md` (autoridade; decisões D1–D5, regras 1–14 e 5a, mensagens C1–C10, commit `165355a`). Contexto: `2026-10-13-purchase-firm-payables-design.md` (D10/D11: complemento de valor de terceiro, firme na confirmação).

## Global Constraints

- Dois repositórios: `C:\Projetos\SiagroB1\siagro-b1-backend` (Tasks 1–6) no branch **`feature/nfe-value-complement`** (já existe, criado de `main` `0945a53`; HEAD `165355a` = spec revisado, D5) e `C:\Projetos\SiagroB1\siagro-b1-frontend` (Tasks 7–8) no branch **`feature/nfe-value-complement`**, criado a partir de `main` (`889d2da`) na Task 7. `git branch --show-current` antes de cada `git add`. **Nunca push. Nunca merge.**
- Todo arquivo novo: `git add <path>` logo depois de criado, no repo dono. O último passo de cada task é **stage** (`git add`) — o **controlador** commita com a mensagem dada (o `git commit` de subagente é bloqueado nesta sessão).
- Mensagem de commit: `tipo(escopo): descrição pt-BR, imperativo, minúscula, sem ponto`; escopo **`invoice`**; corpo com o PORQUÊ; trailer `DB: AddPurchaseInvoiceItemComplementedItem` **só** no commit da Task 1 (a única migration); terminar com `Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>`, onde `{MODELO}` é substituído pelo script do controlador pelo nome do modelo que de fato escreveu o código da task (sem repetir "Claude").
- Identificadores em inglês; texto ao usuário (mensagens, rótulos) em pt-BR; comentários em pt-BR.
- Modo alvo: `STANDALONE` com a regra de tributação ativa na filial (`TaxCalculationGate`) e `IssuerType = Own`. Em `SAPB1` nada muda; o complemento de **terceiro** continua como está (sem vínculo, sem emissão).
- Modelo: `PURCHASE_INVOICE_ITEMS.ComplementedItemKey UNIQUEIDENTIFIER NULL`, FK → `PURCHASE_INVOICE_ITEMS(Key)`, índice não único, **`DeleteBehavior.Restrict`** (o spec diz "NoAction": no SQL Server os dois geram o mesmo DDL `ON DELETE NO ACTION`, mas o EF grava a diferença no snapshot; as auto-relações vizinhas `PurchaseInvoiceItemOriginKey` e `ProducerInvoiceItemKey` são `Restrict`, então esta também — ver memória `restrict-vs-noaction-not-interchangeable-in-ef`). Migration `AddPurchaseInvoiceItemComplementedItem`, sem backfill. Exposta no EDM por ser persistida (**sem** `AddProperty`).
- Mensagens EXATAS (copiar literalmente):
  - **Do spec:**
    - C1 `Item {item}: o complemento de valor de emissão própria exige o item da nota original.`
    - C2 `Item {item}: a nota original {número} precisa ser uma entrada própria normal, com NF-e autorizada e não cancelada.`
    - C3 `Item {item}: a nota original {número} é de outro produtor ou de outra filial.`
    - C4 `O item {número da nota original}/{nº do item} já está complementado nesta nota.`
    - C5 `Item {item}: produto, unidade e natureza vêm do item da nota original e não podem ser alterados.`
    - C6 `A nota original {número} não está mais autorizada: o complemento não pode ser emitido.`
    - C7 `O item da nota original só existe no complemento de valor de emissão própria.`
    - C8 `A nota {número} tem o complemento {número do complemento} ativo: cancele o complemento antes.`
    - C9 `Complemento de valor não tem devolução.`
    - C10 `Item {item}: o contrato vem do item da nota original e não pode ser alterado.`
    - Tela: `Informe o fornecedor antes de incluir itens.`
    - Existentes, reaproveitadas no complemento com original SEM contrato (D5, regra 5a): `Item {ItemCode}: o complemento de valor exige o contrato de compra na linha.` (`PurchaseInvoiceValueComplement.cs:44`) e as quatro de `PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync` (`PurchaseInvoiceLineGuard.cs:108-139`: não encontrado / não aprovado nem encerrado / outro fornecedor / outro produto).
  - **Preenchimento decidido neste plano:** `{número}` = `PurchaseInvoice.InvoiceNumber` (DE000001 — o mesmo das mensagens da contra-nota, `PurchaseInvoiceCounterNote.cs:31`); `{nº do item}` = nItem da original (`NfeItemNumbering.OriginNumber`); `{item}` em C2–C5 e C10 = `ItemCode` do **item original** (a linha nova ainda não herdou); em C1 = `ItemCode` da linha ou `(sem produto)` quando vazio.
  - **Texto decidido neste plano (o spec não deu):** diálogo `Item da nota original` (título) e `Nenhuma entrada própria autorizada deste fornecedor nesta filial.` (lista vazia); coluna `Nota original`.
- Armadilhas conhecidas (valem para todas as tasks):
  - `ODataActionParameters` pode chegar **nulo**; parâmetro de rota por atributo chega **com as aspas** do OData (`'F001'`) — `Trim('\'')` (como `BranchNfeSettingsGetController.cs:16`).
  - DTO de function: `[JsonPropertyName("PascalCase")]` em **toda** propriedade (sem isso sai camelCase e a tela lê `undefined`); dinheiro/quantidade em `double` (o UI5 lê `Edm.Decimal` como string).
  - `Edm.Decimal` chega como **string** no frontend: `Number(x)` antes de comparar/somar.
  - Enum em expression binding: `${path: 'X', targetType: 'any'}` (sem ele estoura `FormatException`); vale também para `!${…}` sobre propriedade não booleana.
  - `--` dentro de comentário XML mata o fragmento inteiro sem erro em gate nenhum — **não** escrever `--` em `<!-- -->`.
  - XML de view/fragmento **tem de ser parseado** em toda task que o altera (`ts-typecheck`/`lint` não validam XML):
    `python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" <arquivos>` — saída vazia = OK.
  - EF InMemory não confere tradução de LINQ nem FK: consultas novas usam só comparações simples e navegações (sem método .NET dentro do `Where`), e os testes conferem o efeito.
  - `yarn test` inteiro **nunca passa** (gate de cobertura irreal): gates do frontend = `yarn ts-typecheck && yarn lint && yarn ui5lint`; QUnit pelo navegador só se a task pedir.
  - Migrations: **só** `dotnet ef migrations add` (não toca banco). **Nunca** `dotnet ef database update` fora da Task 9, e lá só com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito e conferido (`CEAGUI_SIAGRO_DEV`); nunca o profile `db-migration`. Migrations antigas não são reescritas.
  - Processos (Web/Gateway/dev server): matar **só por PID** (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`). Nunca `taskkill /IM node.exe`/`dotnet.exe`. Conferir as portas de novo no fim (o file watcher ressuscita processo).
- Comandos de teste (raiz do backend): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`; `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~<Classe>"`. Frontend (raiz, dev server **parado**): `yarn ts-typecheck && yarn lint && yarn ui5lint`; QUnit unit pelo navegador com `yarn start` em `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests`.

## Review Focus

1. **A original é cancelada (ou vira outra coisa) entre o rascunho e a transmissão** — o operador espera a recusa C6 antes de qualquer número ser reservado ou XML enviado, nunca um complemento autorizado referenciando NF-e cancelada. Teste `Cancelled_original_blocks_the_emission` (Task 5).
2. **O operador troca o produtor ou a filial do cabeçalho depois de incluir linhas** — espera C3 (a mensagem do complemento), e não a do contrato de outro fornecedor que a guarda de contrato do `SyncItems` daria antes. Testes `Changing_the_supplier_after_including_lines_is_refused` e `Changing_the_branch_after_including_lines_is_refused` (Task 2).
3. **O mesmo item original incluído de novo linha a linha (grade do Editar → `ItemsCreate`)** — espera C4, igual ao deep-insert. Teste `Same_original_item_twice_in_one_note_is_refused` (Task 2, os dois caminhos).
4. **Item original sem contrato** (é o caso de TODA entrada própria autorizada da CEAGUI dev hoje, inclusive a série 9 nº 5 — conferido em 14/10; D5) — o diálogo o oferece com o contrato vazio, o operador escolhe o contrato pelo value help e ele passa pelas regras de contrato de sempre (outro fornecedor/produto recusado), continua obrigatório, e pode ser trocado depois sem C10; trocar o produtor do cabeçalho com esse contrato ainda cai em C3, não na mensagem do contrato. Testes `Items_without_contract_are_offered_with_an_empty_contract` (Task 3) e `Original_without_contract_takes_the_contract_chosen_by_the_operator`, `Chosen_contract_goes_through_the_contract_rules`, `Chosen_contract_can_be_changed_on_update`, `Changing_the_supplier_with_a_chosen_contract_is_refused_by_C3` (Task 2).
5. **Original que não é entrada própria Normal** (outro complemento, devolução, terceiro, pendente) — C2; e a **contra-nota** (própria Normal com `ProducerInvoiceKey`) é aceita (D1). Testes `Original_must_be_an_authorized_own_normal_entry` (Task 2) e `Counter_note_is_offered` (Task 3).

## Decisões do plano (lacunas do spec)

- **Herdar × recusar (regra 5):** a regra decide pelo estado do EF da linha: `Detached` (Create, ItemsCreate, confirmação com `AsNoTracking`) ou `Added` (linha nova do `SyncItems`) **herda** do original; rastreada (`Unchanged`/`Modified`: PATCH de linha, linha existente do PATCH do cabeçalho) **recusa** divergência (C5). Herdar numa linha `AsNoTracking` não grava nada — só valida.
- **Item original inexistente** (chave que não existe) cai em C1 (a linha não aponta um item válido); C2 exige um documento para citar.
- **Ordem na linha do complemento próprio:** C1 (todas as linhas) → por linha: C2 → C3 → C4 → herda/C5 → contrato (herdado/C10, ou o do operador pela guarda de contrato existente, D5) → regras existentes (quantidade/preço 0, valor > 0, contrato obrigatório, natureza com "Gera financeiro"). C7 roda antes de tudo, em qualquer documento.
- **Guarda de contrato da linha do complemento próprio (D5 / regra 5a):** os quatro caminhos (`PurchaseInvoicesCreateService:101`, `PurchaseInvoicesItemsCreateService:47`, `PurchaseInvoicesItemsUpdateService:50`, `SyncItems` do `PurchaseInvoicesUpdateService:207/238`) deixam de chamar `EnsureContractIsCompatibleAsync` para linha com `ComplementedItemKey`; quem chama é a própria regra do complemento, **depois** de C3 e da herança, só para o contrato escolhido pelo operador (original sem contrato), com o produto já herdado e o produtor do cabeçalho. Assim nenhuma validação de contrato é pulada, e C3 é sempre a primeira mensagem quando o produtor/filial do cabeçalho muda (Review Focus 2). O contrato herdado não passa pela guarda: é o da original, e C3 garante o mesmo produtor. `EnsureContractIsCompatibleAsync` ganha uma sobrecarga com `AppDbContext` (a regra só tem o contexto).
- **Candidatos:** todos os itens das originais elegíveis, **com ou sem contrato** (D5: `PurchaseContractKey`/`PurchaseContractCode` nulos), ordenados por data de emissão decrescente e nItem. O DTO leva `UsageName` além dos campos do spec (a coluna Natureza da grade mostra o nome).
- **Prontidão:** C6 é recusa direta (não entra na lista "Faltam dados…"). O complemento **não exige pesos** (regra 8; o montador já omite `vol` com peso 0 — `NfeXmlBuilder.cs:524`); condição de pagamento continua exigida.
- **Ordem no cancelamento da original:** C8 roda **antes** da recusa "Documento de entrada possui devolução." — a série 9 nº 5 da CEAGUI dev tem a devolução DE000002 (nº 6, Rejeitada, Pendente) ainda ativa, e o roteiro do spec (passo 4) espera ver C8 na tela.
- **Emitir é confirmar:** no modo NF-e o Documento de Entrada próprio só confirma emitindo (`PurchaseInvoicesConfirmService.cs:43-45`); o "Confirmar → transmitir" do roteiro do spec é o mesmo clique "Emitir NF-e", e o firme nasce na confirmação feita pelo retorno da SEFAZ.
- **C9** vale para os dois emissores e é a primeira checagem do "Devolver".
- **Diálogo da tela:** `openTableSelectDialog` só serve a `ODataListBinding` com contexto do modelo padrão; os candidatos vêm de uma function, então o diálogo é um `TableSelectDialog` próprio sobre JSONModel (precedente: `ownershipTransfers/BaseController.ts:186-235`), com o mesmo contrato (resolve `undefined` ao cancelar).
- **Contrato na linha nova:** a célula "Contrato" mostra uma NAVEGAÇÃO, que não existe na linha transiente; passa a ler `ui>/contractCodes[PurchaseContractKey]` antes da navegação (formatter com `parts`, como a coluna "Remessa"), e o value help de contrato também grava no mapa.
- **Contrato travado ou editável na tela (D5):** a linha do complemento próprio trava o contrato só quando o item original tem contrato. A tela sabe disso pelo mapa `ui>/inheritedContracts` (item original → contrato), preenchido pela mesma function de candidatos (no "Incluir Item" e, no Detail/Edit, junto dos rótulos da "Nota original"); a edição é decidida por formatter (`draft.isLineContractEditable`), porque expression binding não indexa mapa por chave dinâmica. Mapa ainda não carregado = editável; o backend (C10) protege.
- **Verificação (Task 9):** série 9 nº 5 (sem contrato) com o contrato CC000002 escolhido pelo operador, como o spec pede — nenhuma entrada nova emitida antes.

---

## File Structure

Backend (`siagro-b1-backend`):

| Arquivo | Responsabilidade |
|---|---|
| Modify `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs` | `ComplementedItemKey` + navegação `ComplementedItem` |
| Modify `SiagroB1.Infra/Context/AppDbContext.cs` | FK Restrict da auto-relação |
| Create migration `SiagroB1.Migrations/AppContext/*_AddPurchaseInvoiceItemComplementedItem` | coluna, índice, FK |
| Modify `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs` | regras C1–C5, C7, C8 e predicado da original elegível |
| Modify `…/PurchaseInvoicesTaxApplyService.cs`, `…/Financials/FinancialDocumentsFirmSyncService.cs` | passam o contexto à regra; tributação do complemento próprio |
| Modify `…/PurchaseInvoicesUpdateService.cs`, `…/PurchaseInvoicesItemsUpdateService.cs` | copiam `ComplementedItemKey`; guarda de contrato |
| Create `…/PurchaseInvoicesGetComplementCandidatesService.cs`, `SiagroB1.Domain/Dtos/PurchaseInvoiceComplementCandidateDto.cs`, `SiagroB1.Web/Functions/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesController.cs`; Modify `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | function de candidatos |
| Modify `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`, `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs` | `NfePurpose.Complement` → finNFe=2 |
| Modify `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs`, `NfeReadinessValidator.cs`, `NfeIssueContext.cs`, `NfeIssueInputAssembler.cs` | emissão |
| Modify `…/PurchaseInvoices/PurchaseInvoicesCancelService.cs`, `…/Nfe/PurchaseInvoicesNfeReturnCreateService.cs` | C8, C9 |
| Tests: Create `SiagroB1.Application.Tests/Support/ValueComplementNfeSeed.cs`, `PurchaseInvoices/PurchaseInvoiceValueComplementModelTests.cs`, `PurchaseInvoices/PurchaseInvoiceOwnValueComplementTests.cs`, `PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesTests.cs`, `Nfe/PurchaseInvoicesNfeValueComplementIssueTests.cs`, `Nfe/PurchaseInvoiceValueComplementLocksTests.cs`; Modify `PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs`, `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs`, `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs` | |

Frontend (`siagro-b1-frontend/webapp`):

| Arquivo | Responsabilidade |
|---|---|
| Modify `helpers/PurchaseInvoiceNfeHelpers.ts`, `test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts` | emitir o complemento, `isOwnComplement`, caminho da function, `ComplementedItemKey` no `$select` |
| Modify `view/purchaseInvoices/fragments/Form.fragment.xml`, `controller/purchaseInvoices/BaseController.ts` | Tipo escolhível no modo NF-e; flag `ui>/ownComplement` |
| Modify `helpers/PurchaseInvoiceDraftHelpers.ts`, `test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts`; Create `types/PurchaseInvoiceComplementCandidate.ts` | linha herdada e formatters |
| Modify `model/ServerRoutes.ts`; Create `view/purchaseInvoices/fragments/ComplementCandidatesDialog.fragment.xml`; Modify `fragments/Items.fragment.xml`, `controller/purchaseInvoices/{BaseController,Add,Edit}.controller.ts` | diálogo, travas, "Nota original" |

---

### Task 1: Modelo — `ComplementedItemKey`, FK Restrict, migration e EDM

**Files:**
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs:73-75` (depois de `ProducerInvoiceItem`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:246-250` (depois da FK de `ProducerInvoiceItem`)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseInvoiceItemComplementedItem.cs` (+ `.Designer.cs`; `AppDbContextModelSnapshot.cs` modificado)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementModelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `PurchaseInvoiceItem.ComplementedItemKey` (`Guid?`) e `PurchaseInvoiceItem.ComplementedItem` (`PurchaseInvoiceItem?`); `ComplementedItemKey` (Edm.Guid anulável) no tipo do entity set `PurchaseInvoicesItems`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Modelo e EDM do complemento de valor próprio (spec 2026-10-14): o vínculo mora na linha.</summary>
public class PurchaseInvoiceValueComplementModelTests
{
    private static IEdmModel EdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Complemented_item_is_a_restrict_self_reference()
    {
        var db = TestDb.CreateUnitOfWork();
        var foreignKey = db.Context.Model.FindEntityType(typeof(PurchaseInvoiceItem))!.GetForeignKeys()
            .Single(f => f.Properties.Single().Name == nameof(PurchaseInvoiceItem.ComplementedItemKey));

        Assert.Equal(typeof(PurchaseInvoiceItem), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.IsRequired);
    }

    [Fact]
    public void Item_exposes_the_complemented_item_key_as_a_nullable_guid()
    {
        var type = EdmModel().EntityContainer.FindEntitySet("PurchaseInvoicesItems").EntityType;
        var property = type.FindProperty(nameof(PurchaseInvoiceItem.ComplementedItemKey));

        Assert.NotNull(property);
        Assert.Equal("Edm.Guid", property.Type.FullName());
        Assert.True(property.Type.IsNullable);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceValueComplementModelTests"`
Expected: erro de compilação (`ComplementedItemKey` não existe).

- [ ] **Step 3: Entidade**

`SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs`, logo depois de `public virtual PurchaseInvoiceItem? ProducerInvoiceItem { get; set; }` (linha 75):

```csharp

    /// <summary>
    /// Só no complemento de valor de EMISSÃO PRÓPRIA (spec 2026-10-14): o item da nota original (entrada própria
    /// autorizada, normal ou contra-nota) que esta linha complementa. Obrigatório nesse documento e proibido em todos os
    /// outros (C1/C7). O NFref da NF-e complementar é derivado daqui — as chaves distintas das originais. NÃO reaproveita
    /// <see cref="PurchaseInvoiceItemOriginKey"/>: aquele é devolução/remessa e alimenta o saldo devolvível.
    /// </summary>
    public Guid? ComplementedItemKey { get; set; }
    public virtual PurchaseInvoiceItem? ComplementedItem { get; set; }
```

- [ ] **Step 4: FK**

`SiagroB1.Infra/Context/AppDbContext.cs`, logo depois do bloco que termina em `.HasForeignKey(x => x.ProducerInvoiceItemKey).OnDelete(DeleteBehavior.Restrict);` (linha 250):

```csharp

        // Complemento de valor próprio → item da nota original (spec 2026-10-14). Restrict como as auto-relações acima:
        // o spec escreveu NoAction, que no SQL Server é o MESMO DDL (ON DELETE NO ACTION); Restrict casa com as vizinhas
        // e com o snapshot. A original autorizada não é excluída, e o complemento (dependente) some sem levar nada junto.
        modelBuilder.Entity<PurchaseInvoiceItem>()
            .HasOne(x => x.ComplementedItem)
            .WithMany()
            .HasForeignKey(x => x.ComplementedItemKey)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceValueComplementModelTests|FullyQualifiedName~PurchaseInvoiceNfeSchemaTests|FullyQualifiedName~PurchaseInvoiceNfeEdmModelTests"`
Expected: todos PASS.

- [ ] **Step 6: Migration**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddPurchaseInvoiceItemComplementedItem --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Conferir no `Up`, e **só** isto (nomes gerados pelo EF):

```csharp
            migrationBuilder.AddColumn<Guid>(
                name: "ComplementedItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_ComplementedItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "ComplementedItemKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_INVOICES_ITEMS_ComplementedItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "ComplementedItemKey",
                principalTable: "PURCHASE_INVOICES_ITEMS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
```

e no `Down` o `DropForeignKey`, `DropIndex` e `DropColumn` correspondentes. Qualquer outra operação = modelo divergente: parar e investigar. `git add` dos 3 arquivos (`.cs`, `.Designer.cs`, snapshot).

- [ ] **Step 7: Gates**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: build OK; "No changes have been made to the model since the last migration."

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementModelTests.cs
```

O controlador commita com:

```
feat(invoice): vínculo da linha do complemento com o item da nota original

A NF-e complementar própria referencia, linha a linha, o item da entrada própria
autorizada que ela complementa; o NFref do cabeçalho será derivado daqui. Campo novo
em vez de reaproveitar PurchaseInvoiceItemOriginKey, que significa devolução/remessa e
alimenta o saldo devolvível. Sem backfill: hoje não existe complemento próprio.

Atenção: FK Restrict, como as auto-relações vizinhas. O spec diz NoAction — o DDL é o
mesmo no SQL Server, mas trocar um pelo outro gera migration inerte.

DB: AddPurchaseInvoiceItemComplementedItem
Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 2: Regras da linha do complemento próprio (C1–C5, C7, C10 e contrato do operador)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs` (inteiro; a recusa de `Own` em `:30-32` sai)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs:108-139` (sobrecarga com `AppDbContext`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:46-47`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs:172`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs:101-102`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs:46-49`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs:207-208, 214-233, 238-258`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs:50, 75` (guarda de contrato; atribuições)
- Create (test support): `SiagroB1.Application.Tests/Support/ValueComplementNfeSeed.cs`
- Modify (test): `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs:109-110`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceOwnValueComplementTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ComplementedItemKey`, `ComplementedItem`).
- Produces:
  - `PurchaseInvoiceValueComplement.EnsureAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, IUsage usageService, bool standalone, AppDbContext context)` (assinatura nova: + `context`).
  - `internal static bool PurchaseInvoiceValueComplement.IsComplementableOrigin(PurchaseInvoice origin)` — própria, Normal, NF-e Autorizada, documento não Cancelado (usado nas Tasks 3 e 5).
  - `internal static string PurchaseInvoiceValueComplement.NoLongerAuthorizedMessage(string? originalNumber)` (C6, usado na Task 5).
  - `PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(AppDbContext context, Guid? purchaseContractKey, string? itemCode, string cardCode)` (sobrecarga nova; a de `IUnitOfWork` delega).
  - Test support `ValueComplementNfeSeed` (`SeedAsync()`, `AddOriginalAsync(...)`, `AddContractAsync(...)`, `ChangeAsync(...)`, `Create(UnitOfWork)`, `AccessKey(int)`, `NextLineKey()`, constantes `OriginalNumber`, `SecondNumber`) e `ValueComplementScenario` (`Db`, `Purchase`, `Contract`, `PurchaseUsage`, `PaymentConditionCode`, `OriginalKey`, `OriginalItemKey`, `Complement(params (Guid?, decimal)[])`) — usados pelas Tasks 3, 5 e 6.

- [ ] **Step 1: Seed de teste**

`SiagroB1.Application.Tests/Support/ValueComplementNfeSeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Cenário do complemento de valor próprio (spec 2026-10-14) sobre o da entrada própria (PurchaseNfeTestSeed).</summary>
public sealed record ValueComplementScenario(
    PurchaseNfeScenario Purchase, PurchaseContract Contract, int PurchaseUsage, int PaymentConditionCode,
    Guid OriginalKey, Guid OriginalItemKey)
{
    public UnitOfWork Db => Purchase.Db;

    /// <summary>
    /// Complemento próprio Pendente, ainda não gravado: cada linha só com o item original e o valor — produto, unidade,
    /// natureza e contrato o servidor herda. Data = a do relógio dos testes de emissão (02/10/2026).
    /// </summary>
    public PurchaseInvoice Complement(params (Guid? OriginalItemKey, decimal Amount)[] lines)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, CardName = "PRODUTOR RURAL TESTE",
            IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.ValueComplement,
            IssueDate = new DateTime(2026, 10, 2), PostingDate = new DateTime(2026, 10, 2), FreightTerms = FreightTerms.None,
            PaymentConditionCode = PaymentConditionCode,
        };

        foreach (var (originalItemKey, amount) in lines)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = ValueComplementNfeSeed.NextLineKey(), ComplementedItemKey = originalItemKey, ComplementAmount = amount,
            });

        return invoice;
    }
}

/// <summary>
/// Produtor F-SP com a natureza "COMPRA DE MERCADORIA" gerando financeiro, contrato aprovado de TRIGO e a entrada própria
/// DE000005 (série 9 nº 5) AUTORIZADA — a nota original que o complemento referencia.
/// </summary>
public static class ValueComplementNfeSeed
{
    public const string OriginalNumber = "DE000005";
    public const string SecondNumber = "DE000007";

    private static long _lineSequence;

    /// <summary>Chave de 44 dígitos da NF-e de entrada série 9 nº <paramref name="number"/> da filial semeada.</summary>
    public static string AccessKey(int number) => $"3526101234567800019555009{number:000000000}1481516234";

    /// <summary>Chaves crescentes: a ordem do nItem das linhas novas é a da Key (NfeItemNumbering), e o teste precisa prevê-la.</summary>
    public static Guid NextLineKey() =>
        Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref _lineSequence):x12}");

    public static async Task<ValueComplementScenario> SeedAsync()
    {
        var purchase = await PurchaseNfeTestSeed.SeedAsync();
        var context = purchase.Db.Context;

        // STANDALONE: a linha do complemento exige natureza com "Gera financeiro" (spec 2026-10-13 D10).
        var usage = await context.Usages.SingleAsync(u => u.Name == "COMPRA DE MERCADORIA");
        usage.CreatesFinancialDocument = true;

        var contract = await AddContractAsync(purchase.Db, "PC-TRIGO");

        var condition = (await context.PaymentConditions.FirstAsync()).Code;
        var (originalKey, itemKeys) = await AddOriginalAsync(purchase.Db, contract, usage.Code, OriginalNumber, 5);

        return new ValueComplementScenario(purchase, contract, usage.Code, condition, originalKey, itemKeys[0]);
    }

    /// <summary>Contrato de compra de TRIGO (aprovado por padrão) — o do operador quando a original não tem (D5).</summary>
    public static async Task<PurchaseContract> AddContractAsync(
        UnitOfWork db, string code, string cardCode = PurchaseNfeTestSeed.Supplier, ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = cardCode, ItemCode = "TRIGO", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", Status = status, TotalVolume = 1_000_000m, FreightTerms = FreightTerms.Cif,
            DeliveryLocationCode = "01",
        };
        db.Context.PurchaseContracts.Add(contract);
        await db.SaveChangesAsync();
        return contract;
    }

    /// <summary>Entrada própria Normal, confirmada e AUTORIZADA, com <paramref name="items"/> linhas de TRIGO no contrato.</summary>
    public static async Task<(Guid InvoiceKey, IReadOnlyList<Guid> ItemKeys)> AddOriginalAsync(
        UnitOfWork db, PurchaseContract contract, int usageCode, string invoiceNumber, int nfeNumber, int items = 1)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = invoiceNumber, BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            CardName = "PRODUTOR RURAL TESTE", IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.Normal,
            InvoiceStatus = InvoiceStatus.Confirmed, NfeStatus = NfeStatus.Authorized, NfeEnvironment = NfeEnvironment.Homologation,
            ChaveNFe = AccessKey(nfeNumber), NfeProtocol = "135260000000005", TaxDocumentSeries = "9",
            TaxDocumentNumber = nfeNumber.ToString("000000000"), IssueDate = new DateTime(2026, 10, 1),
            PostingDate = new DateTime(2026, 10, 1), GrossWeight = 1000m * items, NetWeight = 1000m * items,
            FreightTerms = FreightTerms.None,
        };

        for (var number = 1; number <= items; number++)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG",
                Quantity = 1000m, UnitPrice = 1.5m, UsageCode = usageCode, UsageName = "COMPRA DE MERCADORIA",
                PurchaseContractKey = contract.Key, NfeItemNumber = number,
            });

        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (invoice.Key, invoice.Items.OrderBy(i => i.NfeItemNumber).Select(i => i.Key!.Value).ToList());
    }

    /// <summary>Altera um documento no contexto do cenário — é ele que os serviços leem.</summary>
    public static async Task ChangeAsync(UnitOfWork db, Guid invoiceKey, Action<PurchaseInvoice> change)
    {
        var invoice = await db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == invoiceKey);
        change(invoice);
        await db.SaveChangesAsync();
    }

    /// <summary>Inclusão com a regra ativa (STANDALONE) e o cadastro do F-SP; a numeração interna começa em ST-0001.</summary>
    public static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()), new FakeDocNumberSequenceService());
}
```

`git add` o arquivo.

- [ ] **Step 2: Testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceOwnValueComplementTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-14, regras 1–5 e C7: a linha do complemento de valor de emissão própria.</summary>
public class PurchaseInvoiceOwnValueComplementTests
{
    private const string NotIssuable =
        "Item TRIGO: a nota original DE000005 precisa ser uma entrada própria normal, com NF-e autorizada e não cancelada.";
    private const string OtherParty = "Item TRIGO: a nota original DE000005 é de outro produtor ou de outra filial.";
    private const string Duplicated = "O item DE000005/1 já está complementado nesta nota.";
    private const string Inherited =
        "Item TRIGO: produto, unidade e natureza vêm do item da nota original e não podem ser alterados.";
    private const string InheritedContract = "Item TRIGO: o contrato vem do item da nota original e não pode ser alterado.";
    private const string OnlyOwnComplement = "O item da nota original só existe no complemento de valor de emissão própria.";

    /// <summary>D5: a original sem contrato na linha — o caso real de toda entrada própria autorizada da CEAGUI dev.</summary>
    private static Task WithoutContractAsync(ValueComplementScenario s) =>
        ChangeAsync(s.Db, s.OriginalKey, o => o.Items.Single().PurchaseContractKey = null);

    /// <summary>O contrato que o operador escolheu na tela, em todas as linhas do rascunho.</summary>
    private static PurchaseInvoice WithContract(PurchaseInvoice invoice, Guid? contractKey)
    {
        foreach (var line in invoice.Items)
            line.PurchaseContractKey = contractKey;
        return invoice;
    }

    private static PurchaseInvoicesTaxApplyService Apply(ValueComplementScenario s) =>
        TaxTestServices.PurchaseApply(s.Db, PurchaseNfeTestSeed.Partners());

    private static PurchaseInvoicesItemsCreateService ItemsCreate(ValueComplementScenario s) => new(s.Db, new FakeItemService(), Apply(s));

    private static PurchaseInvoicesItemsUpdateService ItemsUpdate(ValueComplementScenario s) => new(s.Db, new FakeItemService(), Apply(s));

    private static PurchaseInvoicesUpdateService Update(ValueComplementScenario s) =>
        new(s.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(s));

    private static async Task<string> RefusalAsync(ValueComplementScenario s, PurchaseInvoice invoice) =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db).ExecuteAsync(invoice, "ana"))).Message;

    /// <summary>Complemento gravado (Pendente) e o rastreador limpo, como entre duas requisições.</summary>
    private static async Task<PurchaseInvoice> SavedComplementAsync(ValueComplementScenario s, decimal amount = 300m)
    {
        var invoice = s.Complement((s.OriginalItemKey, amount));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();
        return invoice;
    }

    private static Task<PurchaseInvoice> ReloadAsync(ValueComplementScenario s, Guid key) =>
        s.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == key);

    [Fact]
    public async Task Line_inherits_product_unit_nature_and_contract_from_the_original()
    {
        var s = await SeedAsync();

        var invoice = await SavedComplementAsync(s);

        var line = Assert.Single((await ReloadAsync(s, invoice.Key)).Items);
        Assert.Equal(s.OriginalItemKey, line.ComplementedItemKey);
        Assert.Equal("TRIGO", line.ItemCode);
        Assert.Equal("TRIGO EM GRAOS", line.ItemName);
        Assert.Equal("KG", line.UnitOfMeasureCode);
        Assert.Equal(s.PurchaseUsage, line.UsageCode);
        Assert.Equal(s.Contract.Key, line.PurchaseContractKey);
        Assert.Equal(0m, line.Quantity);
        Assert.Equal(0m, line.UnitPrice);
        Assert.Equal(300m, line.ComplementAmount);
    }

    [Fact]
    public async Task Line_without_the_original_item_is_refused()
    {
        var s = await SeedAsync();

        Assert.Equal("Item (sem produto): o complemento de valor de emissão própria exige o item da nota original.",
            await RefusalAsync(s, s.Complement((null, 300m))));
        Assert.Equal("Item (sem produto): o complemento de valor de emissão própria exige o item da nota original.",
            await RefusalAsync(s, s.Complement((Guid.NewGuid(), 300m))));
        Assert.Empty(s.Db.Context.PurchaseInvoices.AsNoTracking().Where(i => i.InvoiceType == PurchaseInvoiceType.ValueComplement));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("third-party")]
    [InlineData("nfe-cancelled")]
    [InlineData("document-cancelled")]
    [InlineData("complement")]
    [InlineData("return")]
    public async Task Original_must_be_an_authorized_own_normal_entry(string variant)
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, original =>
        {
            switch (variant)
            {
                case "pending": original.InvoiceStatus = InvoiceStatus.Pending; original.NfeStatus = NfeStatus.None; break;
                case "third-party": original.IssuerType = DocumentIssuerType.ThirdParty; break;
                case "nfe-cancelled": original.NfeStatus = NfeStatus.Cancelled; break;
                case "document-cancelled": original.InvoiceStatus = InvoiceStatus.Cancelled; break;
                case "complement": original.InvoiceType = PurchaseInvoiceType.ValueComplement; break;
                case "return": original.InvoiceType = PurchaseInvoiceType.Return; break;
            }
        });

        Assert.Equal(NotIssuable, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));
    }

    [Fact]
    public async Task Original_of_another_supplier_or_branch_is_refused()
    {
        var s = await SeedAsync();

        await ChangeAsync(s.Db, s.OriginalKey, o => o.CardCode = "F-OUTRO");
        Assert.Equal(OtherParty, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));

        await ChangeAsync(s.Db, s.OriginalKey, o => { o.CardCode = PurchaseNfeTestSeed.Supplier; o.BranchCode = "02"; });
        Assert.Equal(OtherParty, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));
    }

    [Fact]
    public async Task Same_original_item_twice_in_one_note_is_refused()
    {
        var s = await SeedAsync();

        // Deep-insert com as duas linhas.
        Assert.Equal(Duplicated, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m), (s.OriginalItemKey, 100m))));

        // Linha a linha (grade do Editar → POST de linha).
        var invoice = await SavedComplementAsync(s);
        var extra = new PurchaseInvoiceItem
        {
            Key = NextLineKey(), PurchaseInvoiceKey = invoice.Key, ComplementedItemKey = s.OriginalItemKey, ComplementAmount = 50m,
        };
        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemsCreate(s).ExecuteAsync(extra, "ana"));
        Assert.Equal(Duplicated, e.Message);
    }

    [Fact]
    public async Task Successive_complements_of_the_same_item_are_allowed()
    {
        var s = await SeedAsync();

        await SavedComplementAsync(s, 300m);
        await SavedComplementAsync(s, 120m);

        Assert.Equal(2, await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().CountAsync(l => l.ComplementedItemKey == s.OriginalItemKey));
    }

    [Fact]
    public async Task Inherited_fields_cannot_be_changed_on_line_update()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.UsageCode = s.Purchase.ReturnUsage;

        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana"));

        Assert.Equal(Inherited, e.Message);
    }

    [Fact]
    public async Task Inherited_fields_cannot_be_changed_on_header_update()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.Items.Single().UnitOfMeasureCode = "SC";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(Inherited, e.Message);
    }

    [Fact]
    public async Task Only_the_complement_amount_changes_on_line_update()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.ComplementAmount = 450m;

        await ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana");

        Assert.Equal(450m, (await ReloadAsync(s, invoice.Key)).Items.Single().ComplementAmount);
    }

    [Fact]
    public async Task Changing_the_supplier_after_including_lines_is_refused()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.CardCode = "F-OUTRO";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(OtherParty, e.Message);
    }

    [Fact]
    public async Task Changing_the_branch_after_including_lines_is_refused()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.BranchCode = "02";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(OtherParty, e.Message);
    }

    [Fact]
    public async Task Contract_sent_for_an_original_with_contract_is_replaced_by_the_inherited_one()
    {
        var s = await SeedAsync();
        var other = await AddContractAsync(s.Db, "PC-TRIGO-2");

        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), other.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");

        Assert.Equal(s.Contract.Key, (await ReloadAsync(s, invoice.Key)).Items.Single().PurchaseContractKey);
    }

    [Fact]
    public async Task Inherited_contract_cannot_be_changed_on_update()
    {
        var s = await SeedAsync();
        var other = await AddContractAsync(s.Db, "PC-TRIGO-2");
        var invoice = await SavedComplementAsync(s);
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.PurchaseContractKey = other.Key;

        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana"));

        Assert.Equal(InheritedContract, e.Message);
    }

    [Fact]
    public async Task Original_without_contract_takes_the_contract_chosen_by_the_operator()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var chosen = await AddContractAsync(s.Db, "PC-TRIGO-2");

        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), chosen.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");

        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        Assert.Equal(chosen.Key, line.PurchaseContractKey);
        Assert.Equal("TRIGO", line.ItemCode);
    }

    [Fact]
    public async Task Original_without_contract_still_requires_a_contract_on_the_line()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);

        Assert.Equal("Item TRIGO: o complemento de valor exige o contrato de compra na linha.",
            await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));
    }

    [Fact]
    public async Task Chosen_contract_goes_through_the_contract_rules()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var otherSupplier = await AddContractAsync(s.Db, "PC-OUTRO", cardCode: "F-OUTRO");
        var draft = await AddContractAsync(s.Db, "PC-RASCUNHO", status: ContractStatus.Draft);

        Assert.Equal(
            "O contrato PC-OUTRO, amarrado na linha do produto TRIGO, é de outro fornecedor e não pode ser amarrado a este documento.",
            await RefusalAsync(s, WithContract(s.Complement((s.OriginalItemKey, 300m)), otherSupplier.Key)));
        Assert.Equal(
            "O contrato PC-RASCUNHO, amarrado na linha do produto TRIGO, não está aprovado nem encerrado e não pode ser amarrado a um documento de entrada.",
            await RefusalAsync(s, WithContract(s.Complement((s.OriginalItemKey, 300m)), draft.Key)));
    }

    [Fact]
    public async Task Chosen_contract_can_be_changed_on_update()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var first = await AddContractAsync(s.Db, "PC-TRIGO-2");
        var second = await AddContractAsync(s.Db, "PC-TRIGO-3");
        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), first.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.PurchaseContractKey = second.Key;

        await ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana");

        Assert.Equal(second.Key, (await ReloadAsync(s, invoice.Key)).Items.Single().PurchaseContractKey);
    }

    [Fact]
    public async Task Changing_the_supplier_with_a_chosen_contract_is_refused_by_C3()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var chosen = await AddContractAsync(s.Db, "PC-TRIGO-2");
        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), chosen.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.CardCode = "F-OUTRO";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        // C3 primeiro, não "é de outro fornecedor" do contrato: a guarda de contrato roda depois de C3.
        Assert.Equal(OtherParty, e.Message);
    }

    [Fact]
    public async Task Original_item_key_only_exists_in_the_own_complement()
    {
        var s = await SeedAsync();

        var normal = s.Complement((s.OriginalItemKey, 0m));
        normal.InvoiceType = PurchaseInvoiceType.Normal;
        var normalLine = normal.Items.Single();
        normalLine.ItemCode = "TRIGO";
        normalLine.UnitOfMeasureCode = "KG";
        normalLine.Quantity = 10m;
        normalLine.UnitPrice = 1.5m;
        normalLine.UsageCode = s.PurchaseUsage;
        Assert.Equal(OnlyOwnComplement, await RefusalAsync(s, normal));

        var thirdParty = s.Complement((s.OriginalItemKey, 300m));
        thirdParty.IssuerType = DocumentIssuerType.ThirdParty;
        thirdParty.TaxDocumentKind = TaxDocumentKind.Other;
        thirdParty.TaxDocumentNumber = "5001";
        thirdParty.TaxDocumentSeries = "1";
        var thirdPartyLine = thirdParty.Items.Single();
        thirdPartyLine.ItemCode = "TRIGO";
        thirdPartyLine.UsageCode = s.PurchaseUsage;
        thirdPartyLine.PurchaseContractKey = s.Contract.Key;
        Assert.Equal(OnlyOwnComplement, await RefusalAsync(s, thirdParty));
    }
}
```

E em `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs` trocar as linhas 109-110

```csharp
        Assert.Equal("Complemento de valor é só escrituração por enquanto: lance-o como documento de terceiro. A emissão de NF-e de complemento vem depois.",
            await RefusalAsync(s, Document(s, s.Financial, issuer: DocumentIssuerType.Own)));
```

por

```csharp
        // Spec 2026-10-14: a recusa do complemento próprio saiu; sem o item da nota original, C1.
        Assert.Equal("Item SOJA: o complemento de valor de emissão própria exige o item da nota original.",
            await RefusalAsync(s, Document(s, s.Financial, issuer: DocumentIssuerType.Own)));
```

`git add` o arquivo novo.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceOwnValueComplementTests|FullyQualifiedName~PurchaseInvoiceValueComplementTests"`
Expected: compila (Task 1 deu a propriedade); FAIL — a recusa antiga "Complemento de valor é só escrituração por enquanto…" em todos os testes que gravam complemento próprio.

- [ ] **Step 4: A regra**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs` inteiro:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Documento de entrada "Complemento de valor" (spec 2026-10-13 D11): o faturado ABAIXO do contrato. Regra única
/// das linhas, chamada pelos quatro caminhos que gravam linha (deep-insert, PATCH do cabeçalho, POST e PATCH de
/// linha — ver PurchaseInvoiceLineGuard) e de novo na confirmação, que cria os títulos firmes.
///
/// Emissão própria (spec 2026-10-14): cada linha aponta o item de uma entrada própria autorizada do mesmo produtor e
/// filial (ComplementedItemKey) e herda dela produto, unidade e natureza — e o contrato, quando o original tem (D5).
/// </summary>
public static class PurchaseInvoiceValueComplement
{
    /// <param name="standalone">
    /// STANDALONE exige natureza com "Gera financeiro"; em SAPB1 a natureza é o OUSG, sem a flag (D10), e basta existir.
    /// </param>
    /// <param name="context">Lê os itens originais do complemento próprio.</param>
    public static async Task EnsureAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, IUsage usageService, bool standalone,
        AppDbContext context)
    {
        var ownComplement = invoice.InvoiceType == PurchaseInvoiceType.ValueComplement &&
                            invoice.IssuerType == DocumentIssuerType.Own;

        // C7: o vínculo com a nota original só existe no complemento próprio — o de terceiro é escrituração da nota dele.
        if (!ownComplement && lines.Any(l => l.ComplementedItemKey is not null))
            throw new DefaultException("O item da nota original só existe no complemento de valor de emissão própria.");

        if (invoice.InvoiceType != PurchaseInvoiceType.ValueComplement)
        {
            var carrying = lines.FirstOrDefault(l => l.ComplementAmount != 0m);
            if (carrying is not null)
                throw new DefaultException(
                    $"Item {carrying.ItemCode}: valor de complemento só existe no documento do tipo Complemento de valor.");
            return;
        }

        if (ownComplement)
            await EnsureOwnLinesAsync(invoice, lines, context);

        foreach (var line in lines)
        {
            if (line.Quantity != 0m || line.UnitPrice != 0m)
                throw new DefaultException(
                    $"Item {line.ItemCode}: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.");

            if (line.ComplementAmount <= 0m)
                throw new DefaultException($"Item {line.ItemCode}: informe o valor do complemento.");

            if (line.PurchaseContractKey is null)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige o contrato de compra na linha.");

            if (line.UsageCode is not { } usageCode)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige a natureza de operação.");

            if (standalone && (await usageService.GetByIdAsync(usageCode))?.CreatesFinancialDocument != true)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a natureza {usageCode} não gera financeiro; o complemento de valor precisa de uma natureza que gere.");
        }
    }

    /// <summary>
    /// Regra 2 / C2: a nota que se complementa — própria, Normal (a contra-nota é uma entrada própria Normal: D1), com a
    /// NF-e autorizada e o documento não cancelado. Só dados do próprio documento.
    /// </summary>
    internal static bool IsComplementableOrigin(PurchaseInvoice origin) =>
        origin.IssuerType == DocumentIssuerType.Own &&
        origin.InvoiceType == PurchaseInvoiceType.Normal &&
        origin.NfeStatus == NfeStatus.Authorized &&
        origin.InvoiceStatus != InvoiceStatus.Cancelled;

    /// <summary>C6: a original deixou de estar autorizada entre o rascunho e a emissão.</summary>
    internal static string NoLongerAuthorizedMessage(string? originalNumber) =>
        $"A nota original {originalNumber} não está mais autorizada: o complemento não pode ser emitido.";

    /// <summary>C1: a linha do complemento próprio não aponta um item de nota original.</summary>
    internal static string MissingOriginalMessage(string? itemCode) =>
        $"Item {(string.IsNullOrWhiteSpace(itemCode) ? "(sem produto)" : itemCode)}: o complemento de valor de emissão própria exige o item da nota original.";

    /// <summary>Regras 1–5a (C1–C5, C10). Linha nova herda do original; linha já gravada não pode divergir dele.</summary>
    private static async Task EnsureOwnLinesAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, AppDbContext context)
    {
        var missing = lines.FirstOrDefault(l => l.ComplementedItemKey is null);
        if (missing is not null)
            throw new DefaultException(MissingOriginalMessage(missing.ItemCode));

        var keys = lines.Select(l => l.ComplementedItemKey!.Value).Distinct().ToList();
        var originals = await context.PurchaseInvoicesItems.AsNoTracking()
            .Include(i => i.PurchaseInvoice!).ThenInclude(p => p.Items)
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);

        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            var key = line.ComplementedItemKey!.Value;

            // Chave que não existe: a linha não aponta item nenhum (C1) — C2 precisa de um documento para citar.
            if (!originals.TryGetValue(key, out var original) || original.PurchaseInvoice is not { } origin)
                throw new DefaultException(MissingOriginalMessage(line.ItemCode));

            if (!IsComplementableOrigin(origin))
                throw new DefaultException(
                    $"Item {original.ItemCode}: a nota original {origin.InvoiceNumber} precisa ser uma entrada própria normal, com NF-e autorizada e não cancelada.");

            // C3 roda também no PATCH do cabeçalho: trocar o produtor ou a filial depois de incluir linhas é recusado.
            if (origin.CardCode != invoice.CardCode || origin.BranchCode != invoice.BranchCode)
                throw new DefaultException(
                    $"Item {original.ItemCode}: a nota original {origin.InvoiceNumber} é de outro produtor ou de outra filial.");

            // Complementos sucessivos do mesmo item são permitidos; o mesmo item duas vezes na MESMA nota, não (C4).
            if (!seen.Add(key))
                throw new DefaultException(
                    $"O item {origin.InvoiceNumber}/{NfeItemNumbering.OriginNumber(original, origin.Items.Count)} já está complementado nesta nota.");

            await InheritOrRefuseAsync(invoice, line, original, context);
        }
    }

    /// <summary>
    /// Regras 5 e 5a. Linha NOVA — Detached (deep-insert, POST de linha, confirmação com AsNoTracking) ou Added (linha
    /// incluída pelo PATCH do cabeçalho) — herda do item original produto, unidade e natureza, e o contrato quando o
    /// original tem. Linha já gravada (rastreada) recusa divergência: C5 nos três campos, C10 no contrato herdado (a tela
    /// trava esses campos, então a divergência só chega pela API).
    ///
    /// Original SEM contrato (D5): o contrato é o que o operador informou, e passa AQUI pelas regras de contrato da linha
    /// de entrada — os quatro caminhos que gravam linha deixam de chamar a guarda para a linha do complemento, para que
    /// ela rode com o produto já herdado e DEPOIS de C3 (trocar o produtor do cabeçalho recusa por C3, não pelo
    /// contrato). Sem contrato, a regra existente do complemento recusa adiante (obrigatório).
    /// </summary>
    private static async Task InheritOrRefuseAsync(
        PurchaseInvoice invoice, PurchaseInvoiceItem line, PurchaseInvoiceItem original, AppDbContext context)
    {
        if (context.Entry(line).State is EntityState.Detached or EntityState.Added)
        {
            line.ItemCode = original.ItemCode;
            line.ItemName = original.ItemName;
            line.UnitOfMeasureCode = original.UnitOfMeasureCode;
            line.UsageCode = original.UsageCode;
            if (original.PurchaseContractKey is not null)
                line.PurchaseContractKey = original.PurchaseContractKey;
        }
        else
        {
            if (line.ItemCode != original.ItemCode || line.UnitOfMeasureCode != original.UnitOfMeasureCode ||
                line.UsageCode != original.UsageCode)
                throw new DefaultException(
                    $"Item {original.ItemCode}: produto, unidade e natureza vêm do item da nota original e não podem ser alterados.");

            if (original.PurchaseContractKey is not null && line.PurchaseContractKey != original.PurchaseContractKey)
                throw new DefaultException(
                    $"Item {original.ItemCode}: o contrato vem do item da nota original e não pode ser alterado.");
        }

        if (original.PurchaseContractKey is null && line.PurchaseContractKey is not null)
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                context, line.PurchaseContractKey, line.ItemCode, invoice.CardCode);
    }
}
```

- [ ] **Step 5: Quem chama passa o contexto**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:45-47` — trocar

```csharp
    public Task EnsureValueComplementAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        PurchaseInvoiceValueComplement.EnsureAsync(invoice, lines, usageService, gate.IsStandalone);
```

por

```csharp
    public Task EnsureValueComplementAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        PurchaseInvoiceValueComplement.EnsureAsync(invoice, lines, usageService, gate.IsStandalone, db.Context);
```

`SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs:172` — trocar

```csharp
        await PurchaseInvoiceValueComplement.EnsureAsync(invoice, lines, usageService, ErpMode.IsStandalone(configuration));
```

por

```csharp
        await PurchaseInvoiceValueComplement.EnsureAsync(
            invoice, lines, usageService, ErpMode.IsStandalone(configuration), db.Context);
```

- [ ] **Step 6: Guarda de contrato com o contexto e os quatro caminhos**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs:108-139` — trocar a assinatura

```csharp
    public static async Task EnsureContractIsCompatibleAsync(
        IUnitOfWork db, Guid? purchaseContractKey, string? itemCode, string cardCode)
    {
        if (purchaseContractKey is null)
            return;

        var contract = await db.Context.PurchaseContracts
```

por

```csharp
    public static Task EnsureContractIsCompatibleAsync(
        IUnitOfWork db, Guid? purchaseContractKey, string? itemCode, string cardCode) =>
        EnsureContractIsCompatibleAsync(db.Context, purchaseContractKey, itemCode, cardCode);

    /// <summary>
    /// Mesma regra, para quem só tem o contexto: a linha do complemento próprio (spec 2026-10-14, D5) roda a guarda de
    /// dentro de <c>PurchaseInvoiceValueComplement</c>, depois de herdar o produto e de C3.
    /// </summary>
    public static async Task EnsureContractIsCompatibleAsync(
        AppDbContext context, Guid? purchaseContractKey, string? itemCode, string cardCode)
    {
        if (purchaseContractKey is null)
            return;

        var contract = await context.PurchaseContracts
```

(o resto do corpo fica igual) e acrescentar `using SiagroB1.Infra.Context;` no topo.

Nos quatro caminhos, a linha do complemento próprio deixa de chamar a guarda (quem chama é a regra do complemento, depois de C3, para o contrato escolhido pelo operador — nenhuma validação de contrato é pulada):

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs:101-102` — trocar

```csharp
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);
```

por

```csharp
            // Complemento próprio (spec 2026-10-14, D5): a regra do complemento valida o contrato depois de herdar o produto.
            if (item.ComplementedItemKey is null)
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);
```

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs:46-49` — trocar

```csharp
        // Linha sem contrato (caso comum: insumo, serviço, frete) não precisa chamar o guard.
        if (item.PurchaseContractKey is not null)
```

por

```csharp
        // Linha sem contrato (caso comum: insumo, serviço, frete) não precisa chamar o guard. Linha do complemento próprio:
        // a regra do complemento valida o contrato depois de herdar o produto e de C3 (spec 2026-10-14, D5).
        if (item.PurchaseContractKey is not null && item.ComplementedItemKey is null)
```

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs:50` — trocar

```csharp
        if (entity.PurchaseContractKey is not null)
```

por

```csharp
        // Linha do complemento próprio: a regra do complemento valida o contrato (C10 no herdado; guarda no escolhido).
        if (entity.PurchaseContractKey is not null && entity.ComplementedItemKey is null)
```

e depois de `existing.ComplementAmount = entity.ComplementAmount;` (`:75`):

```csharp
        existing.ComplementedItemKey = entity.ComplementedItemKey;
```

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs`, no `SyncItemsAsync` — trocar (linha nova, `:207-208`)

```csharp
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

                // Add no DbSet, e NÃO em existing.Items:
```

por

```csharp
                // Complemento próprio: a regra do complemento valida o contrato depois de C3 (spec 2026-10-14, D5).
                if (line.ComplementedItemKey is null)
                    await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                        db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

                // Add no DbSet, e NÃO em existing.Items:
```

no inicializador da linha nova, depois de `ComplementAmount = line.ComplementAmount,` (`:232`):

```csharp
                    ComplementedItemKey = line.ComplementedItemKey,
```

trocar (linha existente, `:238-239`)

```csharp
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

            // Capturado ANTES da atribuição: depois dela a comparação é sempre falsa.
```

por

```csharp
            // Complemento próprio: a regra do complemento valida o contrato DEPOIS de C3 — trocar o produtor do cabeçalho
            // recusa por C3, e o contrato escolhido pelo operador continua passando pela guarda (spec 2026-10-14, D5).
            if (line.ComplementedItemKey is null)
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

            // Capturado ANTES da atribuição: depois dela a comparação é sempre falsa.
```

e depois de `current.ComplementAmount = line.ComplementAmount;` (`:258`):

```csharp
            current.ComplementedItemKey = line.ComplementedItemKey;
```

(`PurchaseInvoicesCreateService` e `PurchaseInvoicesItemsCreateService` gravam a entidade que chegou: o vínculo já vem nela.)

- [ ] **Step 7: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceOwnValueComplementTests|FullyQualifiedName~PurchaseInvoiceValueComplementTests|FullyQualifiedName~FinancialDocumentsValueComplementTests|FullyQualifiedName~PurchaseInvoiceUnitOfMeasureTests|FullyQualifiedName~PurchaseInvoicesUpdateTests|FullyQualifiedName~PurchaseInvoicesItemsTests|FullyQualifiedName~PurchaseInvoiceContractLinkTests"`
Expected: todos PASS (os existentes provam que o complemento de terceiro e o documento Normal ficaram como estavam).

- [ ] **Step 8: Gates**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
```

Expected: build OK; suíte inteira verde (anotar a contagem).

- [ ] **Step 9: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs SiagroB1.Application.Tests/Support/ValueComplementNfeSeed.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceOwnValueComplementTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs
```

O controlador commita com:

```
feat(invoice): linha do complemento próprio aponta e herda o item da nota original

O complemento de valor de emissão própria deixa de ser recusado: cada linha aponta o
item de uma entrada própria autorizada do mesmo produtor e filial, e herda dele
produto, unidade e natureza, e o contrato quando o original tem. Original sem
contrato: o operador escolhe o contrato, que continua obrigatório e passa pelas regras
de contrato da linha. A linha nova herda; a já gravada recusa divergência. O mesmo
item duas vezes na mesma nota é recusado; complementos sucessivos, não. O vínculo é
proibido em qualquer outro documento.

Atenção: herdar ou recusar é decidido pelo estado do EF da linha (Detached/Added
herda). Na linha do complemento a guarda de contrato sai dos quatro caminhos e roda
dentro da regra, depois de C3: trocar o produtor recusa pela mensagem do complemento.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 3: Function `PurchaseInvoicesGetComplementCandidates`

**Files:**
- Create: `SiagroB1.Domain/Dtos/PurchaseInvoiceComplementCandidateDto.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesService.cs`
- Create: `SiagroB1.Web/Functions/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:211-213` (logo depois de `purchaseInvoicesNfeReturnableItems.ReturnsCollection<…>();`)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs:422` (depois de `services.AddScoped<PurchaseInvoicesGetOriginItemsService>();`)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesTests.cs`; Modify `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementModelTests.cs`

**Interfaces:**
- Consumes: Task 2 (`PurchaseInvoiceValueComplement.IsComplementableOrigin`, `ValueComplementNfeSeed`).
- Produces:
  - `PurchaseInvoicesGetComplementCandidatesService.ExecuteAsync(string? cardCode, string? branchCode) → Task<IReadOnlyList<PurchaseInvoiceComplementCandidateDto>>`.
  - `GET odata/PurchaseInvoicesGetComplementCandidates(CardCode='{c}',BranchCode='{b}')` → `{ value: [...] }` com as propriedades PascalCase `ItemKey, InvoiceKey, InvoiceNumber, InvoiceDate, AccessKey, LineNumber, ItemCode, ItemName, UnitOfMeasure, Quantity, UnitPrice, Total, UsageCode, UsageName, PurchaseContractKey, PurchaseContractCode` (Quantity/UnitPrice/Total em `double`; os dois do contrato **nulos** no item sem contrato — D5).

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesTests.cs`:

```csharp
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-14, "Serviços e API": itens que o complemento próprio pode apontar (regras 2 e 3), com ou sem contrato (D5).</summary>
public class PurchaseInvoicesGetComplementCandidatesTests
{
    private static PurchaseInvoicesGetComplementCandidatesService Service(ValueComplementScenario s) => new(s.Db);

    [Fact]
    public async Task Authorized_own_entry_items_with_contract_are_offered()
    {
        var s = await SeedAsync();

        var row = Assert.Single(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01"));

        Assert.Equal(s.OriginalItemKey, row.ItemKey);
        Assert.Equal(s.OriginalKey, row.InvoiceKey);
        Assert.Equal(OriginalNumber, row.InvoiceNumber);
        Assert.Equal(new DateTime(2026, 10, 1), row.InvoiceDate);
        Assert.Equal(AccessKey(5), row.AccessKey);
        Assert.Equal(1, row.LineNumber);
        Assert.Equal("TRIGO", row.ItemCode);
        Assert.Equal("TRIGO EM GRAOS", row.ItemName);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(1000d, row.Quantity);
        Assert.Equal(1.5d, row.UnitPrice);
        Assert.Equal(1500d, row.Total);
        Assert.Equal(s.PurchaseUsage, row.UsageCode);
        Assert.Equal("COMPRA DE MERCADORIA", row.UsageName);
        Assert.Equal(s.Contract.Key, row.PurchaseContractKey);
        Assert.Equal("PC-TRIGO", row.PurchaseContractCode);
    }

    [Fact]
    public async Task Ineligible_documents_and_items_are_not_offered()
    {
        var s = await SeedAsync();
        // O cenário já tem a entrada própria PENDENTE do PurchaseNfeTestSeed (F-SP): não aparece.
        foreach (var (number, change) in new (int, Action<SiagroB1.Domain.Entities.PurchaseInvoice>)[]
                 {
                     (10, i => i.IssuerType = DocumentIssuerType.ThirdParty),
                     (11, i => i.NfeStatus = NfeStatus.Cancelled),
                     (12, i => i.InvoiceStatus = InvoiceStatus.Cancelled),
                     (13, i => i.CardCode = "F-OUTRO"),
                     (14, i => i.InvoiceType = PurchaseInvoiceType.ValueComplement),
                 })
        {
            var (key, _) = await AddOriginalAsync(s.Db, s.Contract, s.PurchaseUsage, $"DE0000{number}", number);
            await ChangeAsync(s.Db, key, change);
        }

        var rows = await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01");

        Assert.Equal([OriginalNumber], rows.Select(r => r.InvoiceNumber).ToArray());
        Assert.Empty(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "02"));
        Assert.Empty(await Service(s).ExecuteAsync("", "01"));
        Assert.Empty(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, null));
    }

    [Fact]
    public async Task Items_without_contract_are_offered_with_an_empty_contract()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, o => o.Items.Single().PurchaseContractKey = null);

        var row = Assert.Single(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01"));

        Assert.Equal(s.OriginalItemKey, row.ItemKey);
        Assert.Null(row.PurchaseContractKey);
        Assert.Null(row.PurchaseContractCode);
    }

    [Fact]
    public async Task Counter_note_is_offered()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, o => o.ProducerInvoiceKey = s.Purchase.InvoiceKey);

        Assert.Single(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01"));
    }

    [Fact]
    public async Task Items_come_newest_note_first_in_item_order()
    {
        var s = await SeedAsync();
        var (newer, items) = await AddOriginalAsync(s.Db, s.Contract, s.PurchaseUsage, SecondNumber, 7, items: 2);
        await ChangeAsync(s.Db, newer, i => i.IssueDate = new DateTime(2026, 10, 2));

        var rows = await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01");

        Assert.Equal([items[0], items[1], s.OriginalItemKey], rows.Select(r => r.ItemKey).ToArray());
        Assert.Equal([1, 2, 1], rows.Select(r => r.LineNumber ?? 0).ToArray());
    }
}
```

E em `PurchaseInvoiceValueComplementModelTests.cs`, depois do último `[Fact]`:

```csharp

    [Fact]
    public void Candidates_function_takes_supplier_and_branch_and_returns_a_collection()
    {
        var function = EdmModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "PurchaseInvoicesGetComplementCandidates");

        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "CardCode").Type.FullName());
        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "BranchCode").Type.FullName());
        Assert.True(function.ReturnType.IsCollection());
    }
```

`git add` o arquivo novo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesGetComplementCandidatesTests|FullyQualifiedName~PurchaseInvoiceValueComplementModelTests"`
Expected: erro de compilação (`PurchaseInvoicesGetComplementCandidatesService` não existe).

- [ ] **Step 3: DTO**

`SiagroB1.Domain/Dtos/PurchaseInvoiceComplementCandidateDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Item de entrada própria autorizada que o complemento de valor próprio pode apontar (spec 2026-10-14) — a lista do
/// "Incluir item". Quantidades e valores em <c>double</c>: o UI5 lê <c>Edm.Decimal</c> como string. Todas as
/// propriedades com <c>[JsonPropertyName]</c> PascalCase — sem isso a resposta sai em camelCase e a tela lê undefined.
/// </summary>
public class PurchaseInvoiceComplementCandidateDto
{
    [JsonPropertyName("ItemKey")]
    public Guid ItemKey { get; set; }

    [JsonPropertyName("InvoiceKey")]
    public Guid InvoiceKey { get; set; }

    /// <summary>Número do documento (DE000001), o mesmo das mensagens.</summary>
    [JsonPropertyName("InvoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("InvoiceDate")]
    public DateTime? InvoiceDate { get; set; }

    [JsonPropertyName("AccessKey")]
    public string? AccessKey { get; set; }

    /// <summary>nItem do item na NF-e original.</summary>
    [JsonPropertyName("LineNumber")]
    public int? LineNumber { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasure")]
    public string? UnitOfMeasure { get; set; }

    [JsonPropertyName("Quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("UnitPrice")]
    public double UnitPrice { get; set; }

    [JsonPropertyName("Total")]
    public double Total { get; set; }

    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    /// <summary>Nome da natureza: a coluna Natureza da grade mostra o nome (fora do spec, decidido no plano).</summary>
    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    /// <summary>Nulo no item sem contrato (D5): o operador escolhe o contrato na linha do complemento.</summary>
    [JsonPropertyName("PurchaseContractKey")]
    public Guid? PurchaseContractKey { get; set; }

    [JsonPropertyName("PurchaseContractCode")]
    public string? PurchaseContractCode { get; set; }
}
```

- [ ] **Step 4: Serviço**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Itens que o complemento de valor próprio pode apontar (spec 2026-10-14, regras 2 e 3): entradas próprias Normais
/// (inclusive contra-nota) com NF-e autorizada, não canceladas, do produtor e da filial do cabeçalho. Com ou sem
/// contrato (D5): sem contrato, o operador escolhe o contrato na linha.
/// </summary>
public class PurchaseInvoicesGetComplementCandidatesService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<PurchaseInvoiceComplementCandidateDto>> ExecuteAsync(string? cardCode, string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(branchCode))
            return [];

        // Só comparações simples no Where (o InMemory não confere tradução); o predicado completo roda em memória.
        var invoices = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(i => i.Items).ThenInclude(l => l.PurchaseContract)
            .Where(i => i.CardCode == cardCode && i.BranchCode == branchCode &&
                        i.IssuerType == DocumentIssuerType.Own && i.InvoiceType == PurchaseInvoiceType.Normal &&
                        i.NfeStatus == NfeStatus.Authorized && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .ToListAsync();

        return invoices
            .Where(PurchaseInvoiceValueComplement.IsComplementableOrigin)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .SelectMany(invoice => NfeItemNumbering.Ordered(invoice.Items)
                .Select(item => new PurchaseInvoiceComplementCandidateDto
                {
                    ItemKey = item.Key!.Value,
                    InvoiceKey = invoice.Key,
                    InvoiceNumber = invoice.InvoiceNumber,
                    InvoiceDate = invoice.IssueDate,
                    AccessKey = invoice.ChaveNFe,
                    LineNumber = NfeItemNumbering.OriginNumber(item, invoice.Items.Count),
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    UnitOfMeasure = item.UnitOfMeasureCode,
                    Quantity = (double)item.Quantity,
                    UnitPrice = (double)item.UnitPrice,
                    Total = (double)item.Total,
                    UsageCode = item.UsageCode,
                    UsageName = item.UsageName,
                    PurchaseContractKey = item.PurchaseContractKey,
                    PurchaseContractCode = item.PurchaseContract?.Code,
                }))
            .ToList();
    }
}
```

- [ ] **Step 5: EDM, controller e DI**

`SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, logo depois de `purchaseInvoicesNfeReturnableItems.ReturnsCollection<PurchaseInvoiceNfeReturnableItemDto>();` (linha 213):

```csharp

        // Complemento de valor de emissão própria (spec 2026-10-14): itens das entradas próprias autorizadas do
        // fornecedor e da filial do cabeçalho. Rota por atributo: as strings chegam com as aspas (o controller tira).
        var purchaseInvoicesComplementCandidates = modelBuilder.Function("PurchaseInvoicesGetComplementCandidates");
        purchaseInvoicesComplementCandidates.Parameter<string>("CardCode");
        purchaseInvoicesComplementCandidates.Parameter<string>("BranchCode");
        purchaseInvoicesComplementCandidates.ReturnsCollection<PurchaseInvoiceComplementCandidateDto>();
```

(`ComplementedItemKey` é persistida: entra no EDM sozinha — **não** acrescentar `AddProperty`.)

`SiagroB1.Web/Functions/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.PurchaseInvoices;

/// <summary>Itens que o complemento de valor próprio pode apontar (spec 2026-10-14) — o diálogo do "Incluir Item".</summary>
public class PurchaseInvoicesGetComplementCandidatesController(
    PurchaseInvoicesGetComplementCandidatesService service) : ODataController
{
    /// <summary>
    /// Rota declarada à mão, como todas as funções deste projeto (a não declarada toma 404). Rotas por atributo
    /// entregam o segmento COM as aspas simples do OData.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/PurchaseInvoicesGetComplementCandidates(CardCode={cardCode},BranchCode={branchCode})")]
    public async Task<ActionResult<IEnumerable<PurchaseInvoiceComplementCandidateDto>>> Get(
        [FromRoute] string cardCode, [FromRoute] string branchCode) =>
        Ok(await service.ExecuteAsync(cardCode.Trim('\''), branchCode.Trim('\'')));
}
```

`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, depois de `services.AddScoped<PurchaseInvoicesGetOriginItemsService>();`:

```csharp
        services.AddScoped<PurchaseInvoicesGetComplementCandidatesService>();
```

`git add` os três arquivos novos.

- [ ] **Step 6: Rodar e gates**

```bash
dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesGetComplementCandidatesTests|FullyQualifiedName~PurchaseInvoiceValueComplementModelTests|FullyQualifiedName~PurchaseInvoiceNfeEdmModelTests"
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
```

Expected: PASS; build OK; suíte inteira verde (anotar a contagem).

- [ ] **Step 7: Stage**

```bash
git add SiagroB1.Domain/Dtos/PurchaseInvoiceComplementCandidateDto.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesService.cs SiagroB1.Web/Functions/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesGetComplementCandidatesTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementModelTests.cs
```

O controlador commita com:

```
feat(invoice): itens elegíveis para o complemento de valor próprio

Function que lista os itens das entradas próprias autorizadas (inclusive contra-nota)
do produtor e da filial do cabeçalho, para a tela escolher o item da nota original.
Item sem contrato também entra, com o contrato vazio: o operador escolhe na linha.
DTO PascalCase, valores em double.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 4: Fiscal — finalidade complementar (finNFe=2)

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs:79-87` (enum `NfePurpose`)
- Modify: `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs:111`
- Modify (test support): `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs` (depois de `PurchaseReturnInput()`)
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs` (depois de `Purchase_return_pays_nothing_and_has_no_billing`)

**Interfaces:**
- Consumes: nada.
- Produces: `NfePurpose.Complement` (valor novo, no fim do enum) → `ide/finNFe = FinalidadeNFe.fnComplementar`.

- [ ] **Step 1: Teste que falha**

`SiagroB1.Fiscal.Tests/Support/NfeTestData.cs`, depois do método `PurchaseReturnInput()`:

```csharp

    /// <summary>
    /// NF-e complementar própria de entrada (spec 2026-10-14, finalidade 2): só valor — quantidade e preço zero, vProd =
    /// valor do complemento —, as notas originais no NFref do cabeçalho e nada no item (não é devolução).
    /// </summary>
    public static NfeIssueInput PurchaseComplementInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "COMPRA DE MERCADORIA",
        Direction = NfeDirection.Incoming,
        Purpose = NfePurpose.Complement,
        ReferencedKeys = [EntryAccessKey, ProducerAccessKey],
        Recipient = NonTaxpayerProducer(),
        Items = [Item() with { Cfop = "1102", PisCst = "74", CofinsCst = "74", Quantity = 0m, UnitPrice = 0m, Total = 60000m }],
    };
```

`SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs`, depois do teste `Purchase_return_pays_nothing_and_has_no_billing`:

```csharp

    // --- NF-e complementar própria (spec 2026-10-14) ---

    [Fact]
    public void Purchase_complement_is_an_incoming_complement_referenced_only_in_the_header()
    {
        var nfe = Build(NfeTestData.PurchaseComplementInput());

        Assert.Equal(TipoNFe.tnEntrada, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnComplementar, nfe.infNFe.ide.finNFe);
        Assert.Equal([NfeTestData.EntryAccessKey, NfeTestData.ProducerAccessKey],
            nfe.infNFe.ide.NFref.Select(r => r.refNFe).ToArray());
        var det = Assert.Single(nfe.infNFe.det);
        Assert.Null(det.DFeReferenciado);
    }

    [Fact]
    public void Purchase_complement_item_has_value_only()
    {
        var prod = Assert.Single(Build(NfeTestData.PurchaseComplementInput()).infNFe.det).prod;

        Assert.Equal(0m, prod.qCom);
        Assert.Equal(0m, prod.vUnCom);
        Assert.Equal(0m, prod.qTrib);
        Assert.Equal(0m, prod.vUnTrib);
        Assert.Equal(60000m, prod.vProd);
        Assert.Equal("KG", prod.uCom);
        Assert.Equal("KG", prod.uTrib);
    }

    [Fact]
    public void Purchase_complement_serializes_finnfe_2()
    {
        Assert.Contains("<finNFe>2</finNFe>", Build(NfeTestData.PurchaseComplementInput()).ObterXmlString());
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeXmlBuilderTests"`
Expected: erro de compilação (`NfePurpose.Complement` não existe).

- [ ] **Step 3: Enum**

`SiagroB1.Fiscal/Nfe/NfeIssueInput.cs:79-87` — trocar

```csharp
/// <summary>
/// Finalidade da NF-e (<c>ide/finNFe</c>): normal (1) ou devolução (4). Não decide o sentido: a devolução
/// de venda é entrada e a devolução de compra é saída (<see cref="NfeDirection"/>).
/// </summary>
public enum NfePurpose
{
    Normal,
    Return,
}
```

por

```csharp
/// <summary>
/// Finalidade da NF-e (<c>ide/finNFe</c>): normal (1), devolução (4) ou complementar (2, spec 2026-10-14: complemento de
/// valor próprio, que referencia as notas originais no NFref). Não decide o sentido: a devolução de venda é entrada e a
/// devolução de compra é saída (<see cref="NfeDirection"/>). Valor novo sempre no fim.
/// </summary>
public enum NfePurpose
{
    Normal,
    Return,
    Complement,
}
```

- [ ] **Step 4: Builder**

`SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs:111` — trocar

```csharp
        finNFe = input.Purpose == NfePurpose.Return ? FinalidadeNFe.fnDevolucao : FinalidadeNFe.fnNormal,
```

por

```csharp
        finNFe = input.Purpose switch
        {
            NfePurpose.Return => FinalidadeNFe.fnDevolucao,
            NfePurpose.Complement => FinalidadeNFe.fnComplementar,
            _ => FinalidadeNFe.fnNormal,
        },
```

(O item já sai de `NfeItem`: `qCom/qTrib = Quantity`, `vUnCom/vUnTrib = UnitPrice`, `vProd = Total`, `uCom/uTrib = UnitOfMeasure` — `:259-266`; nada a mudar. ⚠️ Risco do spec, regra 11: se a SEFAZ-SP aplicar a 629 (qCom × vUnCom = vProd) à finalidade 2, o ajuste é no montador — fica para a Task 9 registrar.)

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Fiscal.Tests`
Expected: todos PASS (os existentes provam Normal e Devolução intactos).

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Fiscal/Nfe/NfeIssueInput.cs SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs SiagroB1.Fiscal.Tests/Support/NfeTestData.cs SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs
```

O controlador commita com:

```
feat(invoice): finalidade complementar na NF-e

O montador do XML ganha a finalidade 2 (complementar) para a NF-e de complemento de
valor própria. As notas originais vão no NFref do cabeçalho e o item não leva
DFeReferenciado, porque não é devolução; quantidade e preço saem zerados e o vProd é o
valor do complemento.

Atenção: a validação qCom × vUnCom = vProd (rejeição 629) não se aplica à finalidade 2
pelo leiaute; se a SEFAZ-SP rejeitar mesmo assim, o ajuste é no montador.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 5: Emissão do complemento próprio (tipo emitível, tributação, prontidão C6, montador)

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs:35-48`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:12-39`
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs:14-21, 48-58, 178-183` (+ método novo)
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueContext.cs:13-33`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs:54-58, 67-97, 99-110, 131`
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeValueComplementIssueTests.cs`

**Interfaces:**
- Consumes: Task 2 (`IsComplementableOrigin`, `NoLongerAuthorizedMessage`, `MissingOriginalMessage`, `ValueComplementNfeSeed`), Task 4 (`NfePurpose.Complement`); dos testes existentes: `PurchaseInvoicesNfeIssueServiceTests.Issue(PurchaseNfeScenario, FakeNfeSefazClient, FakeNfeNumberReservationService?)` e `.ReloadAsync(PurchaseNfeScenario, Guid?)` (ambos `internal static`).
- Produces:
  - `NfeIssueContext.ComplementReferencedKeys` (`IReadOnlyList<string>?`, último parâmetro opcional).
  - `NfeReadinessRequest.RequiresWeights` (`bool`, padrão `true`).
  - `PurchaseInvoicesTaxApplyService.IsActiveForAsync` verdadeiro para `Own + ValueComplement`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeValueComplementIssueTests.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Spec 2026-10-14, regras 6–12: o complemento de valor próprio sai como NF-e complementar (finNFe=2).</summary>
public class PurchaseInvoicesNfeValueComplementIssueTests
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    private static async Task<PurchaseInvoice> CreateAsync(
        ValueComplementScenario s, params (Guid? OriginalItemKey, decimal Amount)[] lines)
    {
        var invoice = s.Complement(lines);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        return invoice;
    }

    private static decimal Value(XElement prod, string name) =>
        decimal.Parse(prod.Element(Nfe + name)!.Value, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Own_complement_is_taxed_like_the_own_entry()
    {
        var s = await SeedAsync();

        var invoice = await CreateAsync(s, (s.OriginalItemKey, 300m));

        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(l => l.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal("1102", line.Cfop);
        Assert.Equal("51", line.CstIcms);
        Assert.Equal(300m, line.IcmsBase);
    }

    [Fact]
    public async Task Own_complement_is_issued_as_a_complementary_nfe_referencing_the_originals()
    {
        var s = await SeedAsync();
        var (_, second) = await AddOriginalAsync(s.Db, s.Contract, s.PurchaseUsage, SecondNumber, 7, items: 2);
        // nItem = ordem das chaves (NextLineKey crescente): 1ª linha → nota 7, 2ª → nota 5, 3ª → nota 7 de novo.
        var invoice = await CreateAsync(s, (second[0], 300m), (s.OriginalItemKey, 200m), (second[1], 100m));
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, sefaz).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var xml = XDocument.Parse(Assert.Single(sefaz.Sent).Xml);
        var ide = xml.Descendants(Nfe + "ide").Single();
        Assert.Equal("2", ide.Element(Nfe + "finNFe")!.Value);
        Assert.Equal("0", ide.Element(Nfe + "tpNF")!.Value);
        // Chaves DISTINTAS, na ordem de primeira aparição.
        Assert.Equal([AccessKey(7), AccessKey(5)],
            ide.Elements(Nfe + "NFref").Select(r => r.Element(Nfe + "refNFe")!.Value).ToArray());
        Assert.Empty(xml.Descendants(Nfe + "DFeReferenciado"));

        var products = xml.Descendants(Nfe + "det").Select(d => d.Element(Nfe + "prod")!).ToList();
        Assert.Equal([300m, 200m, 100m], products.Select(p => Value(p, "vProd")).ToArray());
        foreach (var prod in products)
        {
            Assert.Equal(0m, Value(prod, "qCom"));
            Assert.Equal(0m, Value(prod, "vUnCom"));
            Assert.Equal(0m, Value(prod, "qTrib"));
            Assert.Equal(0m, Value(prod, "vUnTrib"));
            Assert.Equal("KG", prod.Element(Nfe + "uCom")!.Value);
            Assert.Equal("KG", prod.Element(Nfe + "uTrib")!.Value);
        }

        // Só valor: sem pesos, o montador omite o vol.
        Assert.Empty(xml.Descendants(Nfe + "vol"));
        // natOp = o da natureza da linha (regra 12).
        Assert.Equal("COMPRA DE MERCADORIA", ide.Element(Nfe + "natOp")!.Value);

        // Emitir é confirmar: a confirmação cria um firme por linha (regressão do sub-projeto 2).
        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s.Purchase, invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        var lineKeys = saved.Items.Select(i => (Guid?)i.Key!.Value).ToList();
        Assert.Equal(3, await s.Db.Context.FinancialDocuments.AsNoTracking()
            .CountAsync(d => d.OriginType == FinancialDocumentOrigin.PurchaseInvoice && lineKeys.Contains(d.OriginKey)));
    }

    [Fact]
    public async Task Cancelled_original_blocks_the_emission()
    {
        var s = await SeedAsync();
        var invoice = await CreateAsync(s, (s.OriginalItemKey, 300m));
        await ChangeAsync(s.Db, s.OriginalKey, o =>
        {
            o.NfeStatus = NfeStatus.Cancelled;
            o.InvoiceStatus = InvoiceStatus.Cancelled;
        });
        var sefaz = new FakeNfeSefazClient();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, sefaz).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("A nota original DE000005 não está mais autorizada: o complemento não pode ser emitido.", e.Message);
        Assert.Empty(sefaz.Sent);
    }

    [Fact]
    public async Task Third_party_complement_is_still_not_issued()
    {
        var s = await SeedAsync();
        var invoice = s.Complement((null, 300m));
        invoice.IssuerType = DocumentIssuerType.ThirdParty;
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        s.Db.Context.PurchaseInvoices.Add(invoice);
        await s.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, new FakeNfeSefazClient()).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Só o documento de emissão própria é emitido como NF-e.", e.Message);
    }

    [Fact]
    public async Task Own_entry_without_weights_is_still_refused()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.Purchase.InvoiceKey, i => { i.GrossWeight = 0m; i.NetWeight = 0m; });
        var invoice = await s.Db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Purchase.InvoiceKey);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new NfeReadinessValidator(s.Db, new NfeOptions(NfeTestSeed.Config())).ValidateAsync(invoice));

        Assert.Contains("Documento: peso bruto e peso líquido", e.Message);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesNfeValueComplementIssueTests"`
Expected: FAIL — `Own_complement_is_taxed…` (Cfop vazio: a tributação não roda), os de emissão com "Complemento de valor não é emitido como NF-e por aqui…"; `Third_party…` e `Own_entry_without_weights…` já PASS.

- [ ] **Step 3: Tipo emitível**

`SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs:35-48` — trocar o método inteiro por:

```csharp
    protected override void EnsureIssuableType(PurchaseInvoice invoice)
    {
        if (invoice.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("Só o documento de emissão própria é emitido como NF-e.");

        // Spec 2026-10-14 (regra 6): o complemento de valor próprio sai como NF-e complementar (finNFe=2).
        if (invoice.InvoiceType is not (PurchaseInvoiceType.Normal or PurchaseInvoiceType.ValueComplement) &&
            !PurchaseInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só a entrada Normal e a devolução criada pelo Devolver são emitidas como NF-e por aqui.");
    }
```

E no comentário da classe (`:14-17`) acrescentar ao fim da frase: `; o complemento de valor próprio, como NF-e de ENTRADA complementar (finalidade 2, spec 2026-10-14)`.

- [ ] **Step 4: Tributação**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:34-39` — trocar

```csharp
    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        invoice.ProducerInvoiceKey is null
        && (invoice.IssuerType == DocumentIssuerType.Own
            && (invoice.InvoiceType == PurchaseInvoiceType.Normal || IsOwnNfeReturn(invoice))
```

por

```csharp
    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        invoice.ProducerInvoiceKey is null
        && (invoice.IssuerType == DocumentIssuerType.Own
            // Spec 2026-10-14 (regra 7): o complemento de valor próprio calcula como a entrada própria (base = valor).
            && (invoice.InvoiceType is PurchaseInvoiceType.Normal or PurchaseInvoiceType.ValueComplement || IsOwnNfeReturn(invoice))
```

(o resto da expressão fica igual).

- [ ] **Step 5: Contexto da emissão**

`SiagroB1.Application/Services/Nfe/NfeIssueContext.cs` — no comentário, depois da linha `/// <param name="CustodyReferencedKey">…`:

```csharp
/// <param name="ComplementReferencedKeys">No complemento de valor próprio, as chaves distintas das notas originais, na ordem de primeira aparição (NFref; spec 2026-10-14 regra 10).</param>
```

e trocar o fim do record

```csharp
    NfeCounterNoteOrigin? ProducerInvoice = null,
    string? CustodyReferencedKey = null);
```

por

```csharp
    NfeCounterNoteOrigin? ProducerInvoice = null,
    string? CustodyReferencedKey = null,
    IReadOnlyList<string>? ComplementReferencedKeys = null);
```

- [ ] **Step 6: Prontidão (C6, pesos)**

`SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs`:

No comentário do record (`:14-17`), acrescentar:

```csharp
/// <param name="RequiresWeights">Falso no complemento de valor: nada é transportado (o montador omite o vol com peso 0).</param>
```

e trocar a assinatura (`:18-21`)

```csharp
public sealed record NfeReadinessRequest(
    string? BranchCode, string PartnerCode, string PartnerLabel, string? DeliveryCardCode, string? TruckingCompanyCode,
    string? TruckCode, decimal GrossWeight, decimal NetWeight, int? PaymentConditionCode, bool RequiresPayment,
    NfeDirection Direction, IReadOnlyList<INfeTaxedLine> Lines, bool RequiresProductAndUnit);
```

por

```csharp
public sealed record NfeReadinessRequest(
    string? BranchCode, string PartnerCode, string PartnerLabel, string? DeliveryCardCode, string? TruckingCompanyCode,
    string? TruckCode, decimal GrossWeight, decimal NetWeight, int? PaymentConditionCode, bool RequiresPayment,
    NfeDirection Direction, IReadOnlyList<INfeTaxedLine> Lines, bool RequiresProductAndUnit, bool RequiresWeights = true);
```

Trocar `ValidateAsync(PurchaseInvoice)` (`:43-58`, comentário incluído) por:

```csharp
    /// <summary>
    /// Entrada própria (destinatário = fornecedor, CFOP 1xxx/2xxx, condição de pagamento) ou devolução de compra
    /// (CFOP 5xxx/6xxx, sem pagamento, a entrada de origem conferida). Produto e unidade em toda linha: na
    /// entrada os dois são anuláveis (nota de terceiro pode trazer código fora do cadastro). Complemento de valor próprio
    /// (spec 2026-10-14): as notas originais recarregadas (C6) e sem pesos.
    /// </summary>
    public async Task<NfeIssueContext> ValidateAsync(PurchaseInvoice invoice)
    {
        // Regra 8 / C6: a regra 2 vale no rascunho, mas a original pode ter sido cancelada antes da transmissão. Recusa
        // direta (não é "dado faltando"), antes de qualquer reserva de número.
        var complementKeys = invoice.InvoiceType == PurchaseInvoiceType.ValueComplement
            ? await LoadComplementReferencesAsync(invoice)
            : null;

        var context = await ValidateCoreAsync(
            new NfeReadinessRequest(
                invoice.BranchCode, invoice.CardCode, "Fornecedor", DeliveryCardCode: null, invoice.TruckingCompanyCode,
                invoice.TruckCode, invoice.GrossWeight, invoice.NetWeight, invoice.PaymentConditionCode,
                // Devolução e nota de custódia (armazenagem/beneficiamento) não têm pagamento (tPag 90).
                RequiresPayment: !invoice.IsNfeReturn && invoice.CustodyRole == StorageCustodyRole.None,
                Direction: invoice.IsNfeReturn ? NfeDirection.Outgoing : NfeDirection.Incoming,
                Lines: invoice.Items.Cast<INfeTaxedLine>().ToList(),
                RequiresProductAndUnit: true,
                RequiresWeights: invoice.InvoiceType != PurchaseInvoiceType.ValueComplement),
            invoice.IsNfeReturn ? problems => LoadPurchaseReturnOriginAsync(invoice, problems) : null);

        return complementKeys is null ? context : context with { ComplementReferencedKeys = complementKeys };
    }
```

Trocar o bloco dos pesos (`:178-183`)

```csharp
        // Volume da NF-e: só os pesos são obrigatórios (quantidade, espécie, marca e numeração não).
        // Informados, não derivados da quantidade: a unidade pode ser saco, bag, caixa...
        if (request.GrossWeight <= 0 || request.NetWeight <= 0)
            problems.Add("Documento: peso bruto e peso líquido");
        else if (request.GrossWeight < request.NetWeight)
            problems.Add("Documento: o peso bruto não pode ser menor que o peso líquido");
```

por

```csharp
        // Volume da NF-e: só os pesos são obrigatórios (quantidade, espécie, marca e numeração não).
        // Informados, não derivados da quantidade: a unidade pode ser saco, bag, caixa... O complemento de valor não
        // transporta nada (RequiresWeights falso); peso informado nele continua conferido.
        if (request.RequiresWeights && (request.GrossWeight <= 0 || request.NetWeight <= 0))
            problems.Add("Documento: peso bruto e peso líquido");
        else if (request.GrossWeight < request.NetWeight)
            problems.Add("Documento: o peso bruto não pode ser menor que o peso líquido");
```

E acrescentar, logo antes de `private Task<BusinessPartner?> LoadPartnerAsync(string cardCode) =>`:

```csharp
    /// <summary>
    /// Complemento de valor próprio (spec 2026-10-14, regras 8 e 10): recarrega a nota de cada linha, recusa a que
    /// deixou de estar autorizada (C6) e devolve as chaves DISTINTAS, na ordem de primeira aparição pelo nItem.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadComplementReferencesAsync(PurchaseInvoice invoice)
    {
        var lines = NfeItemNumbering.Ordered(invoice.Items);
        var keys = lines.Where(l => l.ComplementedItemKey is not null).Select(l => l.ComplementedItemKey!.Value).Distinct().ToList();
        var originals = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);

        var references = new List<string>();
        foreach (var line in lines)
        {
            if (line.ComplementedItemKey is not { } key || !originals.TryGetValue(key, out var original) ||
                original.PurchaseInvoice is not { } origin)
                throw new DefaultException(PurchaseInvoiceValueComplement.MissingOriginalMessage(line.ItemCode));

            if (!PurchaseInvoiceValueComplement.IsComplementableOrigin(origin) || origin.ChaveNFe is not { Length: 44 } accessKey)
                throw new DefaultException(PurchaseInvoiceValueComplement.NoLongerAuthorizedMessage(origin.InvoiceNumber));

            if (!references.Contains(accessKey))
                references.Add(accessKey);
        }

        return references;
    }
```

E no topo do arquivo: `using SiagroB1.Application.Services.PurchaseInvoices;`.

- [ ] **Step 7: Montador**

`SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs`:

No `Build(SalesInvoice …)` (`:54-58`), trocar

```csharp
            returnOrigin is null ? NfeDirection.Outgoing : NfeDirection.Incoming,
            returnOrigin, referencedKeys,
```

por

```csharp
            returnOrigin is null ? NfeDirection.Outgoing : NfeDirection.Incoming,
            returnOrigin is null ? NfePurpose.Normal : NfePurpose.Return,
            returnOrigin, referencedKeys,
```

Trocar `Build(PurchaseInvoice …)` inteiro (`:67-97`, comentário incluído) por:

```csharp
    /// <summary>
    /// Entrada própria: ENTRADA normal, destinatário = fornecedor; a contra-nota leva no NFref a chave da escrituração do
    /// produtor e o Funrural dela no infCpl (spec 2026-10-10 §6). Devolução de compra: SAÍDA com finalidade 4, o item da
    /// entrada no DFeReferenciado, sem pagamento. Complemento de valor próprio: ENTRADA com finalidade 2 e as notas
    /// originais no NFref do cabeçalho (spec 2026-10-14, regras 9–10).
    /// </summary>
    public static NfeIssueInput Build(
        PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a entrada de origem.")
            : null;

        var complement = returnOrigin is null && invoice.InvoiceType == PurchaseInvoiceType.ValueComplement;
        var producer = returnOrigin is null && !complement ? context.ProducerInvoice : null;

        IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey]
            : complement ? context.ComplementReferencedKeys ?? throw new DefaultException("O complemento está sem as notas originais.")
            : producer is not null ? [producer.AccessKey]
            : [];

        var purpose = returnOrigin is not null ? NfePurpose.Return
            : complement ? NfePurpose.Complement
            : NfePurpose.Normal;

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            returnOrigin is null ? NfeDirection.Incoming : NfeDirection.Outgoing,
            purpose, returnOrigin, referencedKeys,
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((PurchaseInvoiceItem)line).PurchaseInvoiceItemOriginKey,
            invoice.FreightTerms, invoice.NetWeight, invoice.GrossWeight,
            new NfeVolume(invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            invoice.TaxPayerComments, TaxComments: null,
            producer is not null ? producer.FunruralText : PurchaseInvoiceFunrural.Text(invoice),
            returnOrigin is null ? "COMPRA" : "DEVOLUCAO DE COMPRA",
            WithoutPayment: returnOrigin is not null || invoice.CustodyRole != StorageCustodyRole.None), context, issuedAt, technicalResponsible);
    }
```

No record `NfeDocumentView` (`:104-110`), trocar

```csharp
        NfeDirection Direction, NfeReturnOrigin? ReturnOrigin, IReadOnlyList<string> HeaderReferencedKeys,
```

por

```csharp
        NfeDirection Direction, NfePurpose Purpose, NfeReturnOrigin? ReturnOrigin, IReadOnlyList<string> HeaderReferencedKeys,
```

e em `Build(NfeDocumentView …)` (`:131`) trocar

```csharp
            Purpose = returnOrigin is null ? NfePurpose.Normal : NfePurpose.Return,
```

por

```csharp
            Purpose = view.Purpose,
```

(O complemento não tem `ReturnOrigin`: os itens saem sem `Reference`/`DFeReferenciado` e o NFref vai no cabeçalho — `NfeXmlBuilder.cs:121-125`. O natOp continua o da natureza da primeira linha — regra 12.)

- [ ] **Step 8: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesNfeValueComplementIssueTests|FullyQualifiedName~PurchaseInvoicesNfeIssueServiceTests|FullyQualifiedName~NfeIssueInputAssemblerTests|FullyQualifiedName~NfeReadinessValidatorTests|FullyQualifiedName~PurchaseInvoicesNfeReturnTests|FullyQualifiedName~PurchaseInvoicesCounterNoteIssueTests|FullyQualifiedName~SalesInvoicesNfeIssueServiceTests|FullyQualifiedName~PurchaseInvoicesTaxApplyTests"`
Expected: todos PASS.

- [ ] **Step 9: Gates**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet test SiagroB1.Fiscal.Tests
```

Expected: build OK; as duas suítes verdes (anotar as contagens).

- [ ] **Step 10: Stage**

```bash
git add SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs SiagroB1.Application/Services/Nfe/NfeIssueContext.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeValueComplementIssueTests.cs
```

O controlador commita com:

```
feat(invoice): emitir o complemento de valor próprio como NF-e complementar

O complemento de valor de emissão própria passa a ser tributado e emitido como a
entrada própria, com finalidade 2: o NFref leva as chaves distintas das notas
originais, na ordem dos itens, e os itens saem só com valor. Na emissão a prontidão
recarrega cada original e recusa a que deixou de estar autorizada. O complemento não
exige pesos, porque nada é transportado.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 6: Travas na original (C8) e complemento sem devolução (C9)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs` (método novo)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs:32, 53, 63-66`
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs:127-131`
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoiceValueComplementLocksTests.cs`

**Interfaces:**
- Consumes: Task 2 (`ValueComplementNfeSeed`); `NfeCancelTestServices.PurchaseCancel(UnitOfWork, FakeNfeSefazClient, PurchaseInvoicesCancelService?)`.
- Produces: `PurchaseInvoiceValueComplement.EnsureNoActiveComplementAsync(AppDbContext context, Guid originalKey, string? originalNumber)`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Nfe/PurchaseInvoiceValueComplementLocksTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Spec 2026-10-14, D4 e regras 13–14: a original não é cancelada com complemento ativo; o complemento não é devolvido.</summary>
public class PurchaseInvoiceValueComplementLocksTests
{
    private const string Reason = "Entrada lancada em duplicidade";
    private const string Locked = "A nota DE000005 tem o complemento ST-0001 ativo: cancele o complemento antes.";

    private static async Task<PurchaseInvoice> ComplementAsync(ValueComplementScenario s)
    {
        var invoice = s.Complement((s.OriginalItemKey, 300m));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        return invoice;
    }

    private static PurchaseInvoicesNfeReturnCreateService Returns(ValueComplementScenario s) =>
        new(s.Db, new TaxCalculationGate(s.Db, NfeTestSeed.Config()), Create(s.Db), NfeTestSeed.Clock,
            NfeIssueInputAssembler.BrasiliaZone);

    [Fact]
    public async Task Nfe_cancel_of_an_original_with_an_active_complement_is_refused_before_sefaz()
    {
        var s = await SeedAsync();
        await ComplementAsync(s);
        var sefaz = new FakeNfeSefazClient();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCancel(s.Db, sefaz).ExecuteAsync(s.OriginalKey, Reason, "tester"));

        Assert.Equal(Locked, e.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Document_cancel_rehearsal_of_the_original_is_refused()
    {
        var s = await SeedAsync();
        await ComplementAsync(s);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(s.Db).EnsureCanCancelAsync(s.OriginalKey));

        Assert.Equal(Locked, e.Message);
    }

    [Fact]
    public async Task Complement_lock_comes_before_the_return_lock()
    {
        // O caso da nº 5 da CEAGUI dev: a devolução DE000002 (Rejeitada, Pendente) ainda aponta a original.
        var s = await SeedAsync();
        await ComplementAsync(s);
        s.Db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Pending,
            NfeStatus = NfeStatus.Rejected, PurchaseInvoiceOriginKey = s.OriginalKey,
        });
        await s.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(s.Db).EnsureCanCancelAsync(s.OriginalKey));

        Assert.Equal(Locked, e.Message);
    }

    [Fact]
    public async Task Cancelled_complement_releases_the_original()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, complement.Key, c => c.InvoiceStatus = InvoiceStatus.Cancelled);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await PurchaseCancel(s.Db, sefaz).ExecuteAsync(s.OriginalKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
    }

    [Fact]
    public async Task Complement_has_no_return()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(s).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(
                complement.Key, [new PurchaseInvoiceNfeReturnItem(complement.Items.Single().Key!.Value, 1m)], "grão fora do padrão"),
            "tester"));

        Assert.Equal("Complemento de valor não tem devolução.", e.Message);
    }

    [Fact]
    public async Task Original_is_still_returned_alone()
    {
        var s = await SeedAsync();
        await ComplementAsync(s);

        var returned = await Returns(s).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(
                s.OriginalKey, [new PurchaseInvoiceNfeReturnItem(s.OriginalItemKey, 400m)], "grão fora do padrão"),
            "tester");

        var line = Assert.Single(returned.Items);
        Assert.Equal(s.OriginalItemKey, line.PurchaseInvoiceItemOriginKey);
        Assert.Equal(400m, line.Quantity);
        Assert.Null(line.ComplementedItemKey);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceValueComplementLocksTests"`
Expected: FAIL — os dois de cancelamento passam a cancelar/não recusam; `Complement_lock_comes_before_the_return_lock` recusa com "Documento de entrada possui devolução."; `Complement_has_no_return` falha com a mensagem genérica da origem própria; `Cancelled_complement…` e `Original_is_still_returned_alone` PASS.

- [ ] **Step 3: C8 na regra**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs`, logo depois do método `MissingOriginalMessage`:

```csharp

    /// <summary>
    /// D4 / C8: a original não é cancelada (documento nem NF-e) enquanto houver linha de complemento apontando um item
    /// dela num documento não cancelado — o NFref do complemento ficaria referenciando uma NF-e cancelada. Complemento
    /// cancelado não trava.
    /// </summary>
    public static async Task EnsureNoActiveComplementAsync(AppDbContext context, Guid originalKey, string? originalNumber)
    {
        var complement = await context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.ComplementedItemKey != null &&
                        l.ComplementedItem!.PurchaseInvoiceKey == originalKey &&
                        l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(l => new { l.PurchaseInvoice!.InvoiceNumber })
            .FirstOrDefaultAsync();

        if (complement is not null)
            throw new DefaultException(
                $"A nota {originalNumber} tem o complemento {complement.InvoiceNumber} ativo: cancele o complemento antes.");
    }
```

- [ ] **Step 4: Cancelamento da original**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs`:

Em `EnsureCanCancelAsync` (`:32`) e em `CancelAsync` (`:53`) trocar

```csharp
        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus);
```

por

```csharp
        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus, invoice.InvoiceNumber);
```

Trocar a assinatura (`:63`)

```csharp
    private async Task EnsureBusinessRulesAsync(Guid key, InvoiceStatus status)
```

por

```csharp
    private async Task EnsureBusinessRulesAsync(Guid key, InvoiceStatus status, string? invoiceNumber)
```

e logo depois de `throw new DefaultException("Documento de entrada já está cancelado.");` (`:66`), ANTES da recusa "Documento de entrada possui devolução.":

```csharp

        // Complemento de valor próprio aponta itens desta nota (spec 2026-10-14, D4/C8). Vale para o cancelamento do
        // documento e, pelo ensaio do handler, para o da NF-e — antes de falar com a SEFAZ. Primeira das recusas: a nº 5
        // de referência da CEAGUI dev tem uma devolução Rejeitada ainda ativa, e o roteiro espera ver C8.
        await PurchaseInvoiceValueComplement.EnsureNoActiveComplementAsync(db.Context, key, invoiceNumber);
```

(`PurchaseInvoicesNfeCancelService` não muda: o ensaio `NfeCancelServiceBase.cs:58` chama `PurchaseInvoiceNfeCancellationHandler.EnsureCanCancelAsync` → `PurchaseInvoicesCancelService.EnsureCanCancelAsync`.)

- [ ] **Step 5: C9 no "Devolver"**

`SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs`, no início de `ValidateOriginAsync` (`:127-128`), antes do teste de `CustodyRole`:

```csharp
        // Spec 2026-10-14 (C9): o complemento não tem devolução — devolve-se a original, que leva só a original (o
        // complemento não tem PurchaseInvoiceItemOriginKey e não entra no saldo devolvível). Vale para os dois emissores.
        if (origin.InvoiceType == PurchaseInvoiceType.ValueComplement)
            throw new DefaultException("Complemento de valor não tem devolução.");

```

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceValueComplementLocksTests|FullyQualifiedName~PurchaseInvoicesCancelServiceTests|FullyQualifiedName~PurchaseInvoicesNfeCancelServiceTests|FullyQualifiedName~PurchaseInvoicesNfeReturnTests|FullyQualifiedName~PurchaseInvoicesThirdPartyReturnTests|FullyQualifiedName~PurchaseInvoicesCounterNoteTests"`
Expected: todos PASS.

- [ ] **Step 7: Gates**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: build OK; suíte inteira verde (anotar a contagem); "No changes have been made to the model since the last migration."

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs SiagroB1.Application.Tests/Nfe/PurchaseInvoiceValueComplementLocksTests.cs
```

O controlador commita com:

```
feat(invoice): travar o cancelamento da nota complementada e a devolução do complemento

Cancelar a nota original, documento ou NF-e, é recusado enquanto houver complemento
não cancelado apontando um item dela: o NFref do complemento ficaria referenciando uma
NF-e cancelada. A recusa vem no ensaio, antes de falar com a SEFAZ. O complemento não
tem devolução; devolver a original continua igual e leva só a original.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 7: Frontend — Tipo "Complemento de valor" no modo NF-e e regras de tela

**Repo:** `C:\Projetos\SiagroB1\siagro-b1-frontend` (todos os caminhos abaixo relativos a ele).

**Files:**
- Modify: `webapp/helpers/PurchaseInvoiceNfeHelpers.ts:64-69` (+ funções novas depois de `mustFallBackToNormal`, `:185-187`; `PURCHASE_ITEM_SELECT`, `:193-200`)
- Modify: `webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts`
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml:62-75`
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts:14, 58-70, 101-103, 123-133`

**Interfaces:**
- Consumes: backend Tasks 2–6 (o servidor é quem decide; a tela só não oferece o que voltaria recusado).
- Produces:
  - `isOwnComplement(nfeMode: boolean, invoiceType: string): boolean`
  - `complementCandidatesPath(base: string, cardCode: string, branchCode: string): string`
  - `canIssuePurchaseNfe` aceita `InvoiceType === "ValueComplement"`.
  - `PURCHASE_ITEM_SELECT` inclui `ComplementedItemKey`.
  - `ui>/ownComplement` (boolean), `ui>/complementedItemLabels`, `ui>/inheritedContracts` e `ui>/contractCodes` (`Record<string, string>`, zerados no reset) escritos por `refreshNfeMode`.

- [ ] **Step 1: Branch**

```bash
git -C /c/Projetos/SiagroB1/siagro-b1-frontend status --short
git -C /c/Projetos/SiagroB1/siagro-b1-frontend log --oneline -1 main
git -C /c/Projetos/SiagroB1/siagro-b1-frontend checkout -b feature/nfe-value-complement main
git -C /c/Projetos/SiagroB1/siagro-b1-frontend branch --show-current
```

Expected: working tree limpo; `main` em `889d2da`; branch atual `feature/nfe-value-complement`. Árvore suja: parar e perguntar.

- [ ] **Step 2: Testes que falham**

`webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts` — no import do topo, acrescentar `complementCandidatesPath, isOwnComplement` e `PURCHASE_ITEM_SELECT` à lista; e no fim do arquivo:

```ts

QUnit.module("PurchaseInvoiceNfeHelpers - complemento de valor próprio (spec 2026-10-14)");

QUnit.test("emite o complemento de valor próprio pendente, sem NF-e ou rejeitada", function (assert) {
	const complement = { ...pending, InvoiceType: "ValueComplement" };
	assert.strictEqual(canIssuePurchaseNfe(complement, true), true);
	assert.strictEqual(canIssuePurchaseNfe({ ...complement, NfeStatus: "Rejected" }, true), true);
	assert.strictEqual(canIssuePurchaseNfe({ ...complement, NfeStatus: "Authorized" }, true), false);
	assert.strictEqual(canIssuePurchaseNfe(complement, false), false, "terceiro ou filial sem NF-e");
});

QUnit.test("o complemento próprio calcula tributos como a entrada própria", function (assert) {
	assert.strictEqual(isPurchaseTaxMode(true, "Own", "ValueComplement"), true);
	assert.strictEqual(isPurchaseTaxMode(false, "Own", "ValueComplement"), false);
});

QUnit.test("o complemento não oferece Devolver", function (assert) {
	const authorized = { ...pending, InvoiceType: "ValueComplement", InvoiceStatus: "Confirmed", NfeStatus: "Authorized" };
	assert.strictEqual(canReturnPurchase(authorized, true), false);
});

QUnit.test("no modo NF-e o Complemento de valor não volta para Normal", function (assert) {
	assert.strictEqual(mustFallBackToNormal("ValueComplement", false, true, true), false);
});

QUnit.test("complemento próprio só no modo NF-e e no tipo Complemento de valor", function (assert) {
	assert.strictEqual(isOwnComplement(true, "ValueComplement"), true);
	assert.strictEqual(isOwnComplement(false, "ValueComplement"), false, "terceiro ou filial sem NF-e");
	assert.strictEqual(isOwnComplement(true, "Normal"), false);
});

QUnit.test("caminho da function de candidatos com aspas e códigos codificados", function (assert) {
	assert.strictEqual(
		complementCandidatesPath("/odata/PurchaseInvoicesGetComplementCandidates", "F 01", "01"),
		"/odata/PurchaseInvoicesGetComplementCandidates(CardCode='F%2001',BranchCode='01')");
});

QUnit.test("as linhas do Detail/Edit trazem o item da nota original", function (assert) {
	assert.ok(PURCHASE_ITEM_SELECT.split(",").includes("ComplementedItemKey"));
});
```

- [ ] **Step 3: Rodar e ver falhar**

Dev server parado. Na raiz do frontend: `yarn ts-typecheck`
Expected: erro — `isOwnComplement`/`complementCandidatesPath` não exportados.

- [ ] **Step 4: Helpers**

`webapp/helpers/PurchaseInvoiceNfeHelpers.ts:64-69` — trocar

```ts
/** Entrada própria Normal ou devolução de compra, Pendente, sem NF-e ou com a anterior rejeitada. */
export function canIssuePurchaseNfe(doc: PurchaseNfeState, nfeMode: boolean): boolean {
	const issuable = doc.InvoiceType === "Normal" || (doc.InvoiceType === "Return" && doc.IsNfeReturn === true);
```

por

```ts
/**
 * Entrada própria Normal, complemento de valor próprio (NF-e complementar, spec 2026-10-14) ou devolução de compra,
 * Pendente, sem NF-e ou com a anterior rejeitada.
 */
export function canIssuePurchaseNfe(doc: PurchaseNfeState, nfeMode: boolean): boolean {
	const issuable = doc.InvoiceType === "Normal" || doc.InvoiceType === "ValueComplement" ||
		(doc.InvoiceType === "Return" && doc.IsNfeReturn === true);
```

Depois de `mustFallBackToNormal` (`:185-187`):

```ts

/**
 * Complemento de valor de EMISSÃO PRÓPRIA no modo NF-e (spec 2026-10-14): cada linha nasce de um item de uma entrada
 * própria autorizada ("Incluir Item" abre a lista) e herda produto, unidade e natureza, travados na grade (o contrato
 * também, quando o original tem: D5).
 */
export function isOwnComplement(nfeMode: boolean, invoiceType: string): boolean {
	return nfeMode === true && invoiceType === "ValueComplement";
}

/** URL da function de candidatos: strings OData entre aspas simples, códigos codificados para a URL. */
export function complementCandidatesPath(base: string, cardCode: string, branchCode: string): string {
	return `${base}(CardCode='${encodeURIComponent(cardCode ?? "")}',BranchCode='${encodeURIComponent(branchCode ?? "")}')`;
}
```

Em `PURCHASE_ITEM_SELECT` (`:195`), trocar `"SalesInvoiceItemKey,PurchaseContractKey,PurchaseInvoiceItemOriginKey,UsageCode,UsageName,Cfop,Ncm,GoodsOrigin," +` por

```ts
	"SalesInvoiceItemKey,PurchaseContractKey,PurchaseInvoiceItemOriginKey,ComplementedItemKey,UsageCode,UsageName,Cfop,Ncm,GoodsOrigin," +
```

- [ ] **Step 5: Tipo escolhível no modo NF-e**

`webapp/view/purchaseInvoices/fragments/Form.fragment.xml:62-75` — trocar

```xml
          <!-- No modo NF-e (Emissão Própria na filial que emite pelo Siagro) o tipo fica em Normal: a devolução de
               compra própria só nasce pelo "Devolver" (o servidor recusa a manual). A devolução do cliente (De terceiro)
               e a filial sem NF-e seguem escolhendo. -->
          <Label text="Tipo" />
          <Select
            selectedKey="{path: 'InvoiceType', targetType: 'any'}"
            change=".onInvoiceTypeChange"
            enabled="{= ${ui>/typeEditable} === true &amp;&amp; ${ui>/nfeMode} !== true }"
            forceSelection="false"
            width="100%">
            <core:Item key="Normal" text="Normal" />
            <core:Item key="Return" text="Devolução" />
            <core:Item key="ValueComplement" text="Complemento de valor" />
          </Select>
```

por

```xml
          <!-- No modo NF-e (Emissão Própria na filial que emite pelo Siagro) o tipo é Normal ou Complemento de valor
               (NF-e complementar, spec 2026-10-14): a devolução de compra própria só nasce pelo "Devolver" (o servidor
               recusa a manual), por isso o item Devolução fica desabilitado nesse modo, e uma Devolução escolhida antes
               volta para Normal (mustFallBackToNormal). A devolução do cliente (De terceiro) e a filial sem NF-e seguem
               escolhendo os três. -->
          <Label text="Tipo" />
          <Select
            selectedKey="{path: 'InvoiceType', targetType: 'any'}"
            change=".onInvoiceTypeChange"
            enabled="{= ${ui>/typeEditable} === true }"
            forceSelection="false"
            width="100%">
            <core:Item key="Normal" text="Normal" />
            <core:Item key="Return" text="Devolução" enabled="{= ${ui>/nfeMode} !== true }" />
            <core:Item key="ValueComplement" text="Complemento de valor" />
          </Select>
```

- [ ] **Step 6: Flag `ui>/ownComplement`**

`webapp/controller/purchaseInvoices/BaseController.ts`:

Trocar o import (`:14`)

```ts
import { isPurchaseNfeMode, isPurchaseTaxMode, mustFallBackToNormal, requiresSupplierKey } from "siagrob1/helpers/PurchaseInvoiceNfeHelpers";
```

por

```ts
import { isOwnComplement, isPurchaseNfeMode, isPurchaseTaxMode, mustFallBackToNormal, requiresSupplierKey } from "siagrob1/helpers/PurchaseInvoiceNfeHelpers";
```

No bloco do reset de `refreshNfeMode`, depois de `uiModel.setProperty("/valueComplement", false);` (`:64`):

```ts
      // Complemento de valor próprio (spec 2026-10-14): a flag e os mapas que a grade lê ("Nota original", contrato
      // herdado e travado (D5) e o código do contrato da linha nova, que não tem a navegação). Do componente: zerados
      // a cada documento.
      uiModel.setProperty("/ownComplement", false);
      uiModel.setProperty("/complementedItemLabels", {});
      uiModel.setProperty("/inheritedContracts", {});
      uiModel.setProperty("/contractCodes", {});
```

Depois de `uiModel.setProperty("/valueComplement", invoiceType === "ValueComplement");` (`:103`):

```ts
      // Complemento de valor próprio (spec 2026-10-14): "Incluir Item" abre a lista de itens das notas originais.
      uiModel.setProperty("/ownComplement", isOwnComplement(nfeMode, invoiceType));
```

No `catch` (`:123-133`), depois de `uiModel.setProperty("/isCounterNote", false);`:

```ts
        uiModel.setProperty("/ownComplement", false);
```

- [ ] **Step 7: Gates**

Dev server parado. Na raiz do frontend:

```bash
python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" webapp/view/purchaseInvoices/fragments/Form.fragment.xml
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Expected: parse sem saída; sem erro (ui5lint sem regra nova). QUnit: com `yarn start`, abrir a URL do unitTests (Global Constraints) e conferir o módulo "complemento de valor próprio" verde; derrubar o dev server **por PID** (porta 8080).

- [ ] **Step 8: Stage**

```bash
git add webapp/helpers/PurchaseInvoiceNfeHelpers.ts webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts webapp/view/purchaseInvoices/fragments/Form.fragment.xml webapp/controller/purchaseInvoices/BaseController.ts
```

O controlador commita com:

```
feat(invoice): complemento de valor escolhível na emissão própria com NF-e

No modo NF-e o Tipo deixa de ficar travado em Normal: oferece Normal e Complemento de
valor (Devolução segue só pelo Devolver). O complemento próprio pendente ganha Emitir
NF-e, continua calculando tributos e não oferece Devolver. As linhas do Detail e do
Editar passam a trazer o item da nota original.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 8: Frontend — "Incluir Item" escolhe o item da nota original, campos herdados travados e "Nota original"

**Repo:** `C:\Projetos\SiagroB1\siagro-b1-frontend`, branch `feature/nfe-value-complement` (`git branch --show-current` antes de começar).

**Files:**
- Create: `webapp/types/PurchaseInvoiceComplementCandidate.ts`
- Modify: `webapp/helpers/PurchaseInvoiceDraftHelpers.ts:11-33, 136-143` (+ funções novas no fim)
- Modify: `webapp/test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts:9-16` (+ testes novos)
- Modify: `webapp/model/ServerRoutes.ts:214`
- Create: `webapp/view/purchaseInvoices/fragments/ComplementCandidatesDialog.fragment.xml`
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml:43-48, 67-72, 83-89, 236-246`
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts` (imports; `refreshNfeMode`; `onIssuerTypeChange`/`onInvoiceTypeChange`/`clearHiddenAmountColumns` `:221-248`; `openContractValueHelp` `:408-411`; métodos novos)
- Modify: `webapp/controller/purchaseInvoices/Add.controller.ts:202-213`, `webapp/controller/purchaseInvoices/Edit.controller.ts:69-88`

**Interfaces:**
- Consumes: Task 3 (`GET /odata/PurchaseInvoicesGetComplementCandidates(CardCode='…',BranchCode='…')`), Task 7 (`isOwnComplement`, `complementCandidatesPath`, `ui>/ownComplement`, `ui>/complementedItemLabels`, `ui>/contractCodes`).
- Produces: `InvoiceItemPayload.ComplementedItemKey`; `complementLineFrom`, `complementedItemLabel`, `complementedItemLabels`, `inheritedContracts`, `isLineContractEditable`, `formatComplementedItem`, `formatContractCode`, `withEntry` (DraftHelpers); `BaseController.addOwnComplementLine(binding)` e `isOwnComplementDraft()`.

- [ ] **Step 1: Tipo e testes que falham**

`webapp/types/PurchaseInvoiceComplementCandidate.ts`:

```ts
/**
 * Linha da function PurchaseInvoicesGetComplementCandidates (spec 2026-10-14): um item de entrada própria autorizada do
 * fornecedor e da filial do cabeçalho. PascalCase; Quantity/UnitPrice/Total vêm em double, mas passe por Number() antes de
 * somar. LineNumber (nItem) e as chaves podem vir nulos (strictNullChecks desligado: o tipo já admite).
 */
export type PurchaseInvoiceComplementCandidate = {
  ItemKey: string;
  InvoiceKey: string;
  InvoiceNumber: string;
  InvoiceDate: string;
  AccessKey: string;
  LineNumber: number;
  ItemCode: string;
  ItemName: string;
  UnitOfMeasure: string;
  Quantity: number | string;
  UnitPrice: number | string;
  Total: number | string;
  UsageCode: number;
  UsageName: string;
  PurchaseContractKey: string;
  PurchaseContractCode: string;
};
```

`git add webapp/types/PurchaseInvoiceComplementCandidate.ts`.

`webapp/test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts` — trocar o import da linha 1 por

```ts
import {
	blankItemRow, complementedItemLabel, complementedItemLabels, complementLineFrom, draftItemRows, formatComplementedItem,
	formatContractCode, inheritedContracts, isLineContractEditable, missingUnitOfMeasureMessage, suggestedUnitOfMeasure, toNumber,
	unitSuggestionToApply, withEntry
} from "siagrob1/helpers/PurchaseInvoiceDraftHelpers";
import { PurchaseInvoiceComplementCandidate } from "siagrob1/types/PurchaseInvoiceComplementCandidate";
```

no teste "a linha em branco já traz as chaves que a tela edita, nulas" (`:9-16`), trocar `ComplementAmount: 0,` por `ComplementAmount: 0, ComplementedItemKey: null,`; e no fim do arquivo:

```ts

QUnit.module("PurchaseInvoiceDraftHelpers - complemento de valor próprio (spec 2026-10-14)");

const candidate: PurchaseInvoiceComplementCandidate = {
	ItemKey: "item-1", InvoiceKey: "nota-5", InvoiceNumber: "DE000005", InvoiceDate: "2026-10-01T00:00:00",
	AccessKey: "3526", LineNumber: 1, ItemCode: "TRIGO", ItemName: "TRIGO EM GRAOS", UnitOfMeasure: "KG",
	Quantity: 1000, UnitPrice: 1.5, Total: 1500, UsageCode: 4, UsageName: "COMPRA DE MERCADORIA",
	PurchaseContractKey: "contrato-1", PurchaseContractCode: "CC000002",
};

QUnit.test("a linha nasce do item escolhido: herda produto, unidade, natureza e o contrato do original, sem quantidade nem preço", function (assert) {
	assert.deepEqual(complementLineFrom(candidate), {
		...blankItemRow(),
		ItemCode: "TRIGO", ItemName: "TRIGO EM GRAOS", UnitOfMeasureCode: "KG", PurchaseContractKey: "contrato-1",
		UsageCode: 4, UsageName: "COMPRA DE MERCADORIA", ComplementedItemKey: "item-1",
	});
	assert.strictEqual(complementLineFrom(candidate).Quantity, 0);
	assert.strictEqual(complementLineFrom(candidate).UnitPrice, 0);
});

QUnit.test("original sem contrato (D5): a linha nasce sem contrato, para o operador escolher", function (assert) {
	const line = complementLineFrom({ ...candidate, PurchaseContractKey: null, PurchaseContractCode: null });
	assert.strictEqual(line.PurchaseContractKey, null);
	assert.strictEqual(line.ComplementedItemKey, "item-1");
	assert.strictEqual(line.ItemCode, "TRIGO");
});

QUnit.test("contrato herdado trava; original sem contrato deixa editável", function (assert) {
	const withoutContract = { ...candidate, ItemKey: "item-2", PurchaseContractKey: null, PurchaseContractCode: null };
	const inherited = inheritedContracts([candidate, withoutContract]);
	assert.deepEqual(inherited, { "item-1": "contrato-1" });
	assert.strictEqual(isLineContractEditable(true, false, "item-1", inherited), false, "herdado");
	assert.strictEqual(isLineContractEditable(true, false, "item-2", inherited), true, "original sem contrato");
	assert.strictEqual(isLineContractEditable(true, false, null, inherited), true, "linha comum");
	assert.strictEqual(isLineContractEditable(true, false, "item-1", undefined), true, "mapa ainda não carregado: o servidor protege (C10)");
	assert.strictEqual(isLineContractEditable(false, false, "item-2", inherited), false, "Detail");
	assert.strictEqual(isLineContractEditable(true, true, null, inherited), false, "contra-nota");
});

QUnit.test("Nota original = documento/item; sem nItem, interrogação", function (assert) {
	assert.strictEqual(complementedItemLabel("DE000005", 1), "DE000005/1");
	assert.strictEqual(complementedItemLabel("DE000005", null), "DE000005/?");
	assert.deepEqual(complementedItemLabels([candidate]), { "item-1": "DE000005/1" });
	assert.strictEqual(formatComplementedItem("item-1", { "item-1": "DE000005/1" }), "DE000005/1");
	assert.strictEqual(formatComplementedItem("", { "item-1": "DE000005/1" }), "");
	assert.strictEqual(formatComplementedItem("item-2", undefined), "");
});

QUnit.test("código do contrato: o escolhido na tela vale mais que a navegação; sem chave, vazio", function (assert) {
	assert.strictEqual(formatContractCode(undefined, "contrato-1", { "contrato-1": "CC000002" }), "CC000002", "linha nova");
	assert.strictEqual(formatContractCode("CC000001", "contrato-1", { "contrato-1": "CC000002" }), "CC000002", "trocado na tela");
	assert.strictEqual(formatContractCode("CC000001", "contrato-9", {}), "CC000001", "linha gravada");
	assert.strictEqual(formatContractCode("CC000001", null, {}), "", "produto trocado: contrato desamarrado");
});

QUnit.test("withEntry devolve um mapa NOVO (o JSONModel só notifica quando o objeto muda)", function (assert) {
	const before = { a: "1" };
	const after = withEntry(before, "b", "2");
	assert.notStrictEqual(after, before);
	assert.deepEqual(after, { a: "1", b: "2" });
	assert.deepEqual(withEntry(undefined, "", "x"), {});
});
```

- [ ] **Step 2: Rodar e ver falhar**

Dev server parado: `yarn ts-typecheck`
Expected: erro — funções novas não exportadas e `ComplementedItemKey` fora de `InvoiceItemPayload`.

- [ ] **Step 3: Helpers da linha**

`webapp/helpers/PurchaseInvoiceDraftHelpers.ts`:

No topo, depois dos imports existentes (ou na primeira linha, se não houver):

```ts
import { PurchaseInvoiceComplementCandidate } from "siagrob1/types/PurchaseInvoiceComplementCandidate";
```

Em `InvoiceItemPayload`, depois de `ComplementAmount: number;` (`:32`):

```ts
  /** Complemento de valor próprio (spec 2026-10-14): o item da nota original. Nulo nos demais. (strictNullChecks off.) */
  ComplementedItemKey: string;
```

Em `blankItemRow()` (`:141`), trocar `ComplementAmount: 0,` por `ComplementAmount: 0, ComplementedItemKey: null,`.

No fim do arquivo:

```ts

/**
 * Linha nova do complemento de valor próprio (spec 2026-10-14): herda do item escolhido produto, unidade, natureza e o
 * contrato quando o original tem (o servidor herda de novo e recusa divergência); original sem contrato (D5) = contrato
 * nulo, o operador escolhe pelo value help. Sem quantidade nem preço; o operador digita o valor.
 */
export function complementLineFrom(candidate: PurchaseInvoiceComplementCandidate): InvoiceItemPayload {
  return {
    ...blankItemRow(),
    ItemCode: candidate.ItemCode,
    ItemName: candidate.ItemName,
    UnitOfMeasureCode: candidate.UnitOfMeasure,
    PurchaseContractKey: candidate.PurchaseContractKey,
    UsageCode: candidate.UsageCode,
    UsageName: candidate.UsageName,
    ComplementedItemKey: candidate.ItemKey,
  };
}

/** "DE000005/1": número do documento original e nItem do item (o mesmo formato da recusa C4). */
export function complementedItemLabel(invoiceNumber: string, lineNumber: number): string {
  return `${invoiceNumber ?? ""}/${lineNumber ?? "?"}`;
}

/** Mapa item original → rótulo, a partir da function de candidatos (Detail/Edit). */
export function complementedItemLabels(rows: PurchaseInvoiceComplementCandidate[]): Record<string, string> {
  return (rows ?? []).reduce<Record<string, string>>((labels, row) => {
    labels[row.ItemKey] = complementedItemLabel(row.InvoiceNumber, row.LineNumber);
    return labels;
  }, {});
}

/** Item original para contrato herdado, só dos itens que têm contrato (D5): a linha que aponta um deles trava o contrato. */
export function inheritedContracts(rows: PurchaseInvoiceComplementCandidate[]): Record<string, string> {
  return (rows ?? []).reduce<Record<string, string>>((contracts, row) => {
    if (row.PurchaseContractKey) {
      contracts[row.ItemKey] = row.PurchaseContractKey;
    }
    return contracts;
  }, {});
}

/**
 * Célula "Contrato" editável: tela editável, fora da contra-nota e, na linha do complemento próprio, só quando o item
 * original NÃO tem contrato (D5). Mapa ainda não carregado conta como editável: o servidor recusa a troca (C10).
 */
export function isLineContractEditable(
  editable: boolean, isCounterNote: boolean, complementedItemKey: string, inherited: Record<string, string>
): boolean {
  return editable === true && isCounterNote !== true && !(complementedItemKey && inherited?.[complementedItemKey]);
}

/** Coluna "Nota original": o rótulo do item da linha; vazio fora do complemento próprio. */
export function formatComplementedItem(key: string, labels: Record<string, string>): string {
  return key ? labels?.[key] ?? "" : "";
}

/**
 * Coluna "Contrato": o código escolhido na tela (mapa `ui>/contractCodes`, alimentado pelo value help e pela linha do
 * complemento) vale mais que a NAVEGAÇÃO, que não existe na linha nova e fica velha quando o contrato é trocado. Sem
 * chave, vazio (o produto trocado desamarra o contrato).
 */
export function formatContractCode(navigationCode: string, key: string, codes: Record<string, string>): string {
  return key ? codes?.[key] || navigationCode || "" : "";
}

/** Cópia do mapa com a entrada nova: o binding do JSONModel só reavalia quando o objeto muda de identidade. */
export function withEntry(map: Record<string, string>, key: string, value: string): Record<string, string> {
  return key ? { ...(map ?? {}), [key]: value ?? "" } : { ...(map ?? {}) };
}
```

- [ ] **Step 4: Rota**

`webapp/model/ServerRoutes.ts`, depois de `purchaseInvoicesNfeReturnableItems: '/odata/PurchaseInvoicesNfeReturnableItems',` (`:214`):

```ts
  // Complemento de valor próprio (spec 2026-10-14): itens das entradas próprias autorizadas do fornecedor e da filial.
  purchaseInvoicesGetComplementCandidates: '/odata/PurchaseInvoicesGetComplementCandidates',
```

- [ ] **Step 5: Diálogo**

`webapp/view/purchaseInvoices/fragments/ComplementCandidatesDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    core:require="{
      formatter: 'siagrob1/model/formatter'
    }"
>
  <!--
    Item da nota original do complemento de valor próprio (spec 2026-10-14): itens das entradas próprias autorizadas do
    fornecedor e da filial do cabeçalho, pela function PurchaseInvoicesGetComplementCandidates. JSONModel "candidates"
    no próprio diálogo: a lista vem de uma function, não de um entity set, e por isso não passa pelo
    openTableSelectDialog (que filtra um ODataListBinding do modelo padrão). Valores em double: formatDecimal aceita.
  -->
  <TableSelectDialog
      title="Item da nota original"
      noDataText="Nenhuma entrada própria autorizada deste fornecedor nesta filial."
      items="{candidates>/rows}"
      search=".onComplementCandidateSearch"
      contentWidth="64rem">
    <columns>
      <Column><header><Text text="Nota" /></header></Column>
      <Column><header><Text text="Data" /></header></Column>
      <Column width="4rem"><header><Text text="Item" /></header></Column>
      <Column><header><Text text="Produto" /></header></Column>
      <Column hAlign="End"><header><Text text="Quantidade" /></header></Column>
      <Column hAlign="End"><header><Text text="Valor" /></header></Column>
      <Column><header><Text text="Contrato" /></header></Column>
    </columns>
    <ColumnListItem>
      <cells>
        <Text text="{candidates>InvoiceNumber}" wrapping="false" />
        <Text text="{path: 'candidates>InvoiceDate', formatter: 'formatter.formatDate'}" wrapping="false" />
        <Text text="{candidates>LineNumber}" wrapping="false" />
        <ObjectIdentifier title="{candidates>ItemCode}" text="{candidates>ItemName}" />
        <Text text="{path: 'candidates>Quantity', formatter: 'formatter.formatDecimal3'}" wrapping="false" />
        <Text text="{path: 'candidates>Total', formatter: 'formatter.formatDecimal2'}" wrapping="false" />
        <Text text="{candidates>PurchaseContractCode}" wrapping="false" />
      </cells>
    </ColumnListItem>
  </TableSelectDialog>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 6: Grade — travas, "Nota original" e código do contrato**

`webapp/view/purchaseInvoices/fragments/Items.fragment.xml`:

Coluna "Produto" (`:45-46`) — trocar

```xml
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"
            valueHelpOnly="true"
            valueHelpRequest=".openLineItemValueHelp">
```

por

```xml
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true &amp;&amp; !${path: 'ComplementedItemKey', targetType: 'any'} }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true &amp;&amp; !${path: 'ComplementedItemKey', targetType: 'any'} }"
            valueHelpOnly="true"
            valueHelpRequest=".openLineItemValueHelp">
```

Coluna "Natureza" (`:69-72`) — trocar

```xml
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"
            valueHelpOnly="true"
            valueHelpRequest=".openPurchaseUsageValueHelp" />
```

por

```xml
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true &amp;&amp; !${path: 'ComplementedItemKey', targetType: 'any'} }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true &amp;&amp; !${path: 'ComplementedItemKey', targetType: 'any'} }"
            valueHelpOnly="true"
            valueHelpRequest=".openPurchaseUsageValueHelp" />
```

Coluna "UM" (`:86-89`) — trocar

```xml
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"
            valueHelpOnly="true"
            valueHelpRequest=".openUnitsOfMeasureValueHelp" />
```

por

```xml
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true &amp;&amp; !${path: 'ComplementedItemKey', targetType: 'any'} }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true &amp;&amp; !${path: 'ComplementedItemKey', targetType: 'any'} }"
            valueHelpOnly="true"
            valueHelpRequest=".openUnitsOfMeasureValueHelp" />
```

Coluna "Contrato" (`:236-246`) — trocar o `<t:template>` inteiro

```xml
        <t:template>
          <Input
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/isCounterNote} !== true }"
            value="{path: 'PurchaseContract/Code', mode: 'OneWay'}"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/isCounterNote} !== true }"
            valueHelpOnly="true"
            valueHelpRequest=".openContractValueHelp" />
        </t:template>
      </t:Column>
```

por

```xml
        <t:template>
          <!-- O código escolhido na tela (ui>/contractCodes) vale mais que a NAVEGAÇÃO: a linha nova do complemento
               próprio não tem a navegação, e ela fica velha quando o contrato é trocado (draft.formatContractCode).
               Complemento próprio (D5): travado quando o item original tem contrato (herdado), editável pelo value help
               de sempre quando não tem (draft.isLineContractEditable lê ui>/inheritedContracts). -->
          <Input
            editable="{parts: [{path: 'ui>/editable'}, {path: 'ui>/isCounterNote'}, {path: 'ComplementedItemKey', targetType: 'any'}, {path: 'ui>/inheritedContracts'}], formatter: 'draft.isLineContractEditable'}"
            value="{parts: [{path: 'PurchaseContract/Code', mode: 'OneWay'}, {path: 'PurchaseContractKey', mode: 'OneWay'}, {path: 'ui>/contractCodes', mode: 'OneWay'}], formatter: 'draft.formatContractCode', mode: 'OneWay'}"
            showValueHelp="{parts: [{path: 'ui>/editable'}, {path: 'ui>/isCounterNote'}, {path: 'ComplementedItemKey', targetType: 'any'}, {path: 'ui>/inheritedContracts'}], formatter: 'draft.isLineContractEditable'}"
            valueHelpOnly="true"
            valueHelpRequest=".openContractValueHelp" />
        </t:template>
      </t:Column>

      <!-- Complemento de valor próprio (spec 2026-10-14): o item da nota original que a linha complementa, como
           documento/item. Lido do mapa ui>/complementedItemLabels (function de candidatos), não de $expand. -->
      <t:Column label="Nota original" width="9rem" visible="{= ${ui>/ownComplement} === true }">
        <t:template>
          <Text
            wrapping="false"
            text="{parts: [{path: 'ComplementedItemKey', targetType: 'any'}, {path: 'ui>/complementedItemLabels'}], formatter: 'draft.formatComplementedItem'}" />
        </t:template>
      </t:Column>
```

- [ ] **Step 7: Controller comum**

`webapp/controller/purchaseInvoices/BaseController.ts`:

Imports — acrescentar:

```ts
import Fragment from "sap/ui/core/Fragment";
import TableSelectDialog, { TableSelectDialog$ConfirmEvent, TableSelectDialog$SearchEvent } from "sap/m/TableSelectDialog";
import JSONListBinding from "sap/ui/model/json/JSONListBinding";
import ServerRoutes from "siagrob1/model/ServerRoutes";
import { odataCollection, sendJson } from "siagrob1/helpers/FetchHelpers";
import { PurchaseInvoiceComplementCandidate } from "siagrob1/types/PurchaseInvoiceComplementCandidate";
```

trocar o import da linha 14 (deixado pela Task 7) por

```ts
import { complementCandidatesPath, isOwnComplement, isPurchaseNfeMode, isPurchaseTaxMode, mustFallBackToNormal, requiresSupplierKey } from "siagrob1/helpers/PurchaseInvoiceNfeHelpers";
```

e o da linha 17 por

```ts
import {
  complementedItemLabel, complementedItemLabels, complementLineFrom, inheritedContracts, missingUnitOfMeasureMessage, toNumber,
  unitSuggestionToApply, withEntry
} from "siagrob1/helpers/PurchaseInvoiceDraftHelpers";
```

Em `refreshNfeMode`, depois de `const invoiceType = await oContext.requestProperty("InvoiceType") as string;`:

```ts
      const cardCode = await oContext.requestProperty("CardCode") as string;
```

e trocar a linha da Task 7

```ts
      uiModel.setProperty("/ownComplement", isOwnComplement(nfeMode, invoiceType));
```

por

```ts
      const ownComplement = isOwnComplement(nfeMode, invoiceType);
      uiModel.setProperty("/ownComplement", ownComplement);
      if (ownComplement) {
        void this.refreshComplementedItemLabels(cardCode, branchCode, isCurrent);
      }
```

Trocar `onIssuerTypeChange` e `onInvoiceTypeChange` (`:221-230`)

```ts
  onIssuerTypeChange() {
    void this.refreshNfeMode(false);
  }

  /** Trocar o tipo (Normal/Devolução) do terceiro liga ou desliga o cálculo: a devolução do cliente não tem natureza. */
  onInvoiceTypeChange() {
    this.clearHiddenAmountColumns();
    void this.refreshNfeMode(false);
  }
```

por

```ts
  onIssuerTypeChange() {
    void this.refreshNfeMode(false).then(() => this.dropBlankLinesForOwnComplement());
  }

  /** Trocar o tipo (Normal/Devolução) do terceiro liga ou desliga o cálculo: a devolução do cliente não tem natureza. */
  onInvoiceTypeChange() {
    this.clearHiddenAmountColumns();
    void this.refreshNfeMode(false).then(() => this.dropBlankLinesForOwnComplement());
  }

  /**
   * Complemento próprio em digitação (spec 2026-10-14): a linha em branco do rascunho não tem item original e voltaria
   * recusada (C1); as linhas nascem do "Incluir Item".
   */
  private dropBlankLinesForOwnComplement(): void {
    const ui = this.getModel("ui") as JSONModel;
    if (ui.getProperty("/ownComplement") !== true || ui.getProperty("/typeEditable") !== true) {
      return;
    }

    const binding = (this.byId("tablePurchaseInvoiceItems") as Table | undefined)?.getBinding("rows") as ODataListBinding;
    for (const ctx of binding?.getAllCurrentContexts?.() ?? []) {
      if (ctx.isTransient() && !ctx.getProperty("ItemCode") && !ctx.getProperty("ComplementedItemKey")) {
        void ctx.delete();
      }
    }
    this.refreshDocumentTotal();
  }
```

Em `clearHiddenAmountColumns` (`:240-246`), trocar

```ts
      } else if (toNumber(ctx.getProperty("ComplementAmount")) !== 0) {
        void ctx.setProperty("ComplementAmount", 0);
      }
```

por

```ts
      } else {
        if (toNumber(ctx.getProperty("ComplementAmount")) !== 0) {
          void ctx.setProperty("ComplementAmount", 0);
        }
        // Fora do complemento o vínculo com a nota original é recusado (C7): sai junto com o valor.
        if (ctx.getProperty("ComplementedItemKey")) {
          void ctx.setProperty("ComplementedItemKey", null);
        }
      }
```

Em `openContractValueHelp`, trocar o fim (`:408-411`)

```ts
    oInput.setValue(oSelected.getProperty("Code") as string);
    await oTarget.setProperty("PurchaseContractKey", oSelected.getProperty("Key"));
  }
```

por

```ts
    // A célula lê o mapa antes da navegação (draft.formatContractCode): grava o código ANTES da chave, senão a
    // reavaliação disparada pela chave apagaria o que o setValue escreveu.
    const ui = this.getModel("ui") as JSONModel;
    ui.setProperty("/contractCodes", withEntry(
      ui.getProperty("/contractCodes") as Record<string, string>, oSelected.getProperty("Key") as string,
      oSelected.getProperty("Code") as string));
    oInput.setValue(oSelected.getProperty("Code") as string);
    await oTarget.setProperty("PurchaseContractKey", oSelected.getProperty("Key"));
  }
```

E acrescentar, logo antes de `/** Quantidade, preço ou um dos quatro valores da linha mudou: o "Total geral" acompanha. */`:

```ts
  private complementDialog: TableSelectDialog;

  /** Complemento de valor de emissão própria em edição: "Incluir Item" escolhe o item da nota original (spec 2026-10-14). */
  protected isOwnComplementDraft(): boolean {
    return (this.getModel("ui") as JSONModel).getProperty("/ownComplement") === true;
  }

  /**
   * "Incluir Item" no complemento de valor próprio: a linha nasce do item escolhido, com produto, unidade e natureza
   * herdados e travados, o contrato herdado e travado quando o original tem (sem contrato, o operador escolhe: D5),
   * quantidade e preço 0 e o foco no "Valor do complemento".
   */
  protected async addOwnComplementLine(binding: ODataListBinding): Promise<void> {
    const invoice = this.getView().getBindingContext() as Context;
    const cardCode = invoice?.getProperty("CardCode") as string;

    if (!cardCode) {
      MessageBox.warning("Informe o fornecedor antes de incluir itens.");
      return;
    }

    const candidate = await this.pickComplementCandidate(cardCode, invoice.getProperty("BranchCode") as string);
    if (!candidate) {
      return;
    }

    // Antes do create: "Contrato" e "Nota original" leem estes mapas (a linha nova não tem as navegações). Original com
    // contrato: o contrato vem herdado e a célula trava (D5); sem contrato, a célula fica editável e o operador escolhe.
    const ui = this.getModel("ui") as JSONModel;
    ui.setProperty("/contractCodes", withEntry(
      ui.getProperty("/contractCodes") as Record<string, string>, candidate.PurchaseContractKey, candidate.PurchaseContractCode));
    ui.setProperty("/inheritedContracts", withEntry(
      ui.getProperty("/inheritedContracts") as Record<string, string>,
      candidate.PurchaseContractKey ? candidate.ItemKey : "", candidate.PurchaseContractKey));
    ui.setProperty("/complementedItemLabels", withEntry(
      ui.getProperty("/complementedItemLabels") as Record<string, string>, candidate.ItemKey,
      complementedItemLabel(candidate.InvoiceNumber, candidate.LineNumber)));

    const created = binding.create(complementLineFrom(candidate), false, false, false);
    this.refreshDocumentTotal();
    this.focusComplementAmount(created);
  }

  /**
   * Lista os itens elegíveis e resolve com o escolhido, ou undefined ao cancelar (o contrato do openTableSelectDialog,
   * que aqui não serve: a lista vem de uma function, num JSONModel).
   */
  private async pickComplementCandidate(
    cardCode: string, branchCode: string
  ): Promise<PurchaseInvoiceComplementCandidate | undefined> {
    const result = await sendJson("GET",
      complementCandidatesPath(ServerRoutes.purchaseInvoicesGetComplementCandidates, cardCode, branchCode));

    if (!result.ok) {
      MessageBox.error(result.message);
      return undefined;
    }

    // A view morre ao navegar (clearControlAggregation) e leva o diálogo junto: recria quando preciso.
    if (!this.complementDialog || this.complementDialog.isDestroyed()) {
      this.complementDialog = await Fragment.load({
        name: "siagrob1.view.purchaseInvoices.fragments.ComplementCandidatesDialog",
        controller: this,
        id: this.getView().getId(),
      }) as TableSelectDialog;
      this.complementDialog.setModel(new JSONModel({ rows: [] }), "candidates");
      this.getView().addDependent(this.complementDialog);
    }

    const dialog = this.complementDialog;
    (dialog.getModel("candidates") as JSONModel).setData({
      rows: odataCollection<PurchaseInvoiceComplementCandidate>(result.data),
    });
    (dialog.getBinding("items") as JSONListBinding)?.filter([]);

    return new Promise((resolve) => {
      // Ligados por abertura e desligados no primeiro disparo: o diálogo é reaproveitado.
      const finish = (row?: PurchaseInvoiceComplementCandidate) => {
        dialog.detachConfirm(onConfirm);
        dialog.detachCancel(onCancel);
        resolve(row);
      };
      const onConfirm = (ev: TableSelectDialog$ConfirmEvent) =>
        finish(ev.getParameter("selectedItem")?.getBindingContext("candidates")?.getObject() as PurchaseInvoiceComplementCandidate);
      const onCancel = () => finish(undefined);

      dialog.attachConfirm(onConfirm);
      dialog.attachCancel(onCancel);
      dialog.open("");
    });
  }

  /** Busca do diálogo, no cliente (a lista já veio inteira): nota, produto ou contrato. */
  onComplementCandidateSearch(ev: TableSelectDialog$SearchEvent): void {
    const value = (ev.getParameter("value") ?? "").trim();
    const binding = ev.getSource().getBinding("items") as JSONListBinding;

    binding.filter(value
      ? [new Filter({
          filters: ["InvoiceNumber", "ItemCode", "ItemName", "PurchaseContractCode"]
            .map((path) => new Filter(path, FilterOperator.Contains, value)),
          and: false,
        })]
      : []);
  }

  /** "Nota original" e contrato herdado das linhas já gravadas (Detail/Edit), lidos da mesma function do diálogo. */
  private async refreshComplementedItemLabels(cardCode: string, branchCode: string, isCurrent: () => boolean): Promise<void> {
    if (!cardCode || !branchCode) {
      return;
    }

    const result = await sendJson("GET",
      complementCandidatesPath(ServerRoutes.purchaseInvoicesGetComplementCandidates, cardCode, branchCode));

    if (!isCurrent() || !result.ok) {
      return;
    }

    const rows = odataCollection<PurchaseInvoiceComplementCandidate>(result.data);
    const ui = this.getModel("ui") as JSONModel;
    ui.setProperty("/complementedItemLabels", complementedItemLabels(rows));
    ui.setProperty("/inheritedContracts", inheritedContracts(rows));
  }

  /** Foco no "Valor do complemento" da linha criada. getCells() pula coluna invisível: a célula é achada pelo binding. */
  private focusComplementAmount(line: Context): void {
    const table = this.byId("tablePurchaseInvoiceItems") as Table;

    table?.attachEventOnce("rowsUpdated", () => {
      const row = table.getRows().find((r) => r.getBindingContext() === line);
      row?.getCells().find((cell) => cell.getBinding("value")?.getPath() === "ComplementAmount")?.focus();
    });
  }

```

- [ ] **Step 8: Incluir/Editar**

`webapp/controller/purchaseInvoices/Add.controller.ts:202-213` — trocar `onAddItem()` inteiro por:

```ts
  onAddItem() {
    const oTable = this.byId("tablePurchaseInvoiceItems") as Table;
    const oBinding = oTable?.getBinding("rows") as ODataListBinding;

    if (!oBinding) {
      return;
    }

    // Complemento de valor próprio (spec 2026-10-14): a linha nasce do item da nota original escolhido.
    if (this.isOwnComplementDraft()) {
      void this.addOwnComplementLine(oBinding);
      return;
    }

    oBinding.create(blankItemRow(), false, false, false);

    this.refreshDocumentTotal();
  }
```

`webapp/controller/purchaseInvoices/Edit.controller.ts:69-88` — trocar `onAddItem()` inteiro por:

```ts
  onAddItem() {
    const oBinding = (this.byId("tablePurchaseInvoiceItems") as Table)
      ?.getBinding("rows") as ODataListBinding;

    if (!oBinding) {
      return;
    }

    // Complemento de valor próprio (spec 2026-10-14): a linha nasce do item da nota original escolhido.
    if (this.isOwnComplementDraft()) {
      void this.addOwnComplementLine(oBinding);
      return;
    }

    // Toda propriedade editável entra no create() inicial, nem que seja como null: sem isso a
    // primeira alteração abre "Must not change a property before it has been read".
    // Decimais vão como NÚMERO — em string o POST volta 400 no vínculo do corpo.
    oBinding.create({
      ItemCode: "", ItemName: "", UnitOfMeasureCode: "",
      Quantity: 0, UnitPrice: 0, SalesInvoiceItemKey: null, PurchaseContractKey: null,
      UsageCode: null, UsageName: null,
      FreightValue: 0, InsuranceValue: 0, DiscountValue: 0, OtherExpensesValue: 0,
      ComplementedItemKey: null,
    }, false, false, false);

    this.refreshDocumentTotal();
  }
```

- [ ] **Step 9: Gates**

Dev server parado. Na raiz do frontend:

```bash
python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" webapp/view/purchaseInvoices/fragments/Items.fragment.xml webapp/view/purchaseInvoices/fragments/ComplementCandidatesDialog.fragment.xml
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Expected: parse sem saída; sem erro (ui5lint sem regra nova). QUnit unit pelo navegador (`yarn start`, URL das Global Constraints): módulos de `PurchaseInvoiceDraftHelpers` e `PurchaseInvoiceNfeHelpers` verdes; derrubar o dev server **por PID** (porta 8080).

- [ ] **Step 10: Stage**

```bash
git add webapp/types/PurchaseInvoiceComplementCandidate.ts webapp/helpers/PurchaseInvoiceDraftHelpers.ts webapp/test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts webapp/model/ServerRoutes.ts webapp/view/purchaseInvoices/fragments/ComplementCandidatesDialog.fragment.xml webapp/view/purchaseInvoices/fragments/Items.fragment.xml webapp/controller/purchaseInvoices/BaseController.ts webapp/controller/purchaseInvoices/Add.controller.ts webapp/controller/purchaseInvoices/Edit.controller.ts
```

O controlador commita com:

```
feat(invoice): incluir item do complemento próprio a partir da nota original

No complemento de valor de emissão própria, Incluir Item abre a lista dos itens das
entradas próprias autorizadas do fornecedor e da filial; a linha nasce com produto,
unidade e natureza herdados e travados, e o foco vai para o valor. O contrato vem
herdado e travado quando o original tem; sem contrato no original, o operador o
escolhe pelo value help de sempre. A grade mostra a Nota original (documento/item), e
a célula do contrato passa a ler o código escolhido na tela antes da navegação, que
não existe na linha nova.

Atenção: o diálogo é um TableSelectDialog próprio sobre JSONModel, porque a lista vem
de uma function; o openTableSelectDialog só filtra ODataListBinding.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

### Task 9: Verificação ponta a ponta (CEAGUI dev + homologação SEFAZ-SP)

**Files:** nenhum código novo — achado vira correção na task dona, com commit próprio (`fix(invoice): …`).

- [ ] **Step 1: Gates completos**

Backend (raiz): `dotnet build SiagroB1.sln`, `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` (anotar as contagens), `dotnet ef migrations has-pending-model-changes` dos dois contextos ("No changes…"). Frontend (raiz, dev server parado): parse dos três XML alterados, `yarn ts-typecheck && yarn lint && yarn ui5lint`. `git status --short` limpo nos dois repos depois dos commits do controlador; `git log --oneline main..` nos dois mostra só os commits deste plano (backend: o do spec + 6; frontend: 2).

- [ ] **Step 2: Aplicar a migration só no CEAGUI dev**

Ler antes as memórias `run-stack-locally` e `db-migration-profile-targets-production`. No PowerShell, na raiz do backend:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Ceagui-Development"
(Get-Content SiagroB1.Web\appsettings.Ceagui-Development.json -Raw | ConvertFrom-Json).ConnectionStrings.SiagroDB -split ';' | Where-Object { $_ -match '^(Server|Database)=' }
```

Expected: `Database=CEAGUI_SIAGRO_DEV` em servidor local. Qualquer outro banco: **parar**.

```powershell
dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: só `AddPurchaseInvoiceItemComplementedItem` como `(Pending)`. Outra pendente: **parar** e levar ao usuário. Então:

```powershell
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Conferir na saída o banco-alvo e a migration. Nunca produção, nunca o profile `db-migration`.

- [ ] **Step 3: Conferir os dados de referência**

```powershell
sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -W -Q "SET NOCOUNT ON; SELECT i.[Key], i.InvoiceNumber, i.InvoiceStatus, i.NfeStatus, i.ChaveNFe, it.ItemCode, it.UsageCode, it.PurchaseContractKey FROM PURCHASE_INVOICES i JOIN PURCHASE_INVOICES_ITEMS it ON it.PurchaseInvoiceKey = i.[Key] WHERE i.CardCode = 'FTESTE01' AND i.TaxDocumentSeries = '9' AND i.TaxDocumentNumber = '000000005'; SELECT Code, CardCode, ItemCode, Status FROM PURCHASE_CONTRACTS WHERE CardCode = 'FTESTE01'; SELECT Code, CreatesFinancialDocument FROM USAGES WHERE Code = 4"
```

Estado em 14/10: nº 5 = DE000001, Confirmada (1), NF-e Autorizada (2), item `1`, natureza 4, `PurchaseContractKey` NULL (original **sem contrato**, D5); contrato `CC000002` (FTESTE01, produto `1`, Status 1 = Aprovado); natureza 4 com `CreatesFinancialDocument = 1`. Qualquer divergência (nº 5 não autorizada, CC000002 fora de Aprovado/Encerrado, natureza sem "Gera financeiro"): **parar** e levar ao usuário — não corrigir dado por SQL. Anotar a chave da nº 5 (vai no `NFref`). A nº 5 tem a devolução DE000002 (nº 6, Rejeitada, Pendente) ainda ativa: é por isso que C8 roda antes da recusa de devolução (Task 6).

- [ ] **Step 4: Subir a stack**

Profile `ceagui`: Gateway primeiro (`dotnet run --project SiagroB1.Gateway --launch-profile ceagui`, esperar "Loading proxy data from config"), depois Web (`dotnet run --project SiagroB1.Web --launch-profile ceagui`); frontend `yarn start:dev`. Login `admin`/`1234`. Navegar pelo **menu** (não por `goto` de hash).

- [ ] **Step 5: Roteiro (spec, "Verificação no navegador")**

1. Documento de Entrada → Incluir → Emissão Própria: o Select **Tipo** fica habilitado e oferece Normal e Complemento de valor; Devolução não é escolhível. Escolher Complemento de valor: a linha em branco do rascunho some.
2. Sem fornecedor: "Incluir Item" → aviso "Informe o fornecedor antes de incluir itens.".
3. Fornecedor FTESTE01, data de hoje, condição 11 → "Incluir Item" → o diálogo "Item da nota original" lista o item `1` da DE000001 (nº 5) com a coluna Contrato **vazia**. Busca por "DE000001" filtra. Cancelar não cria linha.
4. Escolher o item da nº 5 → a linha nasce com Produto `1`, UM e Natureza preenchidos e **travados** (sem value help), "Nota original" = `DE000001/1`, Quantidade/Preço ocultos, foco em "Valor do complemento"; a célula **Contrato** vazia e **editável**. Salvar sem contrato → recusa existente "Item 1: o complemento de valor exige o contrato de compra na linha." **na tela**.
5. Contrato pelo value help → `CC000002` → a célula mostra CC000002. Valor 15,00 → Salvar → Detail: tipo Complemento de valor, "Nota original" na grade, botão **Emitir NF-e** visível e **sem "Devolver"**. Editar: Produto/UM/Natureza travados; Contrato continua editável (original sem contrato); cancelar a edição.
6. Editar → "Incluir Item" escolhendo de novo o item da nº 5 (com contrato CC000002) → Salvar → recusa C4 **na tela** ("O item DE000001/1 já está complementado nesta nota."). Cancelar a edição.
7. **Emitir NF-e** em **homologação SEFAZ-SP** → autorizada; documento Confirmado (emitir é confirmar). Conferir o XML:

   ```powershell
   sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -W -Q "SET NOCOUNT ON; SELECT x.Kind, CHARINDEX('<finNFe>2</finNFe>', x.Xml) AS FinNFe2, CHARINDEX('<refNFe><chave da nº 5></refNFe>', x.Xml) AS NFref, CHARINDEX('<DFeReferenciado>', x.Xml) AS ItemRef, CHARINDEX('<vol>', x.Xml) AS Vol FROM PURCHASE_INVOICE_NFE_XMLS x JOIN PURCHASE_INVOICES i ON i.[Key] = x.PurchaseInvoiceKey WHERE i.InvoiceNumber = '<DE do complemento>' AND x.Kind = 2"
   ```

   Expected: `FinNFe2` > 0, `NFref` > 0, `ItemRef` = 0, `Vol` = 0 (sem pesos). Firme criado: `SELECT Code, NetAmount, Status FROM FINANCIAL_DOCUMENTS WHERE OriginType = 3 AND OriginKey IN (SELECT [Key] FROM PURCHASE_INVOICES_ITEMS WHERE PurchaseInvoiceKey = '<Key do complemento>')` → uma linha, no contrato CC000002. **Se a SEFAZ rejeitar**, registrar código e motivo (ex.: 629 = qCom × vUnCom ≠ vProd) e parar: o ajuste é no montador e vai ao usuário antes (spec regra 11).
8. Na nº 5: **Cancelar NF-e** (justificativa ≥ 15 caracteres) → recusa C8 **na tela** ("A nota DE000001 tem o complemento DE… ativo: cancele o complemento antes."), nenhum evento enviado. O botão **"Devolver"** continua disponível na nº 5 (não executar).
9. Derrubar a stack **por PID** nas portas 50000, 5246 e 8080 (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`) e conferir as portas de novo no fim (o file watcher ressuscita processo enquanto houver edição).

- [ ] **Step 6: Fechamento**

Anotar contagens de testes, números/chaves usados em homologação, achados e correções. Atualizar a memória do projeto (entrada nova para o sub-projeto 2b: migration `AddPurchaseInvoiceItemComplementedItem` aplicada só no CEAGUI dev; complemento da nº 5 com o contrato CC000002 escolhido (D5); resultado da SEFAZ-SP para finNFe=2 com qCom 0; atualizar o "próximo número livre" da série 9 em `nfe-purchase-invoice-issuance-feature`). Merge em `main` é decisão do usuário; **nunca push**.

---

## Cobertura do spec

| Item do spec | Onde |
|---|---|
| D1 (só nota nossa: entrada própria autorizada, normal ou contra-nota) | Task 2 (`IsComplementableOrigin`, C2) e Task 3 (`Counter_note_is_offered`) |
| D2 (uma nota; cada linha um item de original; originais diferentes do mesmo produtor) | Tasks 2 e 5 (`…referencing_the_originals`, duas originais) |
| D3 (linha herda produto, unidade, natureza e contrato; operador só digita o valor) | Task 2 (herança no backend) e Task 8 (linha nascida do item, campos travados, foco) |
| D5 (original sem contrato: operador escolhe o contrato, com as regras de contrato e obrigatório; original com contrato: herdado e travado) | Task 2 (guarda de contrato dentro da regra, depois de C3; testes do contrato escolhido), Task 3 (candidato sem contrato) e Task 8 (célula editável só sem contrato herdado) |
| D4 (cancelar original recusado com complemento ativo; devolução da original permitida; complemento sem Devolver) | Task 6 (C8 nos dois cancelamentos, C9, `Original_is_still_returned_alone`) e Task 7 (`canReturnPurchase`) |
| Modelo: `ComplementedItemKey` FK sem cascata, índice, migration sem backfill, navegação só para leitura | Task 1 (Restrict = mesmo DDL de NoAction; ver Global Constraints) |
| `ComplementedItemKey` só no complemento próprio e aí obrigatório | Task 2 (C1, C7) |
| Regra 1 (C1) · Regra 2 (C2) · Regra 3 (C3, inclusive no Update — também com contrato escolhido) · Regra 4 (C4) · Regra 5 (produto, unidade e natureza: copia no Create/ItemsCreate, recusa no Update/ItemsUpdate — C5) | Task 2 |
| Regra 5a (contrato herdado: copia e recusa divergência — C10; original sem contrato: o do operador pelas regras da linha, obrigatório) | Task 2 |
| Recusa "Complemento de valor é só escrituração por enquanto" removida | Task 2 (Step 4; teste antigo ajustado) |
| Regra 6 (`EnsureIssuableType` aceita) | Task 5 |
| Regra 7 (`IsActiveForAsync` calcula) | Task 5 (`Own_complement_is_taxed_like_the_own_entry`) |
| Regra 8 (readiness: produto, unidade, condição; sem pesos; recarrega originais — C6) | Task 5 (C6 direto; `RequiresWeights` falso no complemento) |
| Regra 9 (`NfePurpose.Complement` → `fnComplementar`; assembler escolhe pelo tipo) | Tasks 4 e 5 |
| Regra 10 (`ReferencedKeys` distintas em ordem de primeira aparição; itens sem `DFeReferenciado`) | Tasks 4 e 5 |
| Regra 11 (qCom = qTrib = 0, vUnCom = vUnTrib = 0, vProd = valor, uCom/uTrib herdadas; risco 629) | Tasks 4 e 5 (testes); Task 9 registra a resposta da SEFAZ-SP |
| Regra 12 (natOp da natureza da linha) | sem mudança de código — conferido em Task 5 (`natOp`) |
| Regra 13 (C8 no `PurchaseInvoicesCancelService` e no `PurchaseInvoicesNfeCancelService`; complemento cancelado não trava) | Task 6 |
| Regra 14 (saldo devolvível só da original; C9; tela esconde Devolver) | Task 6 e Task 7 |
| Mensagens C1–C10 (texto exato; `{número}` = número do documento) | Global Constraints; C1–C5, C7, C10: Task 2 · C6: Task 5 · C8, C9: Task 6 |
| Function `PurchaseInvoicesGetComplementCandidates(CardCode, BranchCode)` com o DTO PascalCase, contrato anulável | Task 3 (+ `UsageName`, mais recente primeiro — decisões do plano) |
| EDM: function nova e `ComplementedItemKey` no item | Tasks 1 e 3 (testes de EDM) |
| Create/Update/ItemsCreate/ItemsUpdate aceitam `ComplementedItemKey` | Task 2 (Update e ItemsUpdate copiam; Create e ItemsCreate gravam o corpo) |
| Recusas como `BadRequest(string)` | sem mudança — os controllers existentes convertem `DefaultException` em 400 |
| Título firme, Funrural e tributos sem mudança; firme na confirmação | Task 5 (`…referencing_the_originals` confere os firmes) |
| Fluxo igual ao da entrada própria (confirmar = emitir, cancelar, CC-e, inutilizar) | sem mudança — os serviços NF-e da entrada aceitam o documento a partir da Task 5 |
| Complementos sucessivos permitidos; mesmo item duas vezes na mesma nota não | Task 2 |
| Frontend — Tipo no modo NF-e (Normal e Complemento de valor; Devolução só pelo Devolver; `mustFallBackToNormal` mantém) | Task 7 |
| Frontend — Incluir item pelo diálogo, produto/unidade/natureza travados, contrato travado se herdado e editável (value help) se o original não tem, quantidade/preço 0, foco no valor, aviso sem produtor | Task 8 |
| Frontend — coluna "Nota original" (número/item) | Task 8 |
| Frontend — `canIssuePurchaseNfe` e `isPurchaseTaxMode` alinhados | Task 7 |
| Frontend — Detail do complemento sem "Devolver"; original sem mudança visual (C8 na recusa) | Task 7 (teste) e Task 9 (passo 7) |
| Frontend — armadilhas (`--` em XML, `targetType: 'any'`, `Number()`, parse dos XML) | Global Constraints; Tasks 7–8 |
| Testes listados no spec (linha com C10 e original sem contrato, emissão, travas, confirmação, EDM, Fiscal) | Tasks 1–6 |
| Verificação no navegador (CEAGUI dev) e homologação: nº 5 sem contrato + CC000002 escolhido, stack derrubada por PID | Task 9 |
| Fora (complemento de imposto, ajuste, refNFe de terceiro/refNFP, venda, SAPB1) | não implementado — `IsComplementableOrigin` só aceita nota própria; `NfePurpose` não ganha ajuste |
