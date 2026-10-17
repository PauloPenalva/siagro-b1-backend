# CT-e de frete tomado no Documento de Entrada — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Escriturar o CT-e (modelo 57) de frete tomado no Documento de Entrada. O documento usa o caminho de "outra compra" para o título a pagar, calcula os tributos pela natureza usando as UFs da prestação e guarda o vínculo informativo com as NF-e transportadas.

**Architecture:** O CT-e é um `PurchaseInvoice` de terceiro, Normal, com `TaxDocumentKind.Cte`.
- **Leitura do XML:** um leitor próprio (`SupplierCteXmlReader`), escolhido pela raiz do arquivo na importação já existente.
- **Tributos:** o motor de tributos recebe as UFs da prestação no lugar da UF do fornecedor.
- **Título:** sai sem código novo de financeiro, porque a linha usa natureza de "outra compra".
- **Vínculo com as NF-e:** fica na tabela filha `PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS`. Ela é preenchida a partir do XML guardado na gravação e mantida por actions.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, xUnit + EF InMemory; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-17-cte-purchase-entry-design.md`. Leia antes de qualquer tarefa.

**Repos/worktrees:**
- backend: `C:\Projetos\SiagroB1\siagro-b1-backend-cte`;
- frontend: `C:\Projetos\SiagroB1\siagro-b1-frontend-cte`;
- os dois na branch `feature/cte-purchase-entry`.

Commits por tarefa, no repo da tarefa. Formato `tipo(invoice): …` em pt-BR, com o trailer de co-autoria da sessão; migration leva o trailer `DB:`. **Nunca push.** `git add` em todo arquivo novo assim que for criado.

## Global Constraints
- Só STANDALONE. Tributos só com `TaxCalculationGate` ativo (STANDALONE + `Branch.IssuesNfe`).
- Identificadores em inglês; mensagens e rótulos em pt-BR.
- O CT-e é sempre `IssuerType.ThirdParty` e `InvoiceType.Normal`. Só `tpCTe = 0`.
- O ICMS é **recalculado pela natureza**. O ICMS do XML não vai para a linha (decisão do usuário, 17/10).
- O CT-e fica fora do DIFAL.
- Não consultar a SEFAZ para o CT-e.
- O vínculo com NF-e transportadas é informativo: não muda saldo, título nem tributo.
- A NF-e modelo 55, a contra-nota do produtor e os títulos de grão não podem mudar de comportamento.
- `SalesInvoice.TaxDocumentKind` usa o mesmo enum: o documento de saída **recusa** `Cte`.
- Primeira execução de testes num worktree novo: copiar `SiagroB1.Reports/wwwroot` do repo principal (memória "worktree-missing-untracked-report-assets").

## Review Focus
1. **Gravar um CT-e com XML guardado** não pode passar pelo `PurchaseInvoiceSupplierItemNumbers`, que lê o XML como NF-e e quebraria. Teste na Task 3.
2. **Excluir uma NF-e de saída ou de entrada referenciada por um CT-e** não pode dar erro 547 (FK Restrict). O delete limpa o vínculo antes. Testes na Task 7.
3. **Frete dentro de outra UF:** início = fim ≠ UF da filial (ex.: PR→PR, filial SP). Deve sair CFOP 2.x com a regra de ICMS **interna** da natureza. Teste na Task 4.
4. **Chave do CT-e colada com espaços ou de outro emitente:** normaliza ou recusa, como na NF-e. Testes na Task 2.
5. **Importar o mesmo CT-e duas vezes:** a segunda gravação é recusada pela unicidade da chave e não gera título duplicado (anti-objetivo da spec). Teste na Task 8.

---

### Task 1: Modelo de dados e migration

**Files:**
- Modify: `SiagroB1.Domain/Enums/TaxDocumentKind.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoice.cs`
- Modify: `SiagroB1.Domain/Entities/Branch.cs`
- Create: `SiagroB1.Domain/Entities/PurchaseInvoiceTransportedDocument.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`
- Create: migration `SiagroB1.Migrations/AppContext/<timestamp>_AddCtePurchaseEntry.cs`, gerada com `dotnet ef migrations add AddCtePurchaseEntry --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceCteModelTests.cs`

**Interfaces:**
- Produces:
  - `TaxDocumentKind.Cte = 2`, com o comentário "CT-e, modelo 57: frete tomado; chave obrigatória e conferida".
  - Em `PurchaseInvoice`:

    | Propriedade | Tipo | Coluna |
    |---|---|---|
    | `FreightStartState` | `string?` | VARCHAR(2) |
    | `FreightEndState` | `string?` | VARCHAR(2) |
    | `FreightStartCity` | `string?` | VARCHAR(60) |
    | `FreightEndCity` | `string?` | VARCHAR(60) |
    | `FreightPayerTaxId` | `string?` | VARCHAR(14), só dígitos |
    | `CteIcmsCst` | `string?` | VARCHAR(3), CST do ICMS lido do XML, informativo |
    | `CteIcmsValue` | `decimal?` | DECIMAL(19,2), informativo |
    | `TransportedDocuments` | `ICollection<PurchaseInvoiceTransportedDocument>` | navegação |

  - Em `Branch`: `FreightServiceItemCode` (`string?`, VARCHAR(50)), o produto padrão do CT-e importado (*decisão do plano*: na filial, porque a importação ainda não conhece a natureza).
  - `PurchaseInvoiceTransportedDocument`, tabela `PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS`:
    - `Key` (Guid, PK);
    - `PurchaseInvoiceKey` (Guid, FK do CT-e, **Cascade**);
    - `AccessKey` (VARCHAR(44) NOT NULL);
    - `SalesInvoiceKey` (`Guid?`, FK **Restrict**) e `SalesInvoice` (navegação);
    - `TransportedPurchaseInvoiceKey` (`Guid?`, FK **Restrict** para `PurchaseInvoice`) e `TransportedPurchaseInvoice` (navegação);
    - `PurchaseInvoice` (navegação);
    - índice único (`PurchaseInvoiceKey`, `AccessKey`).
  - `DbSet<PurchaseInvoiceTransportedDocument> PurchaseInvoiceTransportedDocuments` no `AppDbContext`.

- [ ] **Step 1: Write the failing test** `Transported_documents_roundtrip_with_cte_header`. Grava um `PurchaseInvoice` com `TaxDocumentKind.Cte`, UFs "SP"/"MT" e uma `PurchaseInvoiceTransportedDocument` com `AccessKey` de 44 dígitos. Relê com `Include(TransportedDocuments)` e confere os 3 valores.
- [ ] **Step 2: Run** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceCteModelTests"`. Expected: falha de compilação.
- [ ] **Step 3: Implement** enum, propriedades, entidade, configuração das FKs (padrão das auto-relações Restrict em `AppDbContext.cs:245-260`) e DbSet.
- [ ] **Step 4: Run** o teste. Expected: PASS.
- [ ] **Step 5: Gere a migration e confira o DDL.**
  - O DDL só pode **adicionar** colunas e tabela. Sem `DropColumn`.
  - As FKs saem `ON DELETE NO ACTION` para SalesInvoice e TransportedPurchaseInvoice, e `CASCADE` para o pai.
  - `dotnet build SiagroB1.sln` deve passar.
