# Contribuição social do produtor rural pelo cadastro do parceiro

Data: 18/10/2026 · Modo: **somente STANDALONE** · Repositórios: backend e frontend
(dois commits, um por repositório).

## 1. Intenção e porquê

Este trabalho é **conferência preventiva**: não houve caso real de erro. A comparação com o
Protheus 12.2310 encontrou esta lacuna (referência em
`.claude/skills/conferir-tributos/references/protheus-tributos-legados.md`, seção 7, na branch
`feature/skill-conferir-tributos`).

**Funrural sem amarração com o fornecedor.** Hoje quem decide se há retenção é só a natureza
   de operação, e é ela que carrega as alíquotas: há naturezas distintas para PF, PJ e decisão
   judicial (spec `2026-10-09-funrural-design.md`). Nada confirma que o fornecedor é produtor
   rural nem de que tipo. O CNPJ não serve para decidir, porque **em SP o produtor rural pessoa
   física tem CNPJ** (fala do usuário). Pela Lei 8.212 (art. 30, IV), o comprador só se
   sub-roga quando o produtor é **PF ou segurado especial**. O Protheus segue essa regra e não
   retém de fornecedor PJ (`FISXFUNRUR.PRW:34-35`). O usuário só viu na prática retenção de PF.
Há ainda um ganho de base: os campos de produtor rural no parceiro são os mesmos que o evento
**R-2055 da EFD-REINF** vai exigir (roadmap de contabilidade e REINF).

## 2. Metas, não-metas e anti-metas

**Metas**
- O parceiro (fornecedor, STANDALONE) diz se é produtor rural, de que tipo, o CPF e se é optante
  pela folha.
- A natureza diz apenas **como a operação incide**: não incide, contribuição completa ou só SENAR. As alíquotas saem da
  natureza e vão para uma tabela global com vigência.
- A linha retém só quando natureza, parceiro e documento permitem (§4.4).

**Não-metas**
- O evento R-2055 em si e a marca de PAA (entram com o R-2055).
- O modo SAPB1, onde o parceiro vem do SAP: nada muda lá.
- O mínimo de R$ 10 das retenções (PCC/IRRF), adiado pelo usuário em 18/10.
- Mudar o texto "Funrural" do `infCpl` da NF-e e do relatório de Funrural retido.

**Anti-metas** (falharia mesmo funcionando)
- Mudar o Funrural de documentos já gravados ou confirmados. As fotografias da linha não podem
  ser recalculadas por esta mudança.
- Passar a reter em documento que hoje não retém (custódia, CT-e, complemento, contranota,
  remessa) porque a alíquota agora é global.

## 3. Restrições

- Somente STANDALONE: parceiro, natureza e tabela de alíquotas seguem o padrão
  `ui>/standalone` / `identityEditable` no frontend e `ErpMode.IsStandalone` no backend.
- Código em inglês; o que o usuário lê, em pt-BR (CLAUDE.md).
- Migrations EF com trailer `DB:` no commit. O Web recusa subir com migration pendente.
- Testes: xUnit + EF InMemory. A migration SQL (backfill) não roda nos testes.

## 4. Contribuição social do produtor rural

### 4.1 Parceiro de negócio (BUSINESS_PARTNERS), só STANDALONE

| Campo (inglês) | Tela (pt-BR) | Regra |
|---|---|---|
| `IsRuralProducer` bool | Produtor rural | default `false` |
| `RuralProducerType` enum `Individual`=1, `SpecialInsured`=2, `LegalEntity`=3 | Tipo de produtor: Pessoa física / Segurado especial / Pessoa jurídica | obrigatório quando produtor rural; nulo quando não |
| `RuralProducerCpf` string(11) | CPF do produtor | obrigatório quando produtor e tipo ≠ PJ **e** o `TaxId` do parceiro for CNPJ; dígitos verificadores validados; só dígitos |
| `RuralPayrollOption` bool | Optante pela contribuição sobre a folha | só faz sentido para PF e segurado especial; zerado quando PJ |

- Os campos aparecem no formulário do parceiro só no STANDALONE (`Form.fragment.xml`, mesmo
  padrão de Armazém e IE).
- Precisam entrar no `create()` do `Add.controller.ts`, que exige todas as propriedades.
- No backend, eles passam por `BusinessPartnerModel`, pelo `BusinessPartnerService` (GetById,
  QueryAll, Create e Update campo a campo) e por uma validação no estilo de `ValidateNfeFields`.
- O serviço SAP deixa os campos no default.

### 4.2 Natureza de operação (USAGES)

- Sai: `FunruralInssRate`, `FunruralRatRate` e `FunruralSenarRate`.
- Entra: `RuralContributionMode` enum (`None`=0, `Full`=1, `SenarOnly`=2). Na tela, uma lista
  **"Contribuição social"**: "Não incide" / "Contribuição completa (INSS + RAT + SENAR)" /
  "Somente SENAR".
- É o mesmo arranjo dos clientes Protheus, que usam duas TES, por exemplo "COMPRA PARA REVENDA
  (FUNRURAL)" e "COMPRA PARA REVENDA (SENAR)" (fala do usuário).
- A seção da tela muda de "Funrural (retenção do produtor rural)" para **"Contribuição Social"**
  (pedido do usuário). Continua visível só para natureza de entrada.
- O mapper (`UsageTaxationMapper`) grava o modo só quando a natureza é de entrada. Na saída,
  força `None`.
- Exemplos:
  - Compra de grão do produtor: contribuição completa.
  - **Compra com fim de exportação, amparada pela liminar da empresa: somente SENAR** (decisão do
    usuário).
  - Remessa, armazenagem/custódia, retorno, CT-e e complemento: não incidem.

