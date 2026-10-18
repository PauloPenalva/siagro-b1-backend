# Tributos legados no Protheus 12.2310 — retenções, apuração, IRPJ/CSLL e REINF

O que se aprendeu lendo as fontes em `C:/Projetos/fontes-protheus-12.2310` (só leitura, pesquisa de
2026-10-18). Use como **segunda referência independente** do motor do Siagro: o Protheus é um ERP
maduro, auditado por milhares de contadores, então divergência entre os dois merece investigação.

Convenções:
- **[lei]** é a regra legal.
- **[Protheus]** é como o Protheus parametriza essa regra.

Caminhos relativos à raiz do dump. Os números de linha são aproximados (±2). Os padrões entre
aspas nos `SuperGetMv` são os defaults do código; os valores reais ficam no SX6, que não está no dump.

## Índice
1. Retenções na fonte (PCC, IRRF, INSS, ISS)
2. Funrural / GILRAT / SENAR e o título a pagar
3. Apuração de ICMS e IPI
4. Apuração de PIS/COFINS (inclusive crédito presumido agro)
5. IRPJ/CSLL (TAF)
6. EFD-REINF (R-2055, R-2050, R-2010/2020, R-4010/4020)
7. Lacunas do Siagro frente a isto

---

## 1. Retenções na fonte

São duas camadas. O **motor fiscal** calcula o tributo por item da NF
(`Livros Fiscais/matxfis.prx` + `FISX*.prw`). O **financeiro** aplica mínimo, acumulação, vencimento
e gera os títulos TX (`Adm/FINA050.PRX`, `Adm/FINXIMP.PRW`, `Adm/finxfin.prx`). A ponte entre as
duas é `A103AtuSE2` em `Gestão de Suprimentos/Gestão de Compras/mata103.prw`.

### PIS/COFINS/CSLL retidos — 4,65% (0,65 + 3 + 1)

**Regra legal:**
- Base legal: Lei 10.833, arts. 30/31.
- Aplica-se quando uma PJ paga serviço a outra PJ.
- Desde a Lei 13.137/2015 (22/06/2015), a retenção é dispensada quando o valor retido é ≤ R$ 10,00.
- Antes disso, valia o limite de R$ 5.000 de pagamentos por mês ao mesmo fornecedor.
- O DARF vence no dia 20 do mês seguinte, antecipado para o último dia útil anterior.

**Como o Protheus aplica:**
- **Quando retém:** a natureza tem `ED_CALCPIS/COF/CSL="S"` e o fornecedor tem `A2_RECPIS...="2"`.
  Fornecedor **PF não sofre** PCC: `AllTrim(cTipoPes) <> "F"` (`FINXIMP.PRW:6450`).
- **Base:** valor da duplicata. Soma o desconto se `MV_PISBRU`, tira o IPI se `F4_IPIPC="2"` e tira
  o ICMS-ST (`FISXPIS.prw:606`).
- **Mínimo atual** (`newMinPcc`, `FINXIMP.PRW:6243`, a partir de `dRef >= 22/06/2015`):
  `If (nCsl + nCof + nPis) <= MV_VL13137 (10) → zera os três` (`:6776-6781`).
  Antes do teste, soma o PCC **já baixado no mesmo dia** para o mesmo fornecedor (`SumDay`, `:7849`).
  **A acumulação é diária, não mensal.**
- **Momento da retenção:** `MV_BX10925` "1" = na baixa (pagamento), "2" = na emissão (default).
  Na baixa parcial, a base é proporcional ao valor pago (`:6573-6583`).
- **Teto:** a retenção nunca passa do saldo do título (`verVrAR`). O excedente vira NDF ou vai para o
  próximo título, conforme `MV_NCCRET`.
- **Arredondamento:** por default **trunca** (`NoRound(x,2)`). `MV_RNDPIS/COF/CSL` liga o
  arredondamento comum (`FINXIMP.PRW:6686-6691`).

### IRRF

**Regra legal:**
- PJ prestadora de serviço: 1,5% sobre o bruto (1% para limpeza, vigilância e similares).
- PF: tabela progressiva.
- Dispensa quando o IRRF é ≤ R$ 10,00.
- Vencimento no dia 20 do mês seguinte.