- [ ] **Step 6: Commit** `feat(invoice): modelo do CT-e de frete no documento de entrada`, com o trailer `DB: AddCtePurchaseEntry`.

### Task 2: Chave do CT-e (modelo 57)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeKeyGuard.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesConfirmService.cs:51-55`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeKeyGuardTests.cs`, `PurchaseInvoicesConfirmSupplierNfeTests.cs`

**Interfaces:**
- Produces:
  - `SupplierNfeKeyGuard.AppliesTo` passa a valer também para `TaxDocumentKind.Cte`.
  - `Ensure` usa o modelo esperado `"57"` para Cte e `"55"` para Nfe. As mensagens trocam "NF-e" por "CT-e" no Cte:
    - "Informe a chave de acesso do CT-e da transportadora.";
    - "A chave não é de CT-e (modelo 57).".
  - Na confirmação, `supplierNfe.EnsureAuthorizedAsync` só roda quando `TaxDocumentKind == Nfe`.

- [ ] **Step 1: Write failing tests** em `SupplierNfeKeyGuardTests`:
  - `Cte_key_with_model_57_passes_and_fills_number_and_series`;
  - `Cte_rejects_nfe_model_key`, que espera a mensagem "A chave não é de CT-e (modelo 57).";
  - `Nfe_rejects_cte_model_key`, que espera "A chave não é de NF-e (modelo 55).";
  - `Cte_key_pasted_with_spaces_is_normalized`;
  - `Cte_key_of_other_issuer_is_rejected`.

  Monte a chave de teste com DV válido. Use o `CheckDigit` público/interno do guard, ou exponha-o como `internal` com `InternalsVisibleTo`, se ainda não estiver.

  Em `PurchaseInvoicesConfirmSupplierNfeTests`, crie `Cte_confirmation_does_not_consult_sefaz`. A filial está em Produção e o fake da SEFAZ falha se for chamado.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SupplierNfeKeyGuardTests|FullyQualifiedName~PurchaseInvoicesConfirmSupplierNfeTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** o filtro. Expected: PASS, inclusive os testes antigos de NF-e.
