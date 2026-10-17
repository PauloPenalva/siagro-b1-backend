# Outras compras no Documento de Entrada — design (2026-10-17)

> Aprovado pelo usuário em 17/10/2026. Escopo: só STANDALONE.

## Context

Hoje o Documento de Entrada só gera contas a pagar pelo caminho do grão: o título firme nasce do vínculo
NF × romaneio na Conciliação Fiscal (ou do Complemento de valor), sempre com contrato, e **não aceita baixa
direta** — sai só por composição no provisório, dação ou compensação (`FinancialDocumentsSettlementGuardService`,
regra 15). Não há como escriturar uma NF de serviço, uso e consumo, insumo ou ativo e pagá-la.

Caso que motiva: um prestador emite NFS-e; escrituro no Siagro B1; nasce contas a pagar firme que eu baixo ao pagar.
Sem título provisório.

Proposta do usuário: a **natureza de entrada** decide se exige contrato, como já faz a de saída
(`UsageEffect.RequiresContract`, hoje só lido por `SalesInvoicesUsageGuardService`).

Decisões do usuário (17/10/2026): parcelas pela condição de pagamento · retenções JÁ nesta entrega · alíquotas na
natureza com valor editável · um título de recolhimento por imposto retido · qualquer produto ativo na linha ·
**só STANDALONE**.

## Desenho

### 1. Natureza de entrada passa a mandar no contrato
- `PurchaseInvoiceLineGuard` (Create, ItemsCreate, ItemsUpdate, `SyncItems` do Update — os mesmos 4 pontos do
  `EnsureContractIsCompatibleAsync`) passa a ler `RequiresContract` da natureza da linha:
  exige → contrato obrigatório; não exige → contrato recusado na linha.
- **"Outra compra"** = linha com natureza de entrada `RequiresContract = false` e `CreatesFinancialDocument = true`.
- ⚠️ **Backfill obrigatório** na migration: naturezas de entrada existentes com `CreatesFinancialDocument = true`
  ganham `RequiresContract = true`. Sem isso viram "outra compra" e passam a gerar títulos na confirmação
  (a natureza 7 da CEAGUI dev está nesse estado).
- `UsageEffectWriter.ValidateEffects`: natureza com papel de depósito/custódia ou tipo Complemento de valor
  continua exigindo contrato como hoje.
- *Minha decisão:* um documento não mistura linhas de contrato e de outra compra (recusa com mensagem).
- `FinancialDocumentsFirmSyncService.EnqueueSyncAsync` já ignora linha sem contrato — nada muda no caminho do grão.

### 2. Retenções (ISS, IRRF, INSS, PIS, COFINS, CSLL)
- Alíquotas na natureza, como o Funrural: `USAGES.WithholdingIssRate/IrrfRate/InssRate/PisRate/CofinsRate/CsllRate`.
- Fotografia no **cabeçalho** do documento (NFS-e é de um serviço): base + valor por imposto e a flag
  `WithholdingsManual`.
  - `false` → o backend recalcula na gravação: base = soma de `GrandTotal` das linhas, valor = base × alíquota.
  - Editar um valor na tela liga a flag; botão "Recalcular pelas alíquotas" desliga.
- `NetPayable` (hoje Total − Funrural, `[NotMapped]`) passa a descontar também as retenções.
- Tela nova **"Configuração de Retenções"** (com migration de menu): por imposto, o parceiro credor
  (Prefeitura, Receita Federal) e o dia de vencimento no mês seguinte à emissão.
  Confirmar com retenção > 0 e imposto sem configuração é recusado.

### 3. Títulos na confirmação (`PurchaseInvoicesConfirmService`)
Novo serviço `FinancialDocumentsOtherPurchaseService.EnqueueAsync(invoice, lines, user)`, ENQUEUE-ONLY e fora de
transação, no mesmo SaveChanges da confirmação (padrão do `EnqueueForValueComplementAsync`). Só em STANDALONE,
tipo Normal, documento de outra compra.
- **Prestador:** uma parcela por prazo de `PaymentInstallmentCalculator`
  (`SiagroB1.Fiscal/Payments/PaymentInstallmentCalculator.cs`) sobre o total menos o Funrural, a partir da condição de pagamento
  do documento. Sem condição, um título vencendo na emissão. Nature `Firm`.
  *Minha decisão:* as retenções saem da 1ª parcela, que é recusada se ficar ≤ 0.