### 4.3 Tabela global de alíquotas (RURAL_CONTRIBUTION_RATES)

Mesmo molde de `IBS_CBS_RATES` (entidade, serviço, controller só STANDALONE e tela aberta por
um botão na lista de naturezas).

| Coluna | |
|---|---|
| `StartDate` DATE | início da vigência |
| `Scheme` enum `Standard`=1, `PayrollOption`=2 | Comercialização / Optante pela folha |
| `InssRate`, `RatRate`, `SenarRate` DECIMAL(7,4) | percentuais, de 0 a 100 |

- Índice único em (`Scheme`, `StartDate`).
- A taxa efetiva é a linha mais recente com `StartDate <= data` daquele regime.
- Seed na migration, com vigência desde 01/01/2000 *(decisão minha: assim documentos pendentes antigos, ao serem regravados, não caem na regra 7)*:
  - `Standard` 1,32 / 0,11 / 0,20 (alíquotas informadas pelo usuário na spec de 09/10);
  - `PayrollOption` 0 / 0 / 0,20.

### 4.4 Regra da linha (PurchaseInvoicesTaxApplyService)

A data das alíquotas é a mesma da IBS/CBS: `IssueDate`, ou a do documento de origem na devolução
própria. O que acontece depende da linha:

| # | Situação | Resultado |
|---|---|---|
| 1 | Fora do gate atual (`IsActiveForAsync`: contranota, status, NF-e autorizada etc.) | Nada muda |
| 2 | Devolução própria de NF-e | Continua copiando as alíquotas da linha de origem (`:171-173`); não olha o parceiro nem a tabela |
| 3 | Natureza com modo `None` | `PurchaseInvoiceFunrural.Clear` |
| 4 | Modo `Full`, parceiro produtor PF ou segurado especial | Aplica a tabela: `PayrollOption` se `RuralPayrollOption`, senão `Standard`. Base = `GrandTotal` da linha, como hoje |
| 4b | Modo `SenarOnly`, parceiro produtor PF ou segurado especial | Só o SENAR do regime `Standard`; INSS e RAT ficam zerados. A opção pela folha não muda nada aqui |
| 5 | Modo `Full` ou `SenarOnly`, parceiro produtor PJ | `Clear` (PJ recolhe por conta própria). Sem erro |
| 6 | Modo `Full` ou `SenarOnly`, parceiro **não** marcado como produtor rural | `Clear`, sem erro: não retém. É o que o Protheus faz hoje (decisão do usuário: quem classifica a compra escolhe a natureza certa, e a retenção só existe quando natureza e parceiro concordam) |
| 7 | Modo `Full` ou `SenarOnly`, não existe linha vigente na tabela para o regime | Erro: "Cadastre as alíquotas de contribuição social vigentes em DD/MM/AAAA." |

Os cálculos (`FunruralCalculator`), as colunas da linha, o texto do `infCpl`, o `NetPayable`, os
títulos e o relatório **não mudam**. Muda só de onde vêm as alíquotas.

### 4.5 Migration e implantação

Uma migration `ReplaceUsageFunruralRatesWithContributionMode`:

1. Cria `RURAL_CONTRIBUTION_RATES` com o seed (idempotente, `IF NOT EXISTS`).
2. Adiciona `BUSINESS_PARTNERS.IsRuralProducer` (default 0), `RuralProducerType`,
   `RuralProducerCpf` e `RuralPayrollOption` (default 0).
3. Adiciona `USAGES.RuralContributionMode` (default 0).
4. Backfill:
   - INSS ou RAT > 0 → `Full`;
   - senão, SENAR > 0 → `SenarOnly`;
   - senão → `None`.
5. Remove as três colunas da USAGES. O `Down` as recria nulas.

**Ponto de atenção na implantação (CEAGUI):**
- Antes de implantar, marcar os parceiros produtores. Sem isso, os lançamentos novos deixam de
  reter a contribuição (regra 6), sem aviso.
- As naturezas "decisão judicial / só SENAR" viram `SenarOnly` pelo backfill. Confirmar que estão
  ligadas às compras para exportação.
- Documentos já gravados não são tocados (anti-meta).
- Conferir se alguma natureza da CEAGUI tinha alíquotas diferentes do seed (ex.: as de PJ 1,98 / 0,25 ou uma PF editada). O seed vale para todos; se a alíquota real for outra, ajuste a tabela antes de lançar.

## 5. Testes esperados

- Fiscal.Tests: os testes de `FunruralCalculator` continuam válidos.
- **Contribuição social:**
  - parceiro PF → Standard;
  - PF optante pela folha → só SENAR;
  - segurado especial;
  - PJ → zero;
  - não produtor + natureza que incide → zero, sem erro;
  - natureza que não incide → zero;
  - natureza "somente SENAR" com PF → só SENAR, inclusive para optante pela folha;
  - natureza "somente SENAR" com PJ → zero;
  - sem vigência → erro;
  - devolução própria → alíquotas da origem;
  - custódia, CT-e e complemento com natureza sem a marca → zero.
- **Parceiro:**
  - CPF obrigatório e válido quando CNPJ + PF;
  - campos limpos quando desmarca produtor;
  - SAPB1 ignora os campos.
- **Tabela de alíquotas:** os mesmos casos de `IbsCbsRatesServiceTests` (faixa, duplicidade e
  vigência por regime).

## 6. Deixado para o construtor

- Nomes finais de enums, colunas e serviços, desde que respeitem o padrão do repositório.
- Layout da tela de alíquotas (copiar a de IBS/CBS) e posição dos campos no formulário do parceiro.