- [ ] **Step 5: Commit** `feat(invoice): conferência da chave do CT-e (modelo 57)`.

### Task 3: Regras do documento CT-e

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceCteRules.cs`
- Modify: `PurchaseInvoicesCreateService.cs`, `PurchaseInvoicesUpdateService.cs` (cópia dos campos novos e chamada da regra), `PurchaseInvoicesConfirmService.cs`, `PurchaseInvoiceSupplierItemNumbers.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs` e o Update do documento de saída (recusar Cte)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceCteRulesTests.cs`, mais um teste no arquivo de testes de criação de `SalesInvoices`

**Interfaces:**
- Produces: `public static class PurchaseInvoiceCteRules`.
  - `bool IsCte(PurchaseInvoice invoice)`.
  - `Task EnsureAsync(AppDbContext ctx, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)`. Recusa, nesta ordem:

    | Condição | Mensagem |
    |---|---|
    | emissão própria | "CT-e é sempre documento de terceiro." |
    | tipo ≠ Normal | "CT-e só pode ser do tipo Normal." |
    | UF de início ou fim vazia ou com tamanho ≠ 2 | "Informe a UF de início e a UF de fim da prestação do CT-e." |
    | `FreightPayerTaxId` vazio | "Informe o CNPJ do tomador do CT-e." |
    | `FreightPayerTaxId` diferente, em dígitos, do `Branch.TaxId` da filial do documento | "O tomador do CT-e (CNPJ {x}) não é a filial do documento." |
    | número de linhas ≠ 1 | "O CT-e tem uma única linha: o serviço de transporte." |

  - Normaliza as UFs para maiúsculas e o `FreightPayerTaxId` para dígitos.
- Create, Update e Confirm chamam `EnsureAsync` quando `IsCte`.
- `PurchaseInvoiceSupplierItemNumbers.Ensure` retorna logo para Cte.
- O Update copia os 7 campos novos do cabeçalho.
- O documento de saída recusa `TaxDocumentKind.Cte` com "CT-e não é documento de saída.".