**Como o Protheus aplica:**
- **Alíquota:** `ED_PERCIRF`, ou `MV_ALIQIRF` se a natureza não tiver (`FINA050.PRX:17454-17461`).
- **Redução de base:** `ED_BASEIRF` ou `B1_REDIRRF` (percentual da base).
- **Acumulação:**
  - **PF:** soma os títulos do fornecedor **no mês**.
  - **PJ:** soma os títulos **no mesmo dia** com o mesmo código de retenção (`FCalcIr`, `:16881`, `:17153-17207`).
  - O IR já retido é descontado.
- **Mínimo:** só se o fornecedor tiver `A2_MINIRF="2"`. Zera quando `nValor <= MV_VLRETIR (10)` (`:17513-17515`).
- **PF:** usa a tabela progressiva do arquivo `SIGAADV.IRF`, com dedução por dependente e o
  desconto simplificado da MP 1.171/23 (`MV_FVL1171`, 528).

### INSS retido (cessão de mão de obra)

**Regra legal:** 11% sobre o serviço (Lei 8.212, art. 31). Para contribuinte individual PF, 11% até o teto.

**Como o Protheus aplica:**
- **Fórmula:** `NoRound(base * ED_PERCINS/100, 2)`. Por default **trunca**; `MV_RNDINS` muda para
  arredondar (`FINA050.PRX:15973-16125`).
- **Mínimo:** para PJ, `MV_VLRETIN`. Para PF, `MV_MININSS` e teto `MV_LIMINSS`, descontado do
  INSS já retido no mês.
- **Funrural:** o teto é desligado (`If lFunRural; nLimInss := 0`, `:15954`).
- **Dedução do título:** `ED_DEDINSS="2"` faz o INSS **não** ser descontado do título.

### ISS retido

**Quando retém:** TES com `F4_ISS="S"`, item com código de serviço e fornecedor que não recolhe o
próprio ISS (`A2_RECISS`). Com `MV_ISSXMUN`, as regras por município vêm da tabela `CE1`.

**Parâmetros:**
- Momento: `MV_MRETISS` "1" = emissão, "2" = baixa.
- Mínimo: por valor (`MV_VRETISS`, default 25,00) ou por base, conforme `MV_FMINISS`.
- Vencimento: `MV_VENCISS`/`MV_DIAISS` (default dia 10).

### Ordem dos descontos no título a pagar (`A103AtuSE2`)

Parcela → − Funrural/GILRAT (**só na 1ª parcela**, `mata103.prw:12333`) → − IRRF (na emissão) →
− ISS (na emissão) → − INSS (salvo `ED_DEDINSS="2"`) → − SEST → − Fundesa → − PCC (na emissão) →
− fundos estaduais (FETHAB, FABOV, FACS, IMA, FAMAD).

Cada retenção vira um título **TX** próprio:
- Fornecedor `MV_UNIAO` para IRRF e PCC, `MV_FORINSS` para INSS e Funrural, `MV_MUNIC` para ISS.
- O título é ligado ao título pai por `E2_TITPAI`.
- O vencimento vem de `F050VImp` (`FINA050.PRX:15152-15478`).

## 2. Funrural, GILRAT e SENAR

**Regra legal:**
- Base legal: Lei 8.212, arts. 25 e 30, IV.
- A PJ que compra de produtor rural **PF** fica sub-rogada e retém 1,2% de INSS + 0,1% de RAT + 0,2% de SENAR.
- O produtor **PJ** recolhe sobre as próprias vendas. Não há retenção pelo comprador.
- O produtor pode optar por contribuir sobre a folha (art. 25, §13).

**Como o Protheus aplica** (`Livros Fiscais/FISXFUNRUR.PRW`, `FISXSENAR.prw`, `FISXINSS.prw`):

- **Quando calcula:** produto com `B1_CONTSOC="S"` e TES com `F4_CONTSOC="1"`.
- **Alíquota:** `MV_CONTSOC` no formato "F/L/J", indexado pelo tipo do fornecedor `A2_TIPORUR`
  (F = pessoa física, L = segurado especial).
- **Fornecedor PJ não sofre retenção na entrada.** O comentário está no código (`FISXFUNRUR.PRW:34-35`).
- **Separação das parcelas no Protheus atual** (tela "Deduções de Impostos", `mata103fin.prw:383-401`):

  | Parcela | Origem | Campo |
  |---|---|---|
  | INSS | natureza | `D1_VALINS` |
  | GILRAT | `MV_CONTSOC` | `D1_VALFUN` (o campo ainda se chama "Funrural") |
  | SENAR | TES `F4_ALSENAR` | `D1_VLSENAR` |

  Com `MV_FUNRURA="1"`, as três usam a mesma base.
