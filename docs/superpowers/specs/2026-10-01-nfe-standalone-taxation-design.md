# NF-e STANDALONE — sub-projeto 1: natureza de operação fiscal e cálculo de tributos

**Data:** 2026-10-01
**Branch:** `feature/nfe-standalone-taxation` (backend e frontend), a partir de `main` (`337fde4` / `56c7704`).
**Roadmap:** é o primeiro pedaço do sub-projeto 3 ("NF-e") de
`2026-08-04-general-purpose-sales-invoice-design.md` §"Sequência". **Reabre** a decisão de 04/08/2026
"sem motor de tributação — impostos digitados pelo usuário": agora a natureza calcula.

## 1. O pedido

> "Para empresas com a chave ERP=STANDALONE, se faz necessário a emissão de NF-e. Hoje temos o projeto
> EfisCloud em produção e gostaria que no Siagro B1 tivéssemos algo semelhante, principalmente o
> cadastro de USAGES (que é relativo a Natureza de Operação no EfisCloud). Qualquer
> validação/guarda/regra de negócio referente a emissão de NF-e STANDALONE, não deve refletir no
> ERP=SAP ou ERP=PROTHEUS."

**Quem é afetado:**

| Cliente | Modo | NF-e | Efeito deste trabalho |
|---|---|---|---|
| CEAGUI (cerealista LTDA, Itaberá/SP) | STANDALONE | **vai emitir pelo Siagro** | cálculo, trava e guardas |
| MH Agro | STANDALONE | não emite | **nenhum** — continua como hoje |
| Yokotobi | SAPB1 | emite no SAP; usa "Informar Nota Fiscal" | **nenhum** — continua como hoje |

**PROTHEUS não existe no código.** `SiagroB1.Web/Program.cs` recusa subir com `Erp` diferente de
`SAPB1`/`STANDALONE`. Por isso tudo aqui é amarrado a um teste **positivo** de `STANDALONE`: um modo
futuro qualquer nasce sem tributação, sem guarda e sem tela nova.

## 2. Decisões do usuário (fechadas no brainstorming)

| # | Decisão |
|---|---|
| D1 | A natureza **calcula** os tributos e a linha do documento fica **travada** — a natureza é a única fonte; exceção se resolve com outra natureza. |
| D2 | Tributos cobertos: **ICMS, PIS/COFINS e IBS/CBS**. IPI, ICMS-ST e DIFAL ficam fora. |
| D3 | ICMS em **dois blocos — dentro e fora do estado** — escolhidos pela mesma comparação de UF que já escolhe o CFOP. A alíquota interestadual é **automática** (Resolução do Senado: 7%/12%, 4% para importado). |
| D4 | A natureza guarda **CST e CSOSN**; o regime tributário (CRT) da filial escolhe qual sai. |
| D5 | Alíquotas de IBS/CBS numa **tabela global por vigência**; a natureza guarda CST, cClassTrib e % de redução. |
| D6 | Ativação por **chave na filial** ("Emite NF-e pelo Siagro"). Sem ela, a filial STANDALONE segue exatamente como hoje. |
| D7 | **NF-e de entrada desde o início**: a natureza ganha tipo Entrada/Saída e CFOPs de entrada já neste sub-projeto; a emissão de entrada emenda com a de saída. |
| D8 | Tributação em **colunas novas da própria `USAGES`** (não tabela separada, não "regra fiscal" compartilhável). |
| D9 | CSOSN 101 fica fora (exige a alíquota de crédito do Simples, mensal). Entra a caixa **"Excluir o ICMS da base do PIS/COFINS"** (Tema 69 do STF). |
| D10 | A tela de alíquotas IBS/CBS é um **botão na lista de naturezas**, não item de menu (menus são por perfil na base comum e não sabem o modo). |
| D11 | As colunas de alíquota da linha passam de `DECIMAL(5,4)` para `DECIMAL(7,4)` e guardam **percentual** (como a NF-e). |
| D12 | A correção dos **dois bugs de endereço do parceiro STANDALONE** entra aqui, porque o cálculo lê a UF do cliente. Os campos novos de endereço (número, município IBGE) ficam no sub-projeto 2. |
| D13 | A natureza continua **por item** (`SALES_INVOICES_ITEMS.UsageCode`, como desde 04/08) — nada de natureza no cabeçalho. |
| D14 | A natureza ganha as flags **"Movimenta estoque"** (estoque **fiscal** — saldo por produto/filial movido pelos documentos fiscais, base do Bloco H; nada a ver com o saldo gerencial de grãos por romaneio) e **"Gera financeiro"** (conta a receber na saída / a pagar na entrada). Equivalem ao "Atualiza Estoque" e ao "Gera Duplicata" da TES do Protheus. **Só cadastro neste sub-projeto**: gravadas na natureza e copiadas para a fotografia da linha, **sem efeito** ainda. |