- [ ] **Step 1: Write failing tests**, um por linha da tabela de recusas.
  - `Cte_with_xml_saves_without_reading_it_as_nfe`: grava com `XmlData` de um CT-e e linha com `NfeItemNumber = null`. Deve passar.
  - `Update_copies_cte_header_fields`.
  - `Sales_invoice_rejects_cte_kind`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PurchaseInvoiceCteRulesTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** o filtro mais `FullyQualifiedName~PurchaseInvoices`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(invoice): regras do documento CT-e`.

### Task 4: Tributos pelas UFs da prestação, sem DIFAL

**Files:**
- Modify: `SiagroB1.Application/Services/Taxes/TaxLineCalculator.cs` (record `TaxLineRequest` e `CalculateAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:110-148`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceDifal.cs` (`EvaluateAsync`)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceCteTaxTests.cs`

**Interfaces:**
- Produces: `TaxLineRequest` ganha o último parâmetro opcional `bool? CfopInState = null`. O CFOP usa `CfopInState ?? InState`; o resto do cálculo usa `InState`. Os chamadores atuais não mudam.
- Para o CT-e, o `ApplyAsync` monta assim:
  - `InState = FreightStartState == FreightEndState`;
  - `OriginState = FreightStartState`;
  - `DestinationState = FreightEndState`;
  - `CfopInState = FreightStartState == branch.StateCode`.
- `PurchaseInvoiceDifal.EvaluateAsync` devolve `None` para Cte.

- [ ] **Step 1: Write failing tests.** Natureza de entrada "FRETE SOBRE VENDAS": CFOP 1353/2353, ICMS interno CST 00 a 12%, fora do estado CST 00 automático, PIS/COFINS CST 50. Produto `FRETE` com NCM `00000000` e origem 0. Filial SP.
  - `Freight_starting_in_branch_state_to_other_state_uses_in_state_cfop_and_interstate_rate`: SP→MT gera CFOP 1353 e alíquota 7%.
  - `Freight_starting_in_other_state_uses_out_state_cfop`: PR→SP gera CFOP 2353 e alíquota 12%.
  - `Freight_inside_other_state_uses_out_state_cfop_and_internal_rule`: PR→PR gera CFOP 2353 e a regra interna da natureza.
  - `Freight_inside_branch_state_is_fully_internal`: SP→SP gera CFOP 1353 e 12%.
  - `Cte_has_no_difal_even_with_use_and_consumption_usage`.
  - `Nfe_calculation_is_unchanged`: um caso existente de compra com UF do fornecedor.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PurchaseInvoiceCteTaxTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** o filtro mais `FullyQualifiedName~Tax`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(invoice): tributos do CT-e pelas UFs da prestação`.

### Task 5: Leitor do XML do CT-e

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/SupplierCteXmlReader.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/SupplierCteXmlReaderTests.cs`, com um XML `cteProc` 4.00 inline, como em `PurchaseInvoicesImportXmlServiceTests`

**Interfaces:**
- Produces:
  - `public static bool IsCte(byte[] xmlData)`: verdadeiro se existir um elemento `infCte` no namespace `http://www.portalfiscal.inf.br/cte`.
  - `public static SupplierCte Read(byte[] xmlData)`.
  - `public sealed record SupplierCte(...)`, com os campos:

    | Campo | Tipo | Origem no XML |
    |---|---|---|
    | `AccessKey` | `string` | `infCte/@Id` sem o prefixo "CTe" |
    | `Number` | `string?` | `ide/nCT` |
    | `Series` | `string?` | `ide/serie` |
    | `IssueDate` | `DateTime?` | `ide/dhEmi` |
    | `CteType` | `string?` | `ide/tpCTe` |
    | `StartState` | `string?` | `ide/UFIni` |
    | `EndState` | `string?` | `ide/UFFim` |
    | `StartCity` | `string?` | `ide/xMunIni` |
    | `EndCity` | `string?` | `ide/xMunFim` |
    | `IssuerTaxId` | `string?` | `emit/CNPJ` ou `emit/CPF` |
    | `IssuerName` | `string?` | `emit/xNome` |
    | `PayerTaxId` | `string?` | ver abaixo |
    | `ServiceTotal` | `decimal` | `vPrest/vTPrest` |
    | `AmountReceivable` | `decimal` | `vPrest/vRec` |
    | `IcmsCst` | `string?` | `CST` do primeiro filho de `imp/ICMS`; ICMSSN vira "SN" |
    | `IcmsValue` | `decimal` | `vICMS` desse filho; 0 se não houver |
    | `Observations` | `string?` | `compl/xObs` |
    | `TransportedKeys` | `IReadOnlyList<string>` | `infCTeNorm/infDoc/infNFe/chave` |

  - `PayerTaxId` sai assim:
    - `ide/toma3/toma` (CT-e 4.00; nas versões antigas `toma03`): 0 = `rem`, 1 = `exped`, 2 = `receb`, 3 = `dest`, e lê o `CNPJ`/`CPF` desse grupo;
    - `ide/toma4`: lê o `CNPJ`/`CPF` direto do grupo.
  - Números em cultura invariante. Recusas, como no leitor de NF-e: "Arquivo XML vazio.", "Arquivo não é um XML válido." e "XML não parece um CT-e: elemento infCte não encontrado.".