- **Base**, conforme `F4_BSRURAL`:
  - 1 = mercadoria − desconto;
  - 2 = + frete, despesas e seguro;
  - 3 = + IPI + ICMS-ST.
  - `F4_DICMFUN` desconta o ICMS da base.
- **Decisão judicial (liminar):** a alíquota é sobrescrita pelo ponto de entrada `MAFISRUR`. Na
  Gestão Agrícola, quem tem liminar paga só o SENAR (`Gestao Agricola/Originacao/OGA430F.PRW:1583-1593`).
- **SENAR no título:** só é deduzido quando a empresa é PJ e o fornecedor é PF
  (`dedSenarDup`, `FISXSENAR.prw:111-130`). Na devolução, a dedução é revertida.
- **Título TX do Funrural:** natureza `MV_CSS`, fornecedor `MV_FORINSS`.
  - Vencimento no dia `MV_DIAFUN` do mês seguinte (`finxfin.prx:8969-9069`).
  - Dia não útil **adia** para o próximo útil quando o fornecedor é PF rural; nas demais retenções antecipa (`FINA050.PRX:15213-15290`).
- **Na venda do produtor:** se a natureza tem `ED_RECFUN="1"` e o cliente é PJ, gera **receber**
  (o cliente paga). Caso contrário, gera pagar à União.
- **Gestão Agrícola, valor líquido da fixação:** subtrai INSS, SENAR, GILRAT e os fundos estaduais
  FETHAB/FACS (MT) e Fundersul (MS).

## 3. Apuração de ICMS e IPI

### Livro fiscal

- **SFT:** uma linha por item da NF. Colunas:
  - valor contábil;
  - base;
  - ICMS;
  - **isentas**;
  - **outras**;
  - ICMS diferido.
- **SF3:** resumo por NF. Agrupa por CFOP + alíquota + livro (`IMPXFIS.prw:11718-11738`).
- **Diferimento (CST 51)** (`FISXICMS.prw:160-205`):
  - `IT_ICMSDIF = VALICM × PICMDIF/100`, e o ICMS da nota fica `VALICM -= ICMSDIF`.
  - No livro, a parte diferida vai para a coluna **Outras** (`IT_COLVDIF='1'`) ou **Isentas** (`'2'`).
  - Base e ICMS ficam em 0 (`IMPXFIS.prw:7168-7181`).
  - **Diferido é só informativo: nunca vira débito** (quadro "ICMS Diferido", `mata953.PRX:9045`).
- **TES `F4_LFICM`:**
  - `T` = tributado (colunas base e ICMS);
  - `I` = Isentas;
  - `O` = Outras;
  - `N`/`Z` = não escritura ICMS.

### Apuração de ICMS — MATA953 (formato do registro E110 do SPED)

| Linha | Conteúdo |
|---|---|
| 001 | Saídas com débito |
| 002 | Outros débitos |
| 003 | Estorno de créditos |
| 004 | Subtotal dos débitos |
| 005 | Entradas com crédito |
| 006 | Outros créditos |
| 007 | Estorno de débitos |
| 008 | Subtotal dos créditos |
| 009 | Saldo credor anterior |
| 010 | Total |
| 011 | Saldo devedor |
| 012 | Deduções |
| 013 | Imposto a recolher |
| 014 | Saldo credor |

Fórmula (`a953Saldo`, `mata953.PRX:6898-6903`):
```
004 = 001+002+003   008 = 005+006+007   010 = 008+009
011 = max(004 − 010, 0)    013 = max(011 − 012, 0)    014 = max(010 − 004 + 012, 0)
```

- **Valores do livro:** `ResumeF3` soma o SF3 por CFOP. CFOPs 1/2/3xxx entram na linha 005; os de saída, na 001.
- **Ajustes** (tabela 5.1.1 do SPED, cadastro CDO):
  - A 3ª posição do código indica o tributo: 0 = ICMS, 1 = ST, 2 = DIFAL, 3 = FCP.
  - A 4ª posição precisa casar com a linha: 002→0, 003→1, 006→2, 007→3, 012→4, débitos especiais→5.
