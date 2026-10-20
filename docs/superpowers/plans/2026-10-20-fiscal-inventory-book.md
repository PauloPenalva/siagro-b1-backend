# Inventário em Data + Livro de Registro de Inventário — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tela "Inventário em Data" (menu Fiscal, só STANDALONE) com grade/Excel e impressão do Livro de Registro de Inventário modelo 7 (termos + folhas numeradas), valores iguais ao saldo final do Kardex.

**Architecture:** `FiscalInventoryService` em `SiagroB1.Reports` lê `FiscalKardexService.OpenLayersAsync` + cadastro de produto (NCM) + filial, monta linhas; `POST /reports/FiscalInventory` (JSON) e `POST /reports/FiscalInventory/Book` (PDF via `FiscalInventoryBook.frx`). Tela UI5 no padrão do Kardex.

**Tech Stack:** .NET 10, EF InMemory, xUnit, FastReport.OpenSource; OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-20-fiscal-inventory-book-design.md` (os 8 "Detalhes resolvidos" são vinculantes).

## Global Constraints

- Identificadores em inglês; textos ao usuário em pt-BR. Só STANDALONE: recusa `"Disponível somente no modo STANDALONE."`.
- Valor autoritativo = Σ camadas; quantidade = Σ camadas − Deficit; custo unitário = valor ÷ quantidade (6 casas, vazio se quantidade ≤ 0); valores 2 casas; quantidades 3 casas.
- Branch `feature/fiscal-inventory` (be e fe, fe a partir de `feature/fiscal-kardex`); migration exige `DB:`; nunca push.

## Review Focus

1. Inventário na data D = saldo final do Kardex com "até" = D, inclusive com movimento no próprio dia D e saldo negativo. Teste na Task 1.
2. Livro com várias páginas: "a transportar" da página n = "transporte" da página n+1, e o total geral = soma das linhas. Teste na Task 2 (renderizar e extrair texto do PDF, ou validar os dados pré-calculados por página).
3. Filial sem IE/CNPJ/razão social → impressão recusada com a mensagem do que falta. Teste na Task 2.
4. Folha inicial 37 → termo de abertura "do nº 37 ao nº 37+N−1" e cabeçalhos com folhas 38… (a abertura é a folha 37). Teste na Task 2.
5. Inventário vazio (nada em estoque) → grade vazia e livro ainda imprime termos com "nada a inventariar"? → decisão: recusa com "Não há saldo a inventariar nesta data.". Teste na Task 2.

---

### Task 1: Serviço + JSON + menu

**Files:** Create `SiagroB1.Reports/Dtos/FiscalInventoryDtos.cs`, `SiagroB1.Reports/Services/FiscalInventoryService.cs`, `SiagroB1.Reports/Controllers/FiscalInventoryController.cs` (só a ação JSON nesta tarefa), migration CommonContext `AddFiscalInventoryMenu` (key `fiscalInventory`, "Inventário em Data", `sap-icon://product`, Order 8, ParentKey `fiscal`, StandaloneOnly, ROLE_MENUS ADMIN). Tests `SiagroB1.Application.Tests/Reports/FiscalInventoryServiceTests.cs`, `FiscalInventoryControllerTests.cs`.

**Interfaces (produces):**
- `FiscalInventoryRequest { string BranchCode; DateTime Date; string? ItemCode; }`
- `FiscalInventoryData { DateTime Date; string BranchCode; List<FiscalInventoryRowDto> Rows; decimal TotalValue; List<string> PendingComplementItems /* "código - descrição" */; }`
- `FiscalInventoryRowDto { string Ncm; string ItemCode; string ItemName; string? UnitOfMeasureCode; decimal Quantity; decimal? UnitCost; decimal Value; }` — ordem NCM (vazios no fim), depois ItemCode; exclui quantidade 0 e valor 0.
- `FiscalInventoryService(IUnitOfWork db, FiscalKardexService kardex)`: `Task<FiscalInventoryData> BuildAsync(FiscalInventoryRequest)`.
- Controller `[Route("/reports/FiscalInventory")]`, `[HttpPost] Get(...)` → Ok; validação `"Informe a filial e a data do inventário."`.
- [ ] Testes: `Build_MatchesKardexClosingAtDate` (inclui movimento no próprio dia), `Build_NegativeBalanceShowsNegativeQuantityAndNoUnitCost`, `Build_ExcludesZeroItems`, `Build_OrdersByNcmThenItemWithBlankNcmLast`, `Build_ListsPendingComplementItems`, `Build_ItemFilterRestricts`; controller SAPB1/validação/ok. RED → implementar → GREEN + suíte → commit `feat(reports): inventário em data a custo FIFO` com `DB: AddFiscalInventoryMenu`.