- **Recolhimento:** um título por imposto com valor > 0. Nature `TaxWithholding`, já existente e sem uso.
  Credor e vencimento vêm da configuração.
- Origem nova `FinancialDocumentOrigin.OtherPurchaseInvoice = 9`, com `OriginKey` = chave do documento,
  `PurchaseInvoiceKey` preenchido e sem contrato. Por não cair em `IsPurchaseFirmPayable`, o título **aceita baixa
  direta** e não entra em composição, dação ou compensação. Conferir o filtro do índice
  `IX_FINANCIAL_DOCUMENTS_FirmOrigin`, já que há N títulos com a mesma origem.
- Idempotente: se já houver títulos ativos dessa origem para o documento, não cria de novo.
- **Estorno da confirmação / cancelamento** (`PurchaseInvoicesReverseConfirmService`, `PurchaseInvoicesCancelService`):
  cancela os títulos em aberto. Recusa se algum tiver baixa, pedindo para estornar a baixa antes.

### 4. Frontend (`siagro-b1-frontend`)
- Cadastro de Naturezas: "Exige contrato" visível também para entrada, mais a seção Retenções (6 alíquotas).
- Documento de Entrada:
  - Value help de produto sem o filtro `ItmsGrpCod == 105` quando a natureza da linha não exige contrato.
  - Coluna Contrato oculta ou recusada nessas linhas.
  - Seção "Retenções" no cabeçalho, editável, com o botão de recalcular.
  - O Detail lista os títulos gerados com link para o Financeiro.
- Tela de Configuração de Retenções + entrada de menu.

### Fora do escopo
Modo SAPB1 · EFD-REINF (R-2010/R-4020), que fica para o roadmap de contabilidade · contabilização ·
importação de XML de NFS-e · retenção na fonte para PF (carnê-leão).

## Arquivos críticos
- Backend: `SiagroB1.Domain/Entities/{UsageEffect,Usage,PurchaseInvoice,FinancialDocument}.cs`,
  `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs`,
  `SiagroB1.Application/Services/PurchaseInvoices/{PurchaseInvoiceLineGuard,PurchaseInvoicesConfirmService,PurchaseInvoicesReverseConfirmService,PurchaseInvoicesCancelService}.cs`,
  `SiagroB1.Application/Services/UsageEffectWriter.cs`,
  `SiagroB1.Application/Services/Financials/` (serviço novo + `FinancialDocumentsSettlementGuardService`, que não muda,
  mas o teste prova a baixa), nova migration com backfill e menu.
- Frontend: `webapp/controller/purchaseInvoices/*`, fragmentos de itens e cabeçalho, cadastro de naturezas,
  tela nova de configuração.

## Execução
Seguir o CLAUDE.md: spec em `docs/superpowers/specs/2026-10-17-other-purchases-design.md` + plano via
`superpowers:writing-plans`, branch `feature/other-purchases` nos 2 repos (worktree irmão), TDD, commits por tarefa,
nunca push. context7 para EF Core/OData quando necessário.

## Verificação
1. `dotnet test` no `SiagroB1.Application.Tests`, com testes novos para:
   - guard de contrato por natureza nos 4 pontos;
   - backfill que preserva o comportamento;
   - parcelas pela condição e retenção descontada da 1ª parcela;
   - títulos de recolhimento com credor e vencimento;
   - baixa direta aceita;
   - estorno com e sem baixa;
   - documento misto recusado;
   - caminho do grão intacto.
2. `yarn ts-typecheck` + `yarn lint` no frontend.
3. Navegador em STANDALONE (profile `ceagui`, `yarn start:dev`), pelo caminho do usuário a partir da home:
   - cadastrar a natureza "Serviço tomado" (sem contrato, gera financeiro, ISS 5 %, IRRF 1,5 %);
   - configurar os credores;
   - lançar uma NFS-e de R$ 1.000 com condição 30/60, ajustar o ISS à mão e confirmar;
   - conferir 2 parcelas (R$ 435 + R$ 500) e 2 títulos de recolhimento;
   - baixar a 1ª parcela no Financeiro;
   - tentar estornar a confirmação (recusa), estornar a baixa e então estornar a confirmação (títulos cancelados).
4. Derrubar a stack ao terminar (matar só pelo PID).