- [ ] **Step 1: Write failing tests.**
  - `Reads_header_values_and_transported_keys`: o XML de exemplo tem duas `infNFe/chave`, toma3 = 3 (dest), vTPrest 1500.00, vRec 1500.00 e ICMS00 com vICMS 105.00.
  - `Payer_from_toma4`.
  - `Payer_from_toma3_sender`.
  - `Simples_nacional_icms_reads_as_SN`.
  - `Nfe_xml_is_not_cte`, que testa `IsCte` com uma NF-e.
  - `Garbage_is_rejected`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SupplierCteXmlReaderTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** com `XDocument`, como `SupplierNfeXmlReader`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(invoice): leitor do XML do CT-e`.

### Task 6: Importação do CT-e

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs`
- Modify: `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportCteTests.cs`

**Interfaces:**
- Consumes: `SupplierCteXmlReader` (Task 5) e o `ResolveCardCode` existente.
- Produces: o DTO ganha os campos abaixo. O frontend lê esses nomes em PascalCase.
  - `TaxDocumentKind` (default `Nfe`), `BranchCode`;
  - `FreightStartState`, `FreightEndState`, `FreightStartCity`, `FreightEndCity`, `FreightPayerTaxId`;
  - `CteIcmsCst`, `CteIcmsValue`;
  - `TruckingCompanyCode`;
  - `List<string> Warnings`.

  O rascunho do CT-e sai assim:
  - `TaxDocumentKind = Cte`, `InvoiceType = Normal`;
  - `CardCode = TruckingCompanyCode` = parceiro do CNPJ do emitente;
  - `BranchCode` = filial cujo `TaxId`, em dígitos, é igual ao `PayerTaxId`;
  - `TotalDocumentValue = ServiceTotal` e `TaxPayerComments = Observations`;
  - uma linha com:
    - `ItemCode = Branch.FreightServiceItemCode` (pode ser null);
    - `ItemName` = nome do produto, ou "SERVIÇO DE TRANSPORTE" sem produto;
    - `Quantity = 1` e `UnitPrice = ServiceTotal`;
    - `UnitOfMeasureCode` = a UM padrão do produto, se houver.

  Recusas e avisos:
  - `tpCTe ≠ "0"` recusa com "Só CT-e normal pode ser escriturado nesta versão (tpCTe {x}).".
  - Tomador que não é filial recusa com "O tomador do CT-e (CNPJ {x}) não é uma filial cadastrada.".
  - Se `AmountReceivable ≠ ServiceTotal`, entra em `Warnings`: "O valor a receber do CT-e (R$ {vRec}) difere do total da prestação (R$ {vTPrest}); confira o título a pagar."
  - Se a filial não tiver `FreightServiceItemCode`, entra em `Warnings`: "Filial sem produto padrão de frete: informe o produto da linha."
- `ExecuteAsync` escolhe o caminho por `SupplierCteXmlReader.IsCte(xmlData)`. O caminho da NF-e não muda.
- O vínculo com as NF-e transportadas **não** vai no rascunho. Ele nasce na gravação a partir do `XmlData` (Task 7).