## 3. Decomposição da NF-e

1. **Este spec** — natureza fiscal (E/S), alíquotas IBS/CBS, chave/CRT na filial, origem/NCM no
   produto, cálculo com linha travada no documento de saída, bugs de endereço do parceiro.
2. **Emissão de NF-e de saída** — certificado A1, ambiente, série/numeração fiscal, emitente e
   destinatário completos (IE, endereço com município IBGE, e-mail), CEST/GTIN, geração/assinatura/
   transmissão (a biblioteca `Zeus.Net.NFe.NFCe` já está referenciada em `SiagroB1.Infra`, sem uso),
   status da NF-e, XML, DANFE, cancelamento, CC-e, inutilização, destino do "Informar Nota Fiscal".
   **Decisão pendente para lá:** o XML tem um único `ide/natOp` no cabeçalho, mas a natureza é por
   item (D13) — definir qual texto vai quando o documento misturar naturezas.
3. **NF-e de entrada de emissão própria** — campos fiscais na linha do documento de entrada,
   reaproveitando o cálculo do 1 e a transmissão do 2; efeito da natureza de entrada em contrato de compra.
4. **Depois** — contingência, manifestação do destinatário.

**Consumidores futuros das flags de D14** (cada um com brainstorming próprio):
- **"Gera financeiro"** → Fase 2 do financeiro (`2026-09-07-financial-documents-design.md`: o documento
  fiscal gera/firma o título; `FinancialDocumentOrigin` já reserva `SalesInvoice = 4` e
  `PurchaseInvoice = 3`).
- **"Movimenta estoque"** → sub-projeto novo **Estoque fiscal** (razão de movimentos por produto e
  filial, saldo, valorização, estoque em poder de terceiros, base do Bloco H).

**Não entra neste spec:** tudo de 2–4; IPI, ST, DIFAL; CSOSN 101; CST de IBS/CBS além de
000/200/400/410; qualquer regra por NCM/UF além da 7/12/4; cálculo em documento do tipo Devolução;
prévia ao vivo do cálculo na tela; qualquer **efeito** das flags de estoque e financeiro.

## 4. A regra de ativação (uma só, num lugar só)

**Tributação ativa** ⇔ `configuration["Erp"]` normalizado == `"STANDALONE"` **E**
`Branch.IssuesNfe == true` para a filial do documento (`DocumentEntity.BranchCode`).

- Um serviço único, `TaxCalculationGate` (`IsActiveAsync(string? branchCode)`), registrado em
  `AddApplicationServices()` (nos dois modos — ele mesmo lê o modo). Cálculo, trava, guardas e telas
  perguntam a ele; ninguém mais combina modo + chave por conta própria.
- Filial sem código ou inexistente ⇒ inativa.
- Exposto à tela pela função OData unbound `TaxCalculationIsActive(BranchCode)` → `bool`.
- **SAPB1 com `IssuesNfe = 1` gravado direto no banco ⇒ inativa.** É contraprova obrigatória nos testes.

| Ambiente | Comportamento |
|---|---|
| SAPB1 | Nada muda. Campos novos ocultos (natureza, filial, produto, diálogo fiscal). Nenhuma guarda nova. Tolerância do faturamento de romaneio intacta. |
| STANDALONE, chave desligada | Documento idêntico ao de hoje. Seções tributárias da natureza aparecem, mas **opcionais**. |
| STANDALONE, chave ligada | Cálculo, trava, guardas. |

## 5. Modelo de dados

Nomes em inglês, textos de tela em pt-BR. Percentuais em `DECIMAL(7,4)` guardam **percentual**
(18% = `18.0000`). Valores monetários em `DECIMAL(18,2)`.

### 5.1 `USAGES` (só STANDALONE; vazia em SAPB1)

