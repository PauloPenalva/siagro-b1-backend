# GAC-1233 — Saídas de transbordo na aba Entregas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A aba Entregas do contrato de compra lista, com quantidade zero, os romaneios que consomem liberações de transbordo do contrato.

**Architecture:** `PurchaseContractsGetAllocationsByContractService` ganha uma segunda consulta Dapper só de exibição (`TransshipmentExitsSql`) cujas linhas são unidas às do razão antes da ordenação. O frontend só registra a origem nova em `ALLOCATION_ORIGINS`.

**Tech Stack:** .NET 10, Dapper, xUnit; OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-11-24-gac-1233-transshipment-exit-rows-design.md`

## Global Constraints

- Hotfix sobre a 1.1.319: backend em `C:\Projetos\SiagroB1\worktrees\gac-1233\backend` (base `116dfa35`), frontend em `C:\Projetos\SiagroB1\worktrees\gac-1233\frontend` (base `7eb7122`), branch `hotfix/gac-1233-transshipment-exit-rows` nos dois. Nunca `push`.
- Sem migration; a consulta principal (`SQL`) não muda.
- Todo `FROM`/`JOIN` da consulta nova leva `WITH (NOLOCK)`.
- `Volume` das linhas novas é sempre `0`.
- Identificadores em inglês; textos de tela em pt-BR.
- Commits: `feat(purchase-contract): …`, `Refs: GAC-1233`, mesmo assunto nos dois repos.

## Review Focus

- Romaneio de transbordo cancelado (`TransactionStatus = 2`) não pode aparecer.
- Romaneio que já tem alocação no contrato não pode aparecer duas vezes.
- Liberação de transbordo de **outro** contrato não pode vazar para este.
- Gerador sem código: o Motivo não pode sair com espaço duplo nem "null".
- Colunas fiscais da linha nova: nada de "diferença" igual ao peso do romaneio.

---

### Task 1: Backend — consulta e linhas de saída de transbordo

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs`
- Modify: `SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs` (só o comentário de `Origin`)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAllocationsByContractSqlTests.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAllocationsByContractFiscalColumnsTests.cs`
- Test (novo): `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTransshipmentExitReasonTests.cs`

**Interfaces:**
- Produces: campo `private static readonly string TransshipmentExitsSql`; `public static string TransshipmentExitReason(int transactionType, string? generatorCode, decimal netWeight, string? unitOfMeasureCode)`; origem `"TransshipmentExit"` no JSON.

- [ ] **Step 1: testes que falham**

Em `…SqlTests.cs`: acrescentar `[InlineData("TransshipmentExitsSql")]` ao teorema do NOLOCK e:

```csharp
[Fact]
public void Transshipment_exits_query_lists_only_informative_rows_of_the_contract()
{
    var sql = Regex.Replace(StripComments(ReadSql("TransshipmentExitsSql")), @"\s+", " ");

    Assert.Contains("SR.Origin = 3", sql);
    Assert.Contains("SR.PurchaseContractKey = @purchaseContractKey", sql);
    Assert.Contains("ST.TransactionType IN (7, 12, 13)", sql);
    Assert.Contains("ST.TransactionStatus <> 2", sql);
    Assert.Contains("CAST(0 AS decimal(18,3)) AS Volume", sql);
    Assert.Contains("'TransshipmentExit' AS Origin", sql);
    Assert.Matches(@"NOT EXISTS \( SELECT 1 FROM PURCHASE_CONTRACTS_ALLOCATIONS PA WITH \(NOLOCK\) WHERE PA\.StorageTransactionKey = ST\.\[Key\] AND PA\.PurchaseContractKey = SR\.PurchaseContractKey \)", sql);
}
```

Em `…FiscalColumnsTests.cs`: `BuildFiscalColumns("TransshipmentExit", 440m, 0m, false, false, null, false)` devolve `new FiscalColumns(0m, 0m, 0m, null, null, false)`.

Novo `…TransshipmentExitReasonTests.cs` (teoria sobre `TransshipmentExitReason`):

| type | generator | weight | uom | esperado |
|---|---|---|---|---|
| 7 | "00017647" | 440m | "KG" | `Saída do transbordo 00017647 — 440 KG` |
| 7 | "00017647" | 46820.5m | "KG" | `Saída do transbordo 00017647 — 46.820,5 KG` |
| 12 | "00017647" | 440m | "KG" | `Devolução ao transbordo 00017647 — 440 KG` |
| 13 | "00017647" | 440m | "KG" | `Perda no transbordo 00017647 — 440 KG` |
| 7 | null | 440m | null | `Saída do transbordo — 440` |
| 7 | "  " | 440m | "KG" | `Saída do transbordo — 440 KG` |

- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsGetAllocationsByContract|FullyQualifiedName~PurchaseContractsTransshipmentExitReason"` → falha de compilação/campo ausente.