- [ ] **Step 1: Write failing tests.**
  - `Cte_draft_has_carrier_branch_states_and_single_service_line`.
  - `Cte_with_other_type_is_rejected`.
  - `Cte_with_unknown_payer_is_rejected`.
  - `Cte_with_receivable_different_from_total_warns`.
  - `Branch_without_freight_item_warns_and_leaves_line_without_product`.
  - `Nfe_import_is_unchanged`, reaproveitando um caso de `PurchaseInvoicesImportXmlServiceTests`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PurchaseInvoicesImportCteTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** o filtro mais `FullyQualifiedName~ImportXml`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(invoice): importação do XML do CT-e`.

### Task 7: Vínculo com as NF-e transportadas

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceTransportedDocuments.cs` (regras)
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTransportedDocumentsService.cs` (actions)
- Create: `SiagroB1.Web/Controllers/PurchaseInvoiceTransportedDocumentsController.cs` (GET com `[EnableQuery]`, somente leitura)
- Create: `SiagroB1.Web/Actions/PurchaseInvoices/PurchaseInvoicesTransportedDocumentsActionsController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Modify: `PurchaseInvoicesCreateService.cs`, `PurchaseInvoicesDeleteService.cs`, `SalesInvoices/SalesInvoicesDeleteService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceTransportedDocumentsTests.cs`

**Interfaces:**
- Produces:
  - `PurchaseInvoiceTransportedDocuments.ResolveAsync(AppDbContext ctx, PurchaseInvoiceTransportedDocument doc, Guid cteKey)`. Procura um `SalesInvoice` com `ChaveNFe == AccessKey` e status ≠ Cancelado. Se não achar, procura um `PurchaseInvoice` com a mesma chave, `Key ≠ cteKey`, `TaxDocumentKind ≠ Cte` e status ≠ Cancelado. Preenche **uma** das FKs ou nenhuma. Confira o nome real do status cancelado no enum.
  - `PurchaseInvoiceTransportedDocuments.SeedFromXmlAsync(AppDbContext ctx, PurchaseInvoice cte)`: Cte com `XmlData` e sem vínculos → um vínculo por chave distinta de `SupplierCte.TransportedKeys`, cada um resolvido. É chamado no Create depois das regras e antes do `SaveChanges`.
  - `PurchaseInvoicesTransportedDocumentsService`:
    - `AddAsync(Guid cteKey, string accessKey)`: 44 dígitos, sem duplicar a chave no mesmo CT-e, resolve;
    - `RemoveAsync(Guid documentKey)`;
    - `RelinkAsync(Guid cteKey)`: resolve de novo todos os vínculos do CT-e.
    - Os três exigem CT-e **Pendente**, com a mensagem "Só é possível alterar os vínculos de um CT-e pendente.".
  - Actions OData sem bind, cada uma com um parâmetro:

    | Action | Parâmetro |
    |---|---|
    | `PurchaseInvoicesTransportedDocumentAdd` | `Key: Guid`, `AccessKey: string` |
    | `PurchaseInvoicesTransportedDocumentRemove` | `Key: Guid` |
    | `PurchaseInvoicesTransportedDocumentsRelink` | `Key: Guid` |

  - Entity set `PurchaseInvoiceTransportedDocuments`, somente leitura, com `$expand` de `PurchaseInvoice`, `SalesInvoice` e `TransportedPurchaseInvoice`.
  - Deletes:
    - o do documento de saída zera `SalesInvoiceKey` dos vínculos que o apontam;
    - o do documento de entrada zera `TransportedPurchaseInvoiceKey`;
    - os dois fazem isso antes de remover, no mesmo `SaveChanges`.

- [ ] **Step 1: Write failing tests.**
  - `Create_seeds_links_from_cte_xml_matching_sales_and_purchase_invoices`: duas chaves, uma de saída e uma de entrada.
  - `Unknown_key_is_kept_without_link`.
  - `Relink_finds_invoice_created_later`.
  - `Add_rejects_duplicate_and_malformed_key`.
  - `Links_cannot_change_after_confirmation`.
  - `Deleting_referenced_sales_invoice_clears_link`.
  - `Deleting_referenced_purchase_invoice_clears_link`.
  - `Deleting_cte_removes_its_links`.

  Os testes de delete devem rodar contra um banco que reproduza a FK. Se o InMemory não aplicar o Restrict, confira que o vínculo ficou nulo depois do delete.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PurchaseInvoiceTransportedDocumentsTests"`. Expected: FAIL.