### Task 2: Livro (PDF)

**Files:** Create `SiagroB1.Reports/Reports/Templates/FiscalInventoryBook.frx`; Modify `FiscalInventoryService.cs` (`Task<byte[]> BookAsync(FiscalInventoryBookRequest)`), `FiscalInventoryController.cs` (`[HttpPost("Book")]` → `File(pdf,"application/pdf")`), DTOs. Tests `FiscalInventoryBookTests.cs` (modelo: `InvoiceReportsPdfTests.cs`, `ReportTemplateRenderSmokeTests.cs`).

**Interfaces:**
- `FiscalInventoryBookRequest { string BranchCode; DateTime Date; int BookNumber; int FirstSheet = 1; }` — ItemCode não existe aqui (livro é da filial inteira).
- Validações (BadRequest pt-BR, nesta ordem): `BookNumber < 1` ou `FirstSheet < 1` → `"Informe o número do livro e a folha inicial."`; faltando razão social/CNPJ/IE da filial → `"Complete o cadastro da filial antes de imprimir: <lista>."` (ex.: "Inscrição Estadual, CNPJ"); sem linhas → `"Não há saldo a inventariar nesta data."`. (Complemento pendente NÃO bloqueia no servidor — a confirmação é da tela.)
- Layout: página 1 = termo de abertura (folha = FirstSheet); páginas do inventário com cabeçalho (razão social, CNPJ, IE, "Inventário existente em dd/MM/yyyy", "Livro nº", "Folha nº" = FirstSheet + página − 1); colunas Classificação fiscal | Discriminação | Quantidade | Unidade | Valor unitário | Parcial | Total; "Total" preenchido só na última linha de cada NCM com o subtotal do NCM; rodapé "A transportar" e cabeçalho "Transporte" com a soma acumulada; última página do inventário com "Total geral"; última página = termo de encerramento. Termos: "Contém este livro N folhas numeradas tipograficamente, do nº X ao nº Y, e servirá/serviu para o lançamento do Registro de Inventário de <razão social>, …" + endereço, CNPJ, IE, cidade/data, duas linhas de assinatura em branco ("Responsável" e "Contabilista"). N/Y por DoublePass.
- Dados para o frx: linhas com campo `NcmGroupTotal` (decimal? só na última do NCM) pré-calculado no serviço; carry-forward por total de página do FastReport (ou script) — escolha do implementador, mas testado.
- [ ] Testes: `Book_RefusesWithoutBranchData` (mensagem lista o que falta), `Book_RefusesInvalidNumbers`, `Book_RefusesEmptyInventory`, `Book_RendersPdf` (bytes começam com "%PDF"), `Book_SheetNumbersStartAtFirstSheet` e `Book_CarryForwardMatchesNextPage` e `Book_GrandTotalEqualsSumOfRows` (extraia o texto do PDF — veja se algum teste do repo já extrai; senão use os dados preparados + a página/variáveis do report em modo de preparação `Report.Prepare()` e inspecione `PreparedPages`), `Book_NcmSubtotalOnLastLineOfGroup`. Copiar fontes/`wwwroot` como os outros testes de PDF exigem. Commit `feat(reports): livro de registro de inventário modelo 7`.

### Task 3: Tela (frontend, worktree `wt-fiscal-inventory-fe`)

**Files:** `webapp/view|controller/fiscalInventory/Main.*` (modelo `fiscalKardex`: filtros no subHeader — Filial obrigatória com padrão = filial de trabalho, Data obrigatória com padrão 31/12 do ano anterior em data local, Produto opcional; grade com NCM, Produto, Descrição, UM, Quantidade (3), Custo unitário (6), Valor (2), total geral no rodapé; linhas de PendingComplementItems num MessageStrip de aviso; Exportar Excel; botão "Imprimir livro" desabilitado com produto preenchido → diálogo com Nº do livro e Folha inicial (padrão 1) → se houver PendingComplementItems, MessageBox.confirm listando-os → POST `/reports/FiscalInventory/Book` → abre PDF em nova aba (helper `openPdfReport`), erro 400 mostra o texto). `ServerRoutes.ts` (`fiscalInventory`, `fiscalInventoryBook`), `manifest.json` rota `fiscal-inventory` / name `fiscalInventory`.
- [ ] `yarn ts-typecheck` e `yarn lint` limpos; commit `feat(reports): tela do inventário em data e impressão do livro`. Verificação no navegador fica com o controlador.