- [ ] **Step 3: implementar**
  - `TransshipmentExitsSql`: `FROM STORAGE_TRANSACTIONS ST` `JOIN SHIPMENT_RELEASES SR ON SR.[Key] = ST.ShipmentReleaseKey`, `LEFT JOIN` em `PURCHASE_CONTRACTS PC`, `BRANCHS B`, `SHIPMENT_LOADS SLOAD` (`ST.ShipmentLoadKey`), `SALES_INVOICES INV` (`ST.SalesInvoiceKey`), `STORAGE_TRANSACTIONS STGEN` (`SR.GeneratedByStorageTransactionKey`). Devolve as mesmas colunas que o laço de `ExecuteAsync` lê (`BranchCode … StorageTransactionKey, ItemCode`), mais `TransactionType`, `GeneratorCode` e `NetWeight`; `StorageTransactionTypeName` por `CASE` (7 `N'Saída transbordo'`, 12 `N'Dev. transbordo'`, 13 `N'Perda transbordo'`); `Volume` e `Origin` como nos asserts; `CardCode` como `SupplierCode`.
  - `TransshipmentExitReason`: formato `#,##0.###` com `CultureInfo("pt-BR")`; prefixo por tipo; partes vazias omitidas.
  - `BuildFiscalColumns`: `"TransshipmentExit"` entra no `is "LoadRefusal" or "ReturnRedelivery"`.
  - `ExecuteAsync`: depois do laço do razão, percorrer a consulta nova e adicionar um DTO por linha (nomes de fornecedor/armazém por `GetSupplierName`, `ReasonDescription` por `TransshipmentExitReason`, colunas fiscais por `BuildFiscalColumns`). A ordenação final existente cobre as duas listas.
  - ⚠️ O laço atual lê `allocation.CardCode`, coluna que a consulta principal não devolve (ela devolve `SupplierCode`). Não corrigir no hotfix; na linha nova usar `SupplierCode`.

- [ ] **Step 4:** mesmo comando do Step 2 → todos passam; depois `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContracts"`.

- [ ] **Step 5: conferir a consulta contra a produção (só leitura)** — rodar `TransshipmentExitsSql` com `sqlcmd -K ReadOnly` para o contrato `8E6E10C4-6E4F-49A1-B743-08DEDB8931CD`: devem vir 10 linhas (00017475, 00017493, 00017523, 00017526, 00017648, 00017669, 00018767, 00018768, 00018770, 00018771), todas com `Volume = 0`, e a 00017669 com carga CG002223.

- [ ] **Step 6: commit** (`git add` dos arquivos novos; `feat(purchase-contract): liste as saídas de transbordo nas entregas do contrato`).

### Task 2: Frontend — origem `TransshipmentExit` e versão

**Files:**
- Modify: `webapp/helpers/AllocationOriginHelpers.ts`
- Modify: `package.json` (`"version": "1.1.319-hotfix.1"`)
- Test: `webapp/test/unit/helpers/AllocationOriginHelpers.qunit.ts`

**Interfaces:**
- Consumes: `Origin = "TransshipmentExit"` da Task 1.

- [ ] **Step 1: testes que falham** — a lista de chaves da legenda termina em `"TransshipmentExit"`; novo teste:

```ts
QUnit.test("saída de transbordo é informativa e não baixa o contrato (GAC-1233)", function (assert) {
	const origin = ALLOCATION_ORIGINS.find((o) => o.key === "TransshipmentExit");
	assert.strictEqual(origin?.legend, "Saída de transbordo");
	assert.strictEqual(allocationOriginText("TransshipmentExit"), "Transbordo");
	assert.strictEqual(allocationOriginHighlight("TransshipmentExit"), "None");
	assert.strictEqual(allocationOriginState("TransshipmentExit"), "None");
	assert.strictEqual(allocationOriginIcon("TransshipmentExit"), "sap-icon://shipping-status");
	assert.strictEqual(
		allocationOriginTooltip("TransshipmentExit", null, null, null),
		"Saída pela liberação de transbordo: o contrato já foi baixado pelo romaneio de compra.");
});
```

- [ ] **Step 2:** rodar os testes unitários → o teste novo e o da legenda falham.
- [ ] **Step 3:** acrescentar a entrada em `ALLOCATION_ORIGINS` e o ramo do tooltip; subir a versão.
- [ ] **Step 4:** `yarn ts-typecheck`, `yarn lint` e os testes unitários → passam.
- [ ] **Step 5: commit** (`feat(purchase-contract): liste as saídas de transbordo nas entregas do contrato`).

### Task 3: Verificação de ponta a ponta

- [ ] Subir Web + Gateway do worktree contra uma **cópia** do banco (nunca a produção) e o frontend do worktree; abrir um contrato com transbordo e conferir, na aba Entregas, as linhas novas, a legenda, o Motivo com o peso e a Quantidade zero.
- [ ] Conferir que a soma da coluna Quantidade continua igual ao volume alocado do contrato.