- [ ] **Step 3: Implement**, inclusive a configuração OData e o registro dos serviços.
- [ ] **Step 4: Run** o filtro e depois `dotnet build SiagroB1.sln`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(invoice): vínculo do CT-e com as NF-e transportadas`.

### Task 8: Ciclo completo do CT-e (título, estorno, duplicidade)

**Files:**
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceCteLifecycleTests.cs`
- Modify: só se um teste falhar. Esperado: nenhum código novo de financeiro.

**Interfaces:**
- Consumes: as Tasks 1 a 7, `FinancialDocumentsOtherPurchaseService` e o padrão de fixture de `FinancialDocumentsOtherPurchaseTests`.

- [ ] **Step 1: Write tests.**
  - `Confirming_cte_with_other_purchase_usage_creates_payable_to_carrier`: R$ 1.500, condição 30/60, duas parcelas de R$ 750 para a transportadora, origem `OtherPurchaseInvoice`.
  - `Reversing_cte_cancels_open_titles`.
  - `Same_cte_key_twice_is_rejected`: a segunda gravação recusa pela unicidade da chave, e só existe um título depois de confirmar a primeira.
  - `Cte_with_contract_requiring_usage_is_rejected`: a regra de contrato pela natureza continua valendo.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PurchaseInvoiceCteLifecycleTests"`. Expected: PASS. Se algo falhar, corrija na tarefa dona do comportamento e anote.
- [ ] **Step 3: Run a suíte completa** `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`. Expected: tudo verde, exceto falhas pré-existentes conferidas contra o `main`.
- [ ] **Step 4: Commit** `test(invoice): ciclo do CT-e com título e estorno`.

### Task 9: Frontend — CT-e no formulário e na importação

Repo: `C:\Projetos\SiagroB1\siagro-b1-frontend-cte`. Leia o `CLAUDE.md` dele. Use os skills `ui5:*` e o MCP do UI5 para APIs.

**Files:**
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml`
  - Item "CT-e (frete)" (`Cte`) no Select "Tipo de documento", hoje em :121-131.
  - Grupo "Prestação do CT-e", visível com `ui>/isCte`: UF de início/fim (Select de UFs no padrão do formulário de parceiro), municípios e CNPJ do tomador.
  - Pesos e volumes escondidos com `ui>/isCte`.
  - Rótulo "Chave de acesso do CT-e" quando for CT-e.
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts`, com a flag `ui>/isCte` calculada em `refreshNfeMode`.
- Modify: `webapp/controller/purchaseInvoices/Add.controller.ts`
  - `applyImportedXml`/`createDraft` copiam os campos novos do DTO (Task 6).
  - Mostram `Warnings` num `MessageBox.warning` depois de criar o rascunho.
- Modify: `webapp/view/branchs/…`, campo "Produto padrão do frete (CT-e)" com o value help de produtos (padrão do formulário de filial).
- Modify: `webapp/view/purchaseInvoices/Main.view.xml` e `fragments/Filterbar.fragment.xml`, com a coluna e o filtro "Tipo de documento" (NF-e / CT-e / Outro) e o formatter em `model/formatter.ts`.
- Modify: `webapp/i18n/i18n.properties`, se o módulo usar i18n para esses rótulos (siga o padrão do arquivo).

- [ ] **Step 1:** Implemente as mudanças acima.
- [ ] **Step 2: Run** `yarn ts-typecheck` e `yarn lint`. Expected: sem erros novos.
- [ ] **Step 3: Commit** `feat(invoice): CT-e no formulário e na importação do documento de entrada`.

### Task 10: Frontend — NF-e transportadas, CT-e de frete e aviso de ICMS