| Coluna | Tipo | Tela |
|---|---|---|
| `Direction` | INT NOT NULL DEFAULT 1 (enum padrão da casa) — `UsageDirection { Outgoing = 1, Incoming = 2 }`; no `UsageModel` é anulável (SAPB1 = nulo) | Tipo (Saída/Entrada) |
| `CfopIncomingInState`, `CfopIncomingOutState` | VARCHAR(4) | CFOP dentro/fora (quando Entrada) |
| `InvoiceOperationText` | VARCHAR(60) | Texto na nota (natOp) — opcional; vazio ⇒ NF-e usa `Name` |
| `DefaultAdditionalInfo` | VARCHAR(2000) | Informações complementares padrão (infCpl, sub-projeto 2) |
| `MovesFiscalInventory` | BIT NOT NULL DEFAULT 0 | Movimenta estoque (fiscal) — sem efeito neste sub-projeto (D14) |
| `CreatesFinancialDocument` | BIT NOT NULL DEFAULT 0 | Gera financeiro (conta a receber/pagar) — sem efeito neste sub-projeto (D14) |
| `IcmsInStateCst`, `IcmsInStateCsosn` | VARCHAR(3) | ICMS dentro: CST / CSOSN |
| `IcmsInStateRate` | DECIMAL(7,4) NULL | ICMS dentro: alíquota % |
| `IcmsInStateBaseReduction` | DECIMAL(7,4) NULL | ICMS dentro: redução de base % |
| `IcmsInStateDeferral` | DECIMAL(7,4) NULL | ICMS dentro: diferimento % |
| `IcmsInStateBenefitCode` | VARCHAR(10) | ICMS dentro: cBenef |
| `IcmsOutStateCst`, `IcmsOutStateCsosn`, `IcmsOutStateBaseReduction`, `IcmsOutStateDeferral`, `IcmsOutStateBenefitCode` | idem | ICMS fora (**sem alíquota** — é automática) |
| `PisCst`, `CofinsCst` | VARCHAR(2) | CST PIS / CST COFINS |
| `PisRate`, `CofinsRate` | DECIMAL(7,4) NULL | Alíquota PIS % / COFINS % |
| `ExcludeIcmsFromPisCofinsBase` | BIT NOT NULL DEFAULT 0 | Excluir o ICMS da base do PIS/COFINS |
| `IbsCbsCst` | VARCHAR(3) | CST IBS/CBS |
| `IbsCbsClassCode` | VARCHAR(6) | cClassTrib |
| `IbsRateReduction`, `CbsRateReduction` | DECIMAL(7,4) NULL | Redução IBS % / Redução CBS % |

As naturezas existentes migram com `Direction = Outgoing` e todo o resto nulo.

**`UsageModel`** ganha as mesmas propriedades (nulas no SAPB1, como os CFOPs de entrada já são nulos no
STANDALONE hoje). O `UsageService` STANDALONE projeta e grava; o `SAP/UsageService` não muda de
comportamento (o `UpdateAsync` dele continua gravando só o efeito e ignora as colunas novas). No modo
STANDALONE, `CfopIncoming*` passa a vir de `USAGES`.

**Regras de natureza ligadas ao tipo:**
- Natureza `Incoming` não pode ser `IsDefault` (padrão do faturamento de romaneio).
- Trocar o tipo limpa os CFOPs do outro tipo; e o tipo **não pode mudar** se a natureza já estiver em
  uso em linha de documento (mesma consulta que hoje bloqueia o delete em `UsageService`).
- O bloco "Efeito no contrato" só aparece para `Outgoing` (efeito em contrato de compra é sub-projeto 3);
  natureza `Incoming` não exige linha em `USAGE_EFFECTS`.
- O value help de natureza do documento de saída mostra só `Outgoing` em STANDALONE (em SAPB1 o OUSG não
  tem tipo: filtro não se aplica).

### 5.2 Validação da natureza — coerência, nunca exigência

Executada no `UsageService` STANDALONE (create/update). **Natureza sem nenhum campo tributário é
válida** — senão a MH Agro seria obrigada a preencher. Regras (cada uma só se o campo estiver preenchido):