- **Ajustes por documento:** tabela CDA (C197 do SPED). Entram em "outros débitos/créditos".
- **Saldo credor anterior:** vem de um **arquivo texto** da apuração anterior (`LoadAnt`). É frágil.
  Para o DIFAL, o saldo por UF fica em tabela (F0I), que é o desenho a copiar.
- **ICMS-ST:** apuração separada por UF.
- **DIFAL/FCP:** apuração por UF (`ApurDifal`, `mata953.PRX:12457-12760`), com a mesma estrutura de débitos e créditos.
- **Guias:** título TX, natureza `MV_ICMS`, GNRE na tabela SF6. Vencimento default = dia seguinte ao fim do período, editável.

### Apuração de IPI — MATA952

| Grupo | Linhas |
|---|---|
| Créditos | 001–008: entradas MN e ME, exportação, estorno de débitos, outros, saldo anterior |
| Débitos | 009–013: saídas MN, estorno de créditos, ressarcimento, outros |
| Resultado | 016 = saldo devedor, 017 = saldo credor |

Vencimento em `MV_IPIVENC`. Código do DARF em `E2_CODRET`.

## 4. Apuração de PIS/COFINS — FISA001 + FISX001 (EFD-Contribuições)

**Regra legal:**
- Cumulativo (presumido): 0,65% / 3%, sem créditos.
- Não cumulativo (real): 1,65% / 7,6%, com créditos.

**Como o Protheus aplica:**
- **Alíquotas:** nunca fixas no código. Vêm do item: exceção fiscal → `B1_PPIS` → `MV_TXPIS`.
- **Regime:** definido por TES, produto ou cliente (`MV_M996TPR`).
- **Débitos** (`CodCon`, `FISX001.PRW:1427`):
  - CST 01 → código 01 (não cumulativo) ou 51 (cumulativo).
  - CST 02 → 02/52.
  - CSTs 04/06/07/08/**09**/49 → receita não tributada (M400/M800). **Suspensão (CST 09) nunca é débito.**
- **Créditos:** vêm das entradas com CST 50–66 (`A001SFTEnt`).
  - O código do crédito é grupo + tipo (`AgrpCred`/`CodCred`, `FISX001.PRW:2015-2140`).
  - Grupo: 1 = mercado interno tributado, 2 = não tributado, 3 = exportação.
  - Tipo: 01 = alíquota básica, 02 = diferenciada, 06 = **presumido agroindústria**, 07 = outros presumidos, 08 = importação.
  - CSTs 53–56/63–66 rateiam o crédito pela receita bruta de cada grupo (`CalcBAseRC`).
- **Crédito presumido agro** (Lei 10.925, art. 8º):
  - **[lei]** Aquisições de PF ou cerealista × (60%, 35% ou 50%) × 9,25%. Com 60%: PIS 0,99% e COFINS 4,56%.
  - **[Protheus] Opção 1, por documento:** TES de compra com CST 60–66 e alíquota reduzida no produto (0,99 / 4,56). O crédito sai no código x06.
  - **[Protheus] Opção 2, ajuste do período** (`A001AjuPresumido`, `FISA001.PRW:3141-3330`):
    - entradas CST 64/66 com NCM em `MV_SPCXCP1`;
    - receitas com NCM em `MV_SPCXCP3`;
    - alíquotas em `MV_SPCXCP2`;
    - rateio entre mercado interno e exportação.
  - **Não há 60/35/50% fixos no código:** tudo é parâmetro.
  - **Estorno de crédito** (ex.: venda com suspensão): percentuais digitados por período (FISA213/F3V), aplicados em `EstCrdAgro`.
- **Consolidação** (`FISX001.PRW:5150-5160`):
  ```
  a recolher NC = contribuição NC − crédito anterior − crédito do período − retenções − outras deduções
  a recolher C  = contribuição C − retenções − outras deduções
  ```
  O crédito que sobra vai para o período seguinte (CCY, registros 1100/1500).
- **Guias** (CL3 + `FISA002`):
  - Códigos de receita em `MV_CDRCEC`: 6912/5856 no não cumulativo, 8109/2172 no cumulativo.
  - Fornecedor `MV_UNIAO`.

## 5. IRPJ/CSLL — só no TAF, motor "Evento Tributário"

**Regra legal:**

