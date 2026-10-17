# Efeito dos tributos e DIFAL no Documento de Entrada: desenho (2026-10-17)

> Continuação de `2026-10-17-other-purchases-design.md`, na mesma branch `feature/other-purchases`.
> Conversado com o usuário em 17/10/2026. Só STANDALONE.

## Por quê

A tabela "Tributos" do Detail soma a fotografia fiscal das linhas, mas não diz o que cada tributo faz.

As retenções da NFS-e (ISS, IRRF, INSS, PIS, COFINS, CSLL) aparecem só na seção Retenções. O usuário quer vê-las na
tabela, junto dos tributos que entram por apuração. O documento de entrada precisa então mostrar, por tributo, se é
crédito, débito, sem crédito, diferido ou retido.

As compras de uso e consumo e de ativo, que a feature "outras compras" passou a permitir, geram DIFAL quando o
fornecedor é de outra UF (EC 87/2015, LC 190/2022). Hoje o Siagro não calcula o DIFAL.

Base legal levantada, a confirmar com o contador:
- **ICMS.** Crédito na mercadoria para revenda ou insumo (LC 87/96, arts. 19 e 20). No ativo imobilizado o crédito
  é em 48 parcelas (CIAP). Uso e consumo não dá crédito até 2033 (LC 87/96, art. 33, I).
- **PIS/COFINS.** Crédito só no regime não cumulativo (Leis 10.637/02 e 10.833/03), com CST de 50 a 66.
- **IBS/CBS.** Regras da LC 214/2025, que crescem por fases.
- **DIFAL e FCP.** O FCP é o adicional de até 2 pontos que alguns estados cobram junto do DIFAL (ADCT, art. 82).

## Decisões do usuário

1. A tabela "Tributos" ganha a coluna **"Efeito"**.
2. As **retenções** do cabeçalho entram como linhas da tabela.
3. **DIFAL calculado**, com a **regra da base configurável por UF** (única ou dupla).
4. Também nesta entrega:
   - **FCP**;
   - **alíquota interna por produto**;
   - **DIFAL quando a filial não calcula tributos**;
   - **crédito conforme o regime tributário**.
5. Os próximos desenvolvimentos ficam fora desta entrega: CT-e na entrada com contra-nota, entrada de complemento
   de ICMS e NF de saída de complemento de ICMS.

## Desenho

### 1. Cadastros

**ICMS por UF.** Tabela nova `ICMS_STATE_SETTINGS`, com tela em Cadastros, "ICMS por UF", e migration de menu.

| Campo | Tipo | Significado |
|---|---|---|
| `StateCode` | sigla, chave | UF |
| `InternalRate` | %, 7,4 | alíquota interna geral |
| `FcpRate` | %, 7,4, padrão 0 | FCP da UF |
| `DifalBaseRule` | enum `Single = 0` / `Double = 1` | regra da base |

A migration semeia **SP: 18 % / FCP 0 / base dupla**, só se não existir. As demais UFs o usuário cadastra.

**Alíquota interna por produto.** Tabela `ICMS_ITEM_STATE_RATES`, chave `(ItemCode, StateCode)`, com `InternalRate`
e `FcpRate` anulável (nulo herda o da UF). Fica numa aba ou seção do Complemento do Produto, que já existe.
Prevalece sobre a da UF.

**Natureza de entrada.** Campo novo **"Destinação"**, enum `PurchasePurpose`:
- `ResaleOrInput = 0` (revenda ou insumo), o padrão;
- `UseAndConsumption = 1` (uso e consumo);
- `FixedAsset = 2` (ativo imobilizado).

O campo fica em `USAGE_EFFECTS`, como os outros efeitos de negócio. O DIFAL incide quando a destinação é uso e
consumo ou ativo, a operação é interestadual (UF do emitente diferente da UF da filial) e a filial é contribuinte
do ICMS, o que vale para toda filial com inscrição estadual.

**Filial.** Campo novo **"Regime de PIS/COFINS"**, enum `PisCofinsRegime`: `Cumulative = 0` (Lucro Presumido) ou
`NonCumulative = 1` (Lucro Real). O padrão é cumulativo. O Simples Nacional já é identificado pelo `TaxRegime` (CRT).