- CFOP: 4 dígitos; prefixo `5`/`6` (Saída, dentro/fora) ou `1`/`2` (Entrada, dentro/fora).
- CST ICMS ∈ {00, 20, 40, 41, 50, 51, 90}; CSOSN ∈ {102, 103, 300, 400, 900}.
- CST 00/20/51/90 e CSOSN 900 no bloco "dentro" exigem alíquota > 0.
- CST 20 exige redução > 0; CST 51 exige diferimento > 0; diferimento só com CST 51.
- CST 40/41/50 e CSOSN 102/103/300/400 não aceitam alíquota, redução nem diferimento.
- Percentuais entre 0 e 100.
- CST PIS/COFINS: Saída ∈ {01–09, 49}; Entrada ∈ {50–56, 60–67, 70–75, 98, 99}.
- CST IBS/CBS ∈ {000, 200, 400, 410}; cClassTrib exatamente 6 dígitos e obrigatório quando há CST;
  CST 200 exige alguma redução > 0; 400/410 não aceitam redução.

**"Tributação configurada"** (o que o documento exige quando a regra está ativa) = código ICMS do
bloco e do regime usados (CST para CRT 2/3, CSOSN para CRT 1/4) + `PisCst` + `CofinsCst`. IBS/CBS é
opcional: sem `IbsCbsCst`, a linha sai sem IBS/CBS.

### 5.3 `BRANCHS` (dois modos)

| Coluna | Tipo | Tela |
|---|---|---|
| `TaxRegime` | INT NULL — enum `TaxRegime { SimplesNacional = 1, SimplesNacionalExcess = 2, Normal = 3, Mei = 4 }` | Regime tributário (CRT) |
| `IssuesNfe` | BIT NOT NULL DEFAULT 0 | Emite NF-e pelo Siagro |

- CRT 1 e 4 ⇒ CSOSN; CRT 2 e 3 ⇒ CST.
- Com `IssuesNfe` ligada, `TaxRegime` e `StateCode` são obrigatórios — validado no `BranchService` ao
  salvar, **só em STANDALONE**, enquanto a chave estiver ligada (não só na virada, para ninguém apagar o
  CRT depois). Desligar é sempre permitido.
- Campos ocultos na tela de filial em SAPB1.

### 5.4 `ITEMS` (só STANDALONE)

| Coluna | Tipo | Tela |
|---|---|---|
| `GoodsOrigin` | TINYINT NULL (0–8, tabela de origem da SEFAZ) | Origem da mercadoria |
| `Ncm` | VARCHAR(8) | NCM (8 dígitos) |

Opcionais no cadastro; **os dois** exigidos no documento quando a regra está ativa (§7.3). `ItemModel` ganha as duas
propriedades; o `ItemService` SAP deixa nulas. Ocultas na tela de produtos em SAPB1.

### 5.5 `IBS_CBS_RATES` (nova)

| Coluna | Tipo |
|---|---|
| `Key` | INT IDENTITY PK |
| `StartDate` | DATE NOT NULL (`DateOnly` ⇒ `Edm.Date`, sem fuso), índice único |
| `CbsRate`, `IbsStateRate`, `IbsMunicipalRate` | DECIMAL(7,4) NOT NULL |

- Vigente para uma data D = linha com o **maior `StartDate` ≤ D**.
- Semente: `2026-01-01` → CBS 0,9 / IBS estadual 0,1 / IBS municipal 0.
- Tela "Alíquotas IBS/CBS" (lista + inclusão/edição/exclusão), aberta por botão na lista de naturezas,
  oculto em SAPB1 como o Incluir/Deletar. O controller recusa chamadas fora do STANDALONE
  (`BadRequest` com mensagem pt-BR), seguindo o padrão de `UsersSyncFromSapController`.

### 5.6 `SALES_INVOICES_ITEMS` — a fotografia do cálculo

Colunas existentes reaproveitadas: `Cfop`, `Ncm`, `CstIcms` (guarda CST **ou** CSOSN), `IcmsBase`,
`IcmsRate`, `IcmsValue`, `CstPis`, `PisBase`, `PisRate`, `PisValue`, `CstCofins`, `CofinsBase`,
`CofinsRate`, `CofinsValue`.

Alteração: `IcmsRate`, `PisRate`, `CofinsRate` de `DECIMAL(5,4)` para **`DECIMAL(7,4)`** (D11). Nenhum
dado existente é convertido; em SAPB1 o único efeito é aceitar números maiores (hoje "18" estoura).

Colunas novas:

| Coluna | Tipo |
|---|---|
| `GoodsOrigin` | TINYINT NULL |
| `IcmsBaseReduction`, `IcmsDeferral` | DECIMAL(7,4) DEFAULT 0 |
| `IcmsOperationValue`, `IcmsDeferredValue` | DECIMAL(18,2) DEFAULT 0 |
| `IcmsBenefitCode` | VARCHAR(10) |
| `IbsCbsCst` | VARCHAR(3) |
| `IbsCbsClassCode` | VARCHAR(6) |
| `IbsCbsBase` | DECIMAL(18,2) DEFAULT 0 |
| `CbsRate`, `CbsRateReduction`, `IbsStateRate`, `IbsMunicipalRate`, `IbsRateReduction` | DECIMAL(7,4) DEFAULT 0 |
| `CbsValue`, `IbsStateValue`, `IbsMunicipalValue` | DECIMAL(18,2) DEFAULT 0 |
| `MovesFiscalInventory`, `CreatesFinancialDocument` | BIT NOT NULL DEFAULT 0 — cópia das flags da natureza (D14), para os consumidores futuros lerem o que valia na emissão |

- `TotalTaxes` (NotMapped) continua `IcmsValue + PisValue + CofinsValue`.
- Novo NotMapped `TotalIbsCbs = CbsValue + IbsStateValue + IbsMunicipalValue`, e no cabeçalho
  `TotalInvoiceIbsCbs`. Lembrar: `[NotMapped]` some do EDM e precisa de `AddProperty` em
  `ODataConfigurations` (como `TotalInvoiceTaxes`).
- O sub-projeto 2 monta o XML **só** desta fotografia, sem recalcular.

## 6. O cálculo

Duas peças:

- **`TaxCalculator`** — função pura: `Calculate(TaxCalculationInput) → TaxCalculationResult`. Sem banco,
  sem configuração. É onde moram todos os testes de regra.
- **`SalesInvoicesTaxApplyService`** — orquestração: pergunta ao gate, carrega natureza
  (`IUsage`), filial, UF do cliente (mesma resolução de `SalesInvoicesCfopResolveService`, que prefere o
  endereço de faturamento e ignora endereço sem UF), produto (`ITEMS`), alíquota IBS/CBS vigente na
  `InvoiceDate`; aplica as guardas (§7); chama o `TaxCalculator`; grava a fotografia na linha.

### 6.1 Passos, em ordem fixa

1. **Interno × interestadual** — UF da filial × UF do cliente. Decide o CFOP (`CfopOutgoingInState`/
   `OutState`, já existente) e o bloco ICMS ("dentro"/"fora").
2. **Código ICMS** — CRT 1/4 ⇒ CSOSN do bloco; CRT 2/3 ⇒ CST do bloco.
3. **Alíquota ICMS** — bloco "dentro": `IcmsInStateRate`. Bloco "fora", primeira regra que casar:
   - origem do produto ∈ {1, 2, 3, 8} ⇒ **4%** (Res. Senado 13/2012);
   - UF da filial ∈ {PR, SC, RS, SP, RJ, MG} **e** UF do cliente ∈ {AC, AM, AP, PA, RO, RR, TO, AL, BA,
     CE, MA, PB, PE, PI, RN, SE, DF, GO, MS, MT, ES} ⇒ **7%** (Res. Senado 22/89);
   - senão ⇒ **12%**.
4. **ICMS** — `valor = SalesInvoiceItem.Total` (a regra atual da linha: `Quantity × UnitPrice` arredondado
   a 2 casas, como o total que o documento já mostra — é o `vProd`):
   - CST 00: base = valor; ICMS = base × alíq.
   - CST 20, 90 e CSOSN 900: base = valor × (1 − red); ICMS = base × alíq.
   - CST 51: base = valor × (1 − red); ICMS da operação = base × alíq; diferido = operação × dif;
     ICMS = operação − diferido.
   - CST 40, 41, 50 e CSOSN 102, 103, 300, 400: base, alíquota e valor = 0.
5. **PIS e COFINS** — base = valor − (ICMS, se `ExcludeIcmsFromPisCofinsBase`); valor = base × alíq.
   CST 04–09: base, alíquota e valor = 0.