| | Presumido | Real |
|---|---|---|
| Base do IRPJ | receita × 8% (comércio) ou 32% (serviços) | lucro contábil + adições − exclusões − prejuízo (compensação limitada a 30%) |
| Base da CSLL | receita × 12% ou 32% | mesma base ajustada do LACS |
| Alíquota | IRPJ 15% + adicional de 10% sobre o que passar de R$ 20.000/mês; CSLL 9% | idem |

**Como o Protheus aplica:**
- **Nenhum número legal é fixo no código.** O cadastro de tributos (`TAF/Cadastros Fiscais/TAFA430.prw`, tabela T0J) guarda:
  - alíquota;
  - adicional;
  - parcela isenta mensal;
  - até 4 percentuais de presunção.
- **Formas de tributação** (T0K): Real, estimativa por balanço, estimativa por receita, **atividade
  rural**, presumido, arbitrado etc. Períodos anual, mensal ou trimestral.
- **Evento tributário** (`TAFA433`): itens agrupados em 18 grupos (receita por alíquota 1–4,
  adições, exclusões, compensação de prejuízo, deduções...). A origem de cada item é conta contábil,
  LALUR parte B, outro evento ou lançamento manual.
- **Cálculo** (`TAF/Apurações/TAFA444.prw`):
  - **Presumido:** `Σ receita_n × presunção_n + demais receitas` (`VlrLucroEs`, ~2740).
  - **Adicional:** `(lucro − parcela_isenta × nº de meses) × 10%` (`VlrIsento` 3078, `VlrAdiciIR` 2901).
    Serve para períodos mensais, trimestrais e anuais sem mudar a fórmula.
  - **A pagar:** `base × alíquota + adicional − deduções − compensações − devido/pago por estimativa
    em períodos anteriores`, nunca negativo (`CalcVlrAPg`, 3100).
  - **Limite de 30%:** é parâmetro do item (`T0O_PERDED`), aplicado em `AplDedComp` e `GetLimite`.
- **Atividade rural:** é um evento separado. O prejuízo entre a atividade geral e a rural compensa
  100% no mesmo período (`ComPjAtual`, 3706).
  ⚠️ Provável **bug do Protheus** em `TAFA444.prw:3739`: a comparação usa o valor rural contra ele
  mesmo. Não copie.
- **DARF** (`TAFA444DA`): gera o registro C0R e, opcionalmente, o título SE2 (fornecedor `MV_UNIAO`).
  - IRPJ: 2089 presumido, 2362 estimativa, 3373 trimestral.
  - CSLL: 2372 presumido, 2484 estimativa, 6012 trimestral.
- **LALUR/LACS:** parte B em `TAFA436`. Blocos da ECF extraídos por `TAF/Obrigações Fiscais/Federal/ECF/TAFECF*.PRW`.

## 6. EFD-REINF (TAF)

**Fluxo:** o extrator (`Livros Fiscais/Extrator/ExtFisxTaf.prw`, `ExtT013.PRW`) leva a NF para as
tabelas do TAF:
- C20 = NF;
- C30 = item;
- C35 = tributo do item.

Códigos de tributo na C35:

| Código | Tributo | Origem no Protheus |
|---|---|---|
| 000013 | Contribuição previdenciária (CP) | `FT_VALINS` |
| 000024 | GILRAT | `FT_VALFUN` |
| 000025 | SENAR | `FT_VLSENAR` |

- **R-2055 — aquisição de produção rural** (`TAFAPR2055.PRW`):
  - **O que entra:** NFs de entrada do mês, excluídas as canceladas, denegadas e inutilizadas, de
    fornecedores com `A2_TIPORUR` preenchido.
  - **Agrupamento:** CP, GILRAT e SENAR somados por item. Itens de serviço vão para o R-2010.
  - **vlrBruto** = valor da operação − IPI − ICMS-ST (deduções controladas por `MV_2055TRB`).
  - **indAquis:**

    | indAquis | Quando |
    |---|---|
    | 1 | PF |
    | 2 | PF via PAA |
    | 3 | PJ |
    | 4 | PF isenta |
    | 5 | PF via PAA e isenta |
    | 6 | PJ via PAA e isenta |
    | 7 | PF com fim de exportação (CFOP 1501/2501) |

  - **Isenção:** vem da TES (`F4_INDISEN`).
  - **Processo judicial:** vai em `infoProcJud`, com os valores **não retidos**.
  - **Opção pela folha:** `A2_INDCP` → `indOpcCP="S"`. **Não muda o cálculo**, só informa.