### 2. Cálculo do DIFAL, por linha

Entradas:
- `V`: o total da linha (`GrandTotal`), que já soma frete, seguro e outras despesas e tira o desconto.
- `aInter`: a alíquota interestadual.
  - Com o motor de tributos ativo, vem do ICMS já calculado na linha (`IcmsRate`).
  - Sem o motor, vem de `InterstateIcmsRate.Resolve(UF do emitente, UF da filial, origem da mercadoria do produto)`,
    que é puro e fica no projeto Fiscal.
  - Se a linha tiver `IcmsRate > 0` informado, ele prevalece.
- `ICMSorig`: o ICMS de origem. É `IcmsValue` da linha quando maior que zero. Senão, `V × aInter`.
- `aInt`: a alíquota interna. É a do produto na UF da filial ou, se não houver, a da UF. Sem nenhuma das duas, a
  confirmação e a gravação recusam com "Cadastre a alíquota interna de ICMS da UF {UF} em ICMS por UF.".
- `aFcp`: o FCP do produto na UF ou, se não houver, o da UF.

Fórmulas, com arredondamento em 2 casas, ToEven:
- **Base única:** `BaseDifal = V`. Então `DIFAL = V × aInt − ICMSorig` e `FCP = V × aFcp`. No caso normal isso dá
  `V × (aInt − aInter)`; com base reduzida na origem, desconta o ICMS realmente destacado.
- **Base dupla:**
  - `BaseDifal = (V − ICMSorig) ÷ (1 − (aInt + aFcp))`;
  - `DIFAL = BaseDifal × aInt − ICMSorig`;
  - `FCP = BaseDifal × aFcp`.
- O DIFAL nunca fica negativo. Se `aInter ≥ aInt`, o DIFAL é 0.

Fotografia nova na linha (`PURCHASE_INVOICES_ITEMS`, `DEFAULT 0`): `DifalBase`, `DifalInternalRate`,
`DifalInterstateRate`, `DifalValue`, `FcpRate`, `FcpValue`.

O cálculo roda nos mesmos pontos em que as retenções são recalculadas: Create, Update, ItemsCreate, ItemsUpdate e
ItemsDelete. Documentos de devolução e complemento de valor não calculam DIFAL.

**Efeito financeiro.** Não há título no contas a pagar. Para contribuinte, o DIFAL entra na apuração mensal do ICMS.
A guia por operação, para não contribuinte, está fora do escopo.

### 3. Efeito de cada tributo na tabela

A tabela "Tributos" do Detail da entrada passa a ter as colunas Tributo, **Efeito**, Base de cálculo e Valor. O
efeito é calculado no cliente, com função pura e QUnit, a partir de dados que o servidor já entrega:

| Tributo | Efeito |
|---|---|
| ICMS, filial do Simples | "Sem crédito" |
| ICMS, destinação revenda ou insumo, valor > 0 | "Crédito na apuração" |
| ICMS, destinação uso e consumo | "Sem crédito" |
| ICMS, destinação ativo | "Crédito CIAP (1/48)" |
| ICMS diferido | "Diferido" |
| DIFAL e FCP do DIFAL | "Débito na apuração" |
| PIS/COFINS com CST de 50 a 66, filial não cumulativa | "Crédito na apuração" |
| PIS/COFINS, outros casos | "Sem crédito" |
| IBS/CBS | "Crédito na apuração", exceto CST de isenção ou não incidência, que é "Sem crédito" |
| ISS, IRRF, INSS, PIS, COFINS e CSLL retidos, Funrural | "Retido na fonte" |

- Os nomes exibidos para as retenções são "ISS retido", "IRRF retido", "INSS retido", "PIS retido", "COFINS retido"
  e "CSLL retido". A base é `WithholdingBase`.
- Linhas com valor 0 e base 0 não aparecem, como hoje.
- O **documento de saída** reaproveita a tabela com a coluna Efeito: ICMS, PIS e COFINS de saída aparecem como
  "Débito na apuração". A saída não tem retenções.

### 4. O que o servidor expõe