6. **IBS e CBS** — só se a natureza tiver `IbsCbsCst`:
   - base = valor − ICMS − PIS − COFINS (fórmula que a SEFAZ valida na NT 2025.002; sem frete,
     seguro, desconto e IS, que a linha não tem);
   - CBS = base × `CbsRate` × (1 − red CBS); IBS estadual = base × `IbsStateRate` × (1 − red IBS);
     IBS municipal = base × `IbsMunicipalRate` × (1 − red IBS);
   - CST 400/410: base, alíquotas e valores = 0;
   - em 2026 o IBS/CBS é informativo: **não soma** no total do documento nem em `TotalTaxes`.
7. **Arredondamento** — cada base e cada valor a 2 casas, `MidpointRounding.AwayFromZero`, **antes**
   de entrar no passo seguinte (PIS usa o ICMS já arredondado; a base do IBS/CBS usa ICMS, PIS e COFINS
   arredondados), para fechar com a validação da SEFAZ.

### 6.2 Exemplo de referência (vira teste)

Venda SP → BA, origem 0, 30.000 kg × R$ 2,00 = **R$ 60.000,00**. Natureza: bloco fora CST 00; PIS 01
1,65%; COFINS 01 7,6%; Tema 69 marcado; IBS/CBS CST 000 sem redução; alíquotas de 2026.

| Tributo | Base | Alíquota | Valor |
|---|---|---|---|
| ICMS | 60.000,00 | 7% (automática) | 4.200,00 |
| PIS | 55.800,00 | 1,65% | 920,70 |
| COFINS | 55.800,00 | 7,6% | 4.240,80 |
| CBS | 50.638,50 | 0,9% | 455,75 |
| IBS estadual | 50.638,50 | 0,1% | 50,64 |
| IBS municipal | 50.638,50 | 0% | 0,00 |

Mesma venda SP → SP, bloco dentro CST 51, alíquota 18%, diferimento 100%: base 60.000,00, ICMS da
operação 10.800,00, diferido 10.800,00, ICMS 0,00.

## 7. Integração, trava e guardas

Tudo abaixo só vale com a regra ativa **e** `InvoiceType == Normal`. Fora disso, cada caminho é o de hoje.

### 7.1 Quando calcula

- `SalesInvoicesCreateService` — após a resolução de natureza e CFOP, para cada linha. Cobre todos os
  caminhos que criam documento Normal por ele: avulso, faturamento de romaneio
  (`ShipmentBillingCreateSalesInvoiceService`) e demais chamadores. Os que criam Devolução
  (`SalesInvoicesReturnService`, `ShipmentLoadsRefuseService`) passam reto.
- `SalesInvoicesItemsCreateService` e `SalesInvoicesItemsUpdateService` — a linha incluída/alterada.
- `SalesInvoicesUpdateService` — se mudar `InvoiceDate`, `CardCode` ou `BranchCode`, recalcula
  **todas** as linhas.
- Só com o documento **Pendente**. `SalesInvoicesConfirmService` não recalcula: confirma a fotografia
  que o usuário viu.
- Respeitar `CommitMode.Deferred` dos chamadores: o cálculo grava no mesmo contexto, sem `SaveChanges`
  próprio.

### 7.2 Trava

- Travados: `Cfop`, `Ncm`, `GoodsOrigin`, todos os campos de tributo da §5.6 e as duas flags de D14.
- Editáveis: natureza (`UsageCode`), `Quantity`, `UnitPrice`, `CostCenterCode`, `LedgerAccountCode`.
- No servidor, o update **sobrescreve** com o cálculo os campos travados que vierem no corpo (não
  recusa): o `SetValues` genérico de `SalesInvoicesItemsUpdateService` devolve a entidade inteira com os
  mesmos valores, e recusar quebraria a tela.
- O log de alterações continua registrando o que o usuário digitou, não o que o cálculo derivou.

### 7.3 Guardas (mensagens de negócio em pt-BR, citando item e natureza)

1. Linha sem natureza cai na padrão (comportamento atual de `SalesInvoicesUsageGuardService`); sem
   padrão ⇒ **erro** (nunca linha sem imposto em silêncio).
2. Natureza precisa ser `Outgoing` e ter **tributação configurada** (§5.2) para o bloco e o regime usados.
3. Filial com `TaxRegime` e `StateCode`; cliente com UF no endereço de faturamento.
4. Produto com `Ncm` e `GoodsOrigin` — os dois sempre, porque a NF-e exige `NCM` e `orig` em todo item
   (a origem também decide os 4% da operação interestadual).