**Files:**
- Create: `webapp/view/purchaseInvoices/fragments/TransportedDocuments.fragment.xml`
  - Tabela ligada a `/PurchaseInvoiceTransportedDocuments?$filter=PurchaseInvoiceKey eq …&$expand=SalesInvoice,TransportedPurchaseInvoice`.
  - Colunas: Chave, Tipo (Saída/Entrada/"Não encontrada"), Número, Parceiro e link para o documento.
  - Botões "Incluir chave", "Remover" e "Refazer vínculos", que chamam as actions da Task 7. Só aparecem com o documento Pendente.
- Modify: `webapp/view/purchaseInvoices/Edit.view.xml` e `Detail.view.xml`, com a seção "NF-e transportadas" visível com `ui>/isCte`.
- Create: `webapp/view/purchaseInvoices/fragments/CteFreights.fragment.xml`, a lista somente leitura "CT-e de frete":
  - mostra CT-e não cancelados com número, transportadora, valor e link;
  - entra no Detail do documento de saída (`webapp/view/salesInvoices/Detail.view.xml`), filtrando por `SalesInvoiceKey`;
  - entra no Detail do documento de entrada, filtrando por `TransportedPurchaseInvoiceKey` e visível só quando houver itens.
- Modify: `webapp/model/ServerRoutes.ts`, com as 3 rotas das actions.
- Modify: `webapp/view/purchaseInvoices/Detail.view.xml` e o controller, com um `MessageStrip` de aviso quando `CteIcmsCst` estiver preenchido e divergir da linha:
  - Divergência = CST diferente, ou `|CteIcmsValue − IcmsValue da linha| > 0,01`.
  - Texto: "O ICMS do XML do CT-e (CST {cst}, R$ {valor}) difere do calculado pela natureza (CST {cst}, R$ {valor}). Confira a natureza da linha."

- [ ] **Step 1:** Implemente as mudanças acima.
- [ ] **Step 2: Run** `yarn ts-typecheck` e `yarn lint`. Expected: sem erros novos.
- [ ] **Step 3: Commit** `feat(invoice): NF-e transportadas e CT-e de frete nas telas`.

### Task 11: Verificação ponta a ponta (navegador)

Siga as memórias "run-stack-locally" (profile `ceagui`, `yarn start:dev` no worktree do frontend) e "subagent-must-kill-by-pid".

- [ ] **Step 1:** Aplique a migration no `CEAGUI_SIAGRO_DEV` com `ASPNETCORE_ENVIRONMENT` explícito (memória "db-migration-profile-targets-production"). Suba Web, Gateway e frontend a partir dos worktrees.
- [ ] **Step 2: Cadastro.**
  - Produto `FRETE` (NCM `00000000`, origem 0) e o mesmo produto como produto de frete da filial.
  - Natureza "Frete sobre vendas", de entrada: sem contrato, gera financeiro, CFOP 1353/2353, ICMS CST 00 12%, PIS/COFINS 50.
- [ ] **Step 3: XML de teste.** Monte um XML de CT-e com:
  - tomador = CNPJ da filial;
  - emitente = uma transportadora cadastrada;
  - UFIni SP e UFFim MT;
  - `infNFe/chave` = chave de uma NF-e de saída existente no dev.

  Guarde o XML em `C:\Projetos\SiagroB1\nfe-homologacao-testes\cte-2026-10-17\`.
- [ ] **Step 4: Caminho do usuário, a partir da home.** Abra Documento de Entrada, Incluir, Importar XML. Confira:
  - transportadora, filial, UFs e valor;
  - a natureza informada na linha;
  - depois de salvar: CFOP 1353, ICMS 7% e "NF-e transportadas" com a NF-e de venda vinculada.
- [ ] **Step 5:** Confirme e veja as parcelas no Financeiro. Abra a NF-e de venda e veja a seção "CT-e de frete". Estorne a confirmação e veja os títulos cancelados.
- [ ] **Step 6:** Digite um CT-e à mão, sem XML, com os mesmos dados, e confira que os tributos são os mesmos.
- [ ] **Step 7:** Derrube a stack, matando só pelos PIDs que você subiu. Registre os achados e prints na pasta do Step 3.