- **Linha:** os novos campos de DIFAL e FCP.
- **Natureza:** `PurchasePurpose`.
- **Filial:** `PisCofinsRegime`.
- **Tabela de efeitos.** O cliente precisa da destinação da natureza de cada linha, da regime da filial e do CRT. A
  forma de entregar isso (`$expand` na natureza, propriedade calculada na linha ou leitura separada) fica a critério
  do construtor, respeitando as armadilhas de propriedade tardia do OData.

### 5. Fora do escopo

- Guia de DIFAL por operação para não contribuinte.
- SPED e EFD-REINF.
- Apuração mensal e livros.
- Crédito CIAP efetivo em 48 parcelas. A tabela só sinaliza o efeito.

## Telas

- Cadastros, "ICMS por UF": lista editável com UF, Alíquota interna %, FCP % e Regra da base.
- Complemento do Produto: seção "Alíquota interna por UF".
- Natureza de entrada: campo "Destinação".
- Filial: campo "Regime de PIS/COFINS".
- Documento de Entrada:
  - colunas DIFAL e FCP no diálogo "Tributos do item";
  - coluna Efeito e linhas de retenção na tabela "Tributos".

## Verificação

- Testes unitários do cálculo no projeto Fiscal: base única, base dupla, FCP, alíquota do produto sobrepondo a da
  UF, interestadual ≥ interna, e sem motor de tributos.
- Testes de aplicação:
  - DIFAL gravado na linha e recalculado ao editar a linha;
  - recusa sem a alíquota da UF;
  - nenhum DIFAL em operação interna ou com destinação revenda ou insumo.
- Navegador na CEAGUI dev:
  - NF de uso e consumo de fornecedor do PR (12 %, ou 7 % conforme a regra) para filial de SP;
  - conferir o DIFAL de base dupla calculado à mão;
  - conferir a tabela Tributos com os efeitos e as retenções.

## Decisões minhas (revisão do construtor, 17/10/2026)

Marcadas como decisão minha, não do usuário. Os itens 1 e 3 merecem o olho do contador.

1. **ICMS de origem sem destaque** (fornecedor do Simples, isento etc.): usa sempre `V × aInter`. A diferença de
   alíquota é calculada pela alíquota interestadual da operação, haja ou não destaque na nota (LC 123/06,
   art. 13, §1º, XIII, "h", para o Simples).
2. **"DIFAL quando a filial não calcula tributos"** é o caminho sem motor já descrito no §2 (interestadual por
   `InterstateIcmsRate.Resolve`). Não há flag nova na filial.
3. **Filial do Simples também calcula DIFAL**, pela mesma LC 123. O efeito dela é **"A recolher em guia"**, e não
   "Débito na apuração", porque o Simples não tem apuração mensal de ICMS. A guia em si continua fora do escopo.
4. **Precedência da alíquota interestadual.** Com o motor ativo, vale o `IcmsRate` que o motor acabou de calcular,
   porque ele recalcula a cada gravação. Sem motor, vale o `IcmsRate` da linha quando for maior que zero
   (informado ou vindo do XML). Senão, vale `Resolve`. Não precisa de flag.
5. **Implantação sem travar documentos existentes.** Na gravação, se faltar a alíquota da UF, o DIFAL fica 0 e a
   gravação passa. Só a **confirmação** recusa. Assim um documento existente nunca fica sem edição depois do deploy.
6. **Destinação é por linha**, porque segue a natureza da linha. O padrão é revenda ou insumo, **sem inferência**
   na migration. Revisar as naturezas criadas para outras compras entra como nota de deploy.
7. **Documento de saída.** Venda normal com valor: ICMS, PIS, COFINS, IBS e CBS como "Débito na apuração".
   Saída do tipo devolução: "Crédito na apuração". Diferido aparece como "Diferido". ST continua fora, porque o
   Siagro não calcula ICMS-ST.
8. **Onde incide o DIFAL.** Só em documento de **terceiro**, tipo Normal, não devolução. Fica fora a emissão
   própria e a contra-nota, que são compra de grão de produtor: revenda, sem DIFAL. DIFAL e FCP **não** entram em
   `NetPayable`, `GrandTotal` nem na NF-e, porque são dívida do comprador com o estado, não com o fornecedor.
9. **Importação.** Emitente com UF "EX" não calcula DIFAL, porque a importação tem ICMS próprio.