5. Alíquota IBS/CBS vigente na `InvoiceDate`, se a natureza tiver `IbsCbsCst`.
6. **Faturamento de romaneio fica estrito**: o `catch (DefaultException) when (fromShipmentBilling)` de
   `SalesInvoicesCreateService` passa a valer só com a regra **inativa**. Com a regra inativa (Yokotobi,
   MH Agro), a tolerância de `SalesInvoicesShipmentBillingFiscalToleranceTests` segue idêntica.

### 7.4 Fora deste sub-projeto (explícito)

- Documento `InvoiceType == Return`: não calcula e não passa pelas guardas novas (NF-e de devolução é
  sub-projeto 3; a cópia de devolução nem copia `UsageCode` hoje).
- "Informar Nota Fiscal" (`SalesInvoicesSetDocumentNumberService`): intocado.

## 8. Telas (frontend)

| Tela | Mudança |
|---|---|
| Naturezas — lista (`view/usages/Main`) | Colunas Tipo e CST/CSOSN dentro/fora (STANDALONE). Botão **"Alíquotas IBS/CBS"** visível só fora do SAPB1. |
| Naturezas — formulário (`view/usages/fragments/Form`) | Blocos: Dados da natureza (+ Tipo, Texto na nota, Informações complementares, caixas **"Movimenta estoque"** e **"Gera financeiro"**), Efeito no contrato (só Saída), **ICMS dentro do estado**, **ICMS fora do estado** (sem alíquota, com aviso "alíquota automática 7%/12%/4%"), **PIS/COFINS**, **IBS/CBS**. CFOP dentro/fora gravam nas colunas do tipo. Blocos novos ocultos em SAPB1 (`ui>/identityEditable` já existe). Selects de CST/CSOSN com listas fixas. |
| Alíquotas IBS/CBS (nova, rota `usages/ibs-cbs-rates`) | Lista + diálogo de inclusão/edição/exclusão. |
| Filial (`view/branchs`) | Regime tributário (CRT) e "Emite NF-e pelo Siagro" — ocultos em SAPB1. |
| Produto (`view/produtos`) | Origem da mercadoria e NCM — ocultos em SAPB1. |
| Documento de saída — diálogo fiscal do item (`ItemFiscalDialog`) | Regra ativa: tributos somente leitura + campos novos (origem, redução, ICMS da operação, diferimento, diferido, cBenef, bloco IBS/CBS); centro de custo e conta contábil editáveis. Regra inativa: **idêntico ao de hoje**, sem os campos novos. |
| Documento de saída — cabeçalho | Total IBS/CBS separado, só com a regra ativa. |
| Documento de saída — value help de natureza | Só `Outgoing` em STANDALONE. |
| Parceiro — inclusão com endereço | Passa a gravar o endereço (bug D12). |

A tela sabe se a regra está ativa chamando `TaxCalculationIsActive(BranchCode)` ao abrir o documento.
Os impostos aparecem depois de gravar (o cálculo é do servidor); não há prévia ao vivo.

Armadilhas conhecidas a respeitar: `$select` explícito no binding da lista de naturezas (o
auto-gerado só traz o que o template usa); um campo por propriedade (binding composto quebra no
OData v4); decimal editável com `Edm.Double`/`targetType` adequado; booleano sem `targetType` vira
"Não" truthy.

## 9. Bugs de endereço do parceiro STANDALONE (D12)

1. `BusinessPartnerService.CreateAsync` ignora `Addresses` — o deep insert da tela cria o parceiro sem
   endereço. Corrigir para gravar os endereços aninhados.
2. `POST BusinessPartners('X')/Addresses` grava e responde 500 (`Created(model)` em rota de atributo
   sem entity set ⇒ `InvalidCastException: EdmUnknownEntitySet`). Trocar por `Ok(model)` (ou declarar a
   navegação), e conferir o GET da mesma rota, que derruba a conexão pelo mesmo motivo.

Os dois só rodam em STANDALONE (`IBusinessPartnerAddressService` só é registrado nesse modo).

## 10. Migrations

Todas no contexto da aplicação (`SiagroB1.Migrations/AppContext`); **nenhuma** no comum (sem menu).
Aditivas, anuláveis ou com default — Yokotobi e MH Agro recebem colunas que não usam.