- **R-2050 — comercialização pelo produtor PJ ou agroindústria:**
  - **O que entra:** NFs de saída com os tributos 13/24/25.
  - **indCom:** 1 = mercado interno, 7 = isenção, 8 = PAA, 9 = exportação (CFOP 7xxx, 5501/5502/6501/6502).
- **R-2010/R-2020 — INSS retido sobre serviço:** entrada e saída. Exige o tipo de serviço no item e o tributo 000013.
- **R-4010 (PF) / R-4020 (PJ) — IRRF e PCC retidos:**
  - **Fontes:** NF, fatura e pagamentos (IR na baixa `000028`; PCC 10/11/18).
  - **Natureza de rendimento:** capturada por item da NF (DHR, `mata103REINF.prw`) e rateada por
    pagamento (`Adm/FINXREINF.PRW`).
- **DCTFWeb:** o Protheus não gera DCTFWeb nem DARF previdenciário. Só fecha o REINF (R-2099/R-4099).

## 7. Lacunas do Siagro frente a isto (2026-10-18)

Comparado com `PurchaseInvoiceWithholdings`, `FunruralCalculator` e `PurchaseInvoiceFunrural` do
backend. Cada item precisa ser **confirmado com o contador** antes de virar código.

| # | Tema | Siagro hoje | Protheus / lei | Risco |
|---|---|---|---|---|
| 1 | Mínimo de R$ 10 do PCC e do IRRF | Não existe: retém qualquer valor | PCC dispensado se PIS+COFINS+CSLL ≤ 10; IRRF dispensado se ≤ 10 | Retenção indevida em notas pequenas |
| 2 | Acumulação para o mínimo | Não existe | PCC acumula no **dia** por fornecedor; IRRF PJ no dia, PF no mês | Com o item 1 implantado, várias notas pequenas no mesmo dia escapariam da retenção |
| 3 | PCC de fornecedor PF | Retém se a natureza tiver alíquota | PF nunca sofre PCC | Depende de escolher a natureza certa |
| 4 | Momento da retenção | Só na emissão (cabeçalho) | Emissão **ou** baixa (`MV_BX10925`); a lei manda reter **no pagamento** | Pagamento parcial e adiantamento ficam errados |
| 5 | Arredondamento das retenções | `ToEven` | Protheus trunca por default; o SEFIP/REINF aceita centavos | Diferença de 1 centavo; definir com o contador |
| 6 | Funrural de fornecedor PJ | Retém se a natureza tiver alíquota ("PF, PJ e decisão judicial são naturezas diferentes") | PJ **não** sofre retenção na entrada | Natureza errada gera retenção indevida; vale uma trava pelo tipo do parceiro |
| 7 | Base do Funrural | GrandTotal da linha (inclui frete/seguro/outras − desconto) | Configurável (`F4_BSRURAL`: só mercadoria, + frete/despesas, + IPI/ST) | Confirmar se frete entra na base |
| 8 | Funrural em parcelas | — | Desconta só da **1ª parcela** | Verificar como o Siagro distribui nos títulos firmes |
| 9 | SENAR com liminar | Natureza separada (OK) | Liminar costuma afastar INSS+RAT e manter SENAR | OK, se o cadastro refletir isso |
| 10 | Livro fiscal (isentas/outras/diferido) | Não existe | SFT/SF3 com colunas por CFOP e alíquota | Pré-requisito do SPED Fiscal e da apuração |
| 11 | Apuração ICMS/PIS/COFINS | Não existe | MATA953 / FISA001 | Roadmap (SPED Fiscal / Contribuições) |
| 12 | Crédito presumido agro (Lei 10.925 art. 8º) | Não existe | CST 60–66 + alíquota reduzida, ou ajuste do período | Relevante para quem industrializa grãos |
| 13 | R-2055 | Roadmap | Campos por item: CP/GILRAT/SENAR separados, isenção, PAA, PF/PJ, opção pela folha | O Siagro já tem INSS/RAT/SENAR separados por linha — falta isenção/PAA/opção folha |
| 14 | IRPJ/CSLL | Não existe | TAF com eventos 100% parametrizados | Fora do escopo do ERP operacional; via ECF/contabilidade |