1. `AddUsageTaxation` — colunas da §5.1 (`Direction` com default 1).
2. `AddBranchTaxRegimeAndIssuesNfe` — §5.3.
3. `AddItemGoodsOriginAndNcm` — §5.4.
4. `CreateIbsCbsRates` — §5.5 + semente de 2026 (`IF NOT EXISTS ... INSERT`).
5. `AddSalesInvoiceItemTaxSnapshot` — §5.6 + alargamento das três alíquotas.

O Web recusa subir com migration pendente: cada ambiente aplica no deploy. Aplicar com
`ASPNETCORE_ENVIRONMENT` explícito (o profile `db-migration` aponta para produção).

## 11. Testes (xUnit + EF InMemory, `SiagroB1.Application.Tests`)

- **`TaxCalculator`**: cada CST (00, 20, 40, 41, 50, 51, 90) e CSOSN (102, 103, 300, 400, 900);
  matriz 7/12/4 incluindo importado (origens 1, 2, 3, 8 ⇒ 4%; 6 e 7 ⇒ 7/12); PIS/COFINS com e sem
  Tema 69; CST 04–09 zerados; IBS/CBS (fórmula da base, reduções, 400/410 zerados, sem CST ⇒ sem
  IBS/CBS); cadeia de arredondamento; o exemplo da §6.2 valor por valor.
- **Vigência IBS/CBS**: data antes da primeira vigência ⇒ guarda; data entre vigências ⇒ a anterior.
- **`TaxCalculationGate`**: SAPB1 + chave ligada ⇒ inativa; STANDALONE + chave desligada ⇒ inativa;
  STANDALONE + chave ligada ⇒ ativa; filial inexistente ⇒ inativa.
- **Isolamento** (contraprovas): regra inativa ⇒ valores digitados preservados no create/update, sem
  guarda nova; `SalesInvoicesShipmentBillingFiscalToleranceTests` passa **sem alteração**.
- **Regra ativa**: trava sobrescreve campos enviados; mudança de data/cliente/filial no cabeçalho
  recalcula todas as linhas; faturamento de romaneio estrito; Devolução passa reto; confirmação não
  recalcula; cada guarda da §7.3 com sua mensagem; as flags de D14 copiadas da natureza para a linha
  (e a confirmação **não** gera título nem movimento algum por causa delas).
- **Natureza**: vazia é válida; cada regra de coerência da §5.2; `Incoming` não pode ser padrão.
- **Filial**: ligar a chave exige CRT e UF (só STANDALONE); desligar sempre passa.
- **Parceiro**: create com endereço grava; POST avulso de endereço responde sem 500.

## 12. Verificação no navegador (pelo caminho do usuário, a partir da home)

1. **STANDALONE local, chave ligada** — filial com CRT 3 e UF SP; produto com origem 0 e NCM; naturezas
   "Venda interna" (CST 51, 18%, diferimento 100%) e "Venda interestadual" (CST 00); cliente BA e
   cliente SP cadastrados **pela tela** com endereço. Documento avulso SP → BA conferindo a §6.2 no
   diálogo fiscal; SP → SP com diferimento; faturamento de romaneio; tentativa de editar um imposto
   (travado); troca da data de emissão recalculando.
2. **Mesma base, chave desligada** — documento volta a ser digitado como hoje.
3. **SAPB1 (Yokotobi)** — telas de natureza, filial, produto e documento de saída sem nenhum campo
   novo; diálogo fiscal idêntico. Conferível mesmo com o SAP fora do ar.

## 13. Riscos e armadilhas

- **Duas paredes**: ao testar uma guarda, percorrer o caminho inteiro até a confirmação — corrigir a
  mensagem reportada costuma revelar a seguinte (lição do faturamento de romaneio em SAPB1).
- **IBS/CBS a partir de 2027** pode passar a integrar a base do ICMS (tendência discutida na transição).
  Fora deste escopo; a tabela por vigência não resolve isso — será regra nova quando vier.
- **CST/cClassTrib** digitados errados aparecem como rejeição da SEFAZ só no sub-projeto 2; a tela só
  valida formato e coerência.
- **Alíquota interna** é uma por natureza: uma filial em outra UF com alíquota interna diferente exige
  natureza própria (aceito — CEAGUI tem uma UF).
