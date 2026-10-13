# Complemento fiscal do contrato de compra — natureza de operação e totalizador de Funrural

Data: 2026-10-13
Status: aprovado para planejamento
Modo alvo: **somente `STANDALONE`**. Em `SAPB1` nada muda (a natureza lá é a `OUSG`, sem alíquota de
Funrural; a retenção da Yokotobi é feita no SAP e será um plano próprio).

Sub-projeto **1 de 4** de "título a pagar firme a partir do romaneio de compra" — ver § Roadmap.

## Problema

O pedido do usuário: cada romaneio de compra (tipo 8) confirmado deve gerar obrigação firme a pagar,
com o número da NF do produtor aparecendo quando ela for vinculada, e a liquidação deve acontecer
contra o título do contrato. Ao decidir como reter o Funrural nesse pagamento, apareceu o bloqueio:

- O Funrural só é calculado **na linha da NF de Entrada** (`PurchaseInvoiceFunrural`), com as
  alíquotas da **natureza de operação** (`USAGES.FunruralInssRate/RatRate/SenarRate`, spec 2026-10-09).
- O pagamento ao produtor segue o **grão**, não a NF — a NF pode demorar muito ou nunca chegar
  (problema fiscal do cliente, não do sistema).
- Logo, sem NF, não há de onde tirar a alíquota.

A solução do usuário: **informar a natureza de operação no contrato de compra**, como o contrato de
venda já faz desde a peça 2b (`2026-10-07-sales-contract-fiscal-complement-design.md`, que deixou
"complemento fiscal no contrato de compra" explicitamente fora de escopo). E a grade "Impostos" do
contrato, hoje digitada, passa a ser um **totalizador calculado pela natureza**, no molde da seção
"Tributos" do Documento de Entrada.

## Decisões do usuário

| # | Decisão |
|---|---|
| D1 | Só `STANDALONE`. Em `SAPB1` a seção não aparece, a grade digitada continua, e nada é exigido. |
| D2 | A grade digitada (`PURCHASE_CONTRACTS_TAXES`) é **substituída** pelo totalizador da natureza em `STANDALONE`. |
| D3 | Funrural é **sempre retido sobre o preço cheio**: Líquido ao produtor = Total − Funrural. Base = o próprio total. |
| D4 | `FunruralType` (Livre/Bruto) é **só comercial** — sai na impressão para o produtor saber como o preço foi negociado, **não entra em cálculo nenhum**. "Funrural livre" não existe na operação de fato. |
| D5 | A natureza é **obrigatória para aprovar** o contrato em `STANDALONE`. Contratos já aprovados sem natureza não são bloqueados — só exibem aviso. |
| D6 | **O fiscal alimenta a natureza, o comercial só lê.** Molde da 2b: tabela de complemento + diálogo no Detail + permissão própria. Nenhum campo no formulário Add/Edit. |
| D7 | A exigência vale para **todas as filiais `STANDALONE`**, não só as que emitem NF-e — a retenção ao produtor existe sem emissão de nota. Consequência: a **MH Agro** (`STANDALONE` sem NF-e, fora do `TaxCalculationGate`) também passa a exigir a natureza para aprovar contrato de compra. |
| D8 | **Copiar contrato não leva o complemento** — o fiscal confere de novo. |

## Modelo de dados

### `PURCHASE_CONTRACT_FISCAL_COMPLEMENTS` (nova) — `PurchaseContractFiscalComplement`

Espelho estrutural de `SalesContractFiscalComplement`. Nasce com um campo de negócio só; é o lugar
natural do que o sub-projeto 4 (escrituração simplificada) trouxer.

| Coluna | Tipo | Observação |
|---|---|---|
| `PurchaseContractKey` | uniqueidentifier, PK + FK → `PURCHASE_CONTRACTS` | 1:1, **cascade** (igual à venda) |
| `UsageCode` | `int NULL` | **sem FK** — em `SAPB1` a natureza é `OUSG`; mesma razão da venda (§3.1 da 2b) |
| `UpdatedAt` | `datetime NULL` | |
| `UpdatedBy` | `VARCHAR(100) NULL` | |

Navegação `PurchaseContract.FiscalComplement` **não** é criada: entraria no EDM do OData sem ninguém
pedir. A leitura é por function, como na venda.

### Permissão

`PermissionCodes.PurchaseContractFiscalEdit = "PURCHASE_CONTRACT_FISCAL_EDIT"`, ao lado de
`SalesContractFiscalEdit` em `IUserPermissions.cs`. Seed idempotente no `CommonContext`, ligado ao
`ADMIN`, `Down` removendo `ROLE_PERMISSIONS` antes de `PERMISSIONS` — molde de
`20261007151804_SeedSalesContractFiscalEditPermission`.

### Alíquotas

**Lidas ao vivo** da natureza via `IUsage.GetByIdAsync` — o totalizador é projeção, não dinheiro.
A **fotografia** de alíquota por título fica para o sub-projeto 2, que é onde ela vira valor a pagar.

## Regras

1. **Escopo de modo:** tudo abaixo só se aplica com `Erp = STANDALONE`. Em `SAPB1` os serviços novos
   recusam (`"Complemento fiscal do contrato de compra não se aplica ao modo SAPB1."`) e os existentes
   seguem como hoje.
2. **Validação da natureza** no `Set`: existe, `Inactive == false`, `Direction == Incoming`.
   Natureza **sem** alíquota de Funrural é aceita (retenção zero — ex.: produtor com decisão judicial
   tem natureza própria só com SENAR, e a que não retém nada também é legítima).
3. **`UsageCode = null` no `Set` limpa** a natureza (botão "Limpar" do diálogo). Permitido em contrato
   não aprovado; em contrato `Approved` é recusado — limpar reabriria a condição que a aprovação exigiu.
4. **Status editáveis:** qualquer um, **exceto** `Finished` e `Canceled`.
5. **Log:** toda troca efetiva de natureza (valor antigo ≠ novo) grava uma linha via
   `PurchaseContractsChangeLogService.Register`, com código de campo novo
   `ContractChangeLogFields.Usage = "Usage"`, `OldValue`/`NewValue` = `"<código> - <nome>"`. O frontend
   ganha o rótulo "Natureza de operação" no formatter do log.
6. **Aprovação:** `PurchaseContractsApprovalService` recusa contrato sem complemento ou com
   `UsageCode` nulo, **antes de qualquer escrita**, com
   `"Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar."`.
   Só em `STANDALONE`.
7. **Copiar contrato** (`PurchaseContractsCopyService`) **não** copia o complemento (D8).
8. **Excluir contrato** (`PurchaseContractsDeleteService`) remove o complemento explicitamente, como
   `SalesContractsDeleteService.cs:52-53` — a cascade cobre o banco, mas o serviço apaga filhos à mão
   e o InMemory não aplica cascade de entidade não carregada.
9. **Totalizador** (`PurchaseContractsTotalsService`), em `STANDALONE`:
   - `FunruralBase = TotalPrice` (Σ fixações **Confirmed**, já arredondado a 2);
   - `FunruralInssRate/RatRate/SenarRate` da natureza (nulo → 0);
   - valores por `FunruralCalculator.Calculate(base, inss, rat, senar)` — o **mesmo** cálculo da NF
     de Entrada (cada parcela `AwayFromZero` a 2), para os números baterem centavo a centavo;
   - `TotalFunrural = soma das três`; `NetToProducer = FunruralBase − TotalFunrural`;
   - `TotalTax = TotalFunrural` (o KPI "Total Impostos" muda de significado sem mudar de binding).
   - Sem natureza: tudo zero e `NetToProducer = FunruralBase`; o aviso de natureza ausente é a
     sinalização.
   - Em `SAPB1`: `TotalTax` continua a soma da grade digitada; campos novos zerados.
10. **A grade digitada não é apagada.** `PURCHASE_CONTRACTS_TAXES`, `TAXES`, os serviços
    `PurchaseContractsTaxes*` e o controller ficam. Em `STANDALONE` ela só deixa de aparecer e de ser
    somada. Nenhuma migration de dados.
11. **`FunruralType` não é lido por cálculo nenhum.** Comentário XML na propriedade dizendo isso
    (D4), para ninguém "conectá-lo" ao totalizador.
12. **`FinishedContractMutationGuardInterceptor`**: o complemento **não** entra no `switch` — a regra
    4 já recusa no serviço, e duplicar no interceptor só tornaria a mensagem pior.

## Serviços e API

Namespace `SiagroB1.Application/Services/PurchaseContracts/`.

| Serviço | Responsabilidade |
|---|---|
| `PurchaseContractFiscalComplementService` | `GetAsync(key)` → DTO; `SetAsync(key, usageCode, user)` com permissão, validação, upsert, log. `IUnitOfWork`, um `SaveChanges`. Construtor `(IUnitOfWork, IUsage, IUserPermissions, PurchaseContractsChangeLogService, IConfiguration)` — espelho de `SalesContractFiscalComplementService`; o modo sai de `ErpMode.IsStandalone(configuration)`, o mesmo teste que `TaxCalculationGate.IsStandalone` usa (aqui **sem** a chave "Emite NF-e" da filial — D7). |
| `PurchaseContractsApprovalService` | + guard da regra 6 |
| `PurchaseContractsTotalsService` | + totalizador da regra 9 |
| `PurchaseContractsDeleteService` | + remoção da regra 8 |

DTO `PurchaseContractFiscalComplementDto`: `UsageCode`, `UsageName`, `FunruralInssRate`,
`FunruralRatRate`, `FunruralSenarRate`, `IsComplete` (= `UsageCode != null`), `UpdatedAt`,
`UpdatedBy`.

`PurchaseContractTotalsResponseDto` += `FunruralBase`, `FunruralInssRate`, `FunruralRatRate`,
`FunruralSenarRate`, `FunruralInssValue`, `FunruralRatValue`, `FunruralSenarValue`, `TotalFunrural`,
`NetToProducer`.

**OData** (`ODataConfigurations.cs`, junto das de venda em ~516-527):

- function `PurchaseContractsGetFiscalComplement(Key)`;
- action `PurchaseContractsSetFiscalComplement(Key, UsageCode?)` — `UsageCode` como `int?`;
  `ODataActionParameters` pode chegar nulo e parâmetro ausente volta `true` com `null`.
- Rotas literais declaradas à mão nas duas formas.
- DTO de function com `Returns<IActionResult>` sai **camelCase** — usar `[JsonPropertyName]` em
  PascalCase, como a conciliação fiscal precisou (ver memória `odata-api-serializes-camelcase`), ou
  confirmar que o molde da venda já resolve.
- Valores monetários e alíquotas no DTO de totais: o endpoint de totais já existe e já é consumido;
  manter o mesmo tipo dos campos atuais.

DI: `AddScoped` em `AddApplicationServices()`.

## Frontend (`siagro-b1-frontend`)

### Detail do contrato de compra (só `ui>/standalone`)

- **Seção nova "Complemento Fiscal"** logo **depois de "Dados do Contrato"**:
  - fragmento somente leitura `PurchaseContractFiscalComplement.fragment.xml` — natureza
    (código + nome) e as três alíquotas, `Form` com `ColumnLayout`;
  - `MessageStrip` de aviso "Natureza de operação não informada — o contrato não pode ser aprovado."
    quando `!IsComplete`;
  - botão **"Editar"** visível só com `SessionService.hasPermission('PURCHASE_CONTRACT_FISCAL_EDIT')`
    e status editável (regra 4);
  - diálogo `PurchaseContractFiscalComplementDialog.fragment.xml` — value help de natureza
    (`UsagesSelectDialog`, filtros `Inactive eq false` e `Direction eq 'Incoming'`) e "Limpar".
  - Cópia adaptada de `salesContracts/fragments/SalesContractFiscalComplement.fragment.xml`,
    `FiscalComplementDialog.fragment.xml` e `salesContracts/Detail.controller.ts:100-234`.
- **Seção "Impostos"**: em `STANDALONE` o fragmento `PurchaseContractTaxes` (grade digitada) é
  trocado por `PurchaseContractFunruralTotals.fragment.xml`, somente leitura, `sap.m.Table` com
  colunas Tributo / Base de cálculo / Alíquota (%) / Valor:
  - linhas "Funrural - INSS", "Funrural - RAT", "Funrural - SENAR", omitindo as de valor zero
    (como `summarizeInvoiceTaxes`);
  - rodapé "Total Funrural" e "Líquido ao produtor".
  - Fonte: os campos novos de `purchaseContractsTotals`, que o Detail já chama. Em `SAPB1`, a grade
    digitada como hoje.
- **Cabeçalho:** "Total Impostos" segue em `viewModel>/TotalTax`. KPI novo "Líquido ao produtor",
  só `STANDALONE`.
- Recarregar totais **e** complemento depois de salvar o diálogo, e depois de confirmar/estornar
  fixação (a base muda).

### Add / Edit

Em `STANDALONE`, a seção "Impostos" (grade digitada) **some**. Não há campo de natureza (D6).

### Aprovação (`approval/Detail`)

Mesmo quadro do totalizador e o mesmo aviso de natureza ausente, somente leitura, sem "Editar" —
quem aprova enxerga antes de clicar e ser recusado.

### Log de alterações

`formatContractChangeLogField` (ou o formatter equivalente do log de contrato) ganha
`Usage → "Natureza de operação"`.

### Armadilhas conhecidas

- `--` dentro de comentário XML mata o fragmento inteiro, sem gate nenhum acusar.
- `core:require` da view **não** chega ao fragmento — `core:require` no próprio
  `FragmentDefinition` ou `.formatter.x` com ponto.
- `BadRequest(string)` chega como `responseJSON` = a própria string — o handler do diálogo precisa
  aceitar as três formas, ou a mensagem de negócio some (lição do guard de cancelamento).

## Impressão do pré-contrato

`SiagroB1.Reports/Services/PrePurchaseContractReportService.cs` + `PrePurchaseContract.frx`:

- Em `STANDALONE`, `data.Taxes` passa a ser montado das linhas de Funrural (mesmo
  `FunruralCalculator`, mesma base), omitindo valor zero, mais uma linha **"LÍQUIDO AO PRODUTOR"**.
  O `.frx` não muda de estrutura (IMPOSTO / ALÍQUOTA (%) / VALOR (R$)).
- A natureza vem de `PURCHASE_CONTRACT_FISCAL_COMPLEMENTS` + `USAGES` (Reports usa `AppDbContext`
  direto e roda em `STANDALONE` com a tabela local).
- Em `SAPB1`, como hoje. `FUNRURAL: BRUTO/LIVRE` continua impresso (D4).

## Migrations

- `AppContext/AddPurchaseContractFiscalComplement` — a tabela.
- `CommonContext/SeedPurchaseContractFiscalEditPermission` — permissão + `ROLE_PERMISSIONS` do ADMIN,
  SQL idempotente, GUIDs fixos.

Deploy: dar a permissão ao perfil do fiscal (o seed só liga ao ADMIN). Contratos **em aprovação**
no momento do deploy passam a exigir a natureza para aprovar — listar quantos são antes, por filial.

## Testes (`SiagroB1.Application.Tests`)

xUnit + EF InMemory, dublês `Fake*` à mão (`FakeUsage`, `FakeUserPermissions` — reutilizar os da
venda se existirem).

- `Set` sem permissão → recusado;
- natureza de saída, inativa ou inexistente → recusada;
- natureza sem alíquota → aceita;
- limpar em contrato aprovado → recusado; em rascunho → aceito;
- contrato `Finished`/`Canceled` → recusado;
- troca de natureza grava **uma** linha de log com antigo e novo; salvar a mesma natureza não grava;
- `Set` em `SAPB1` → recusado;
- aprovação sem natureza em `STANDALONE` → recusada com a mensagem; em `SAPB1` → aprovada;
- totalizador PF (1,32 / 0,11 / 0,20) sobre base com centavos bate com `FunruralCalculator` e
  `NetToProducer = base − total`;
- natureza sem alíquota → Funrural zero, líquido = base;
- PAF sem fixação confirmada → base zero;
- `TotalTax` em `STANDALONE` = total do Funrural, ignorando linhas da grade digitada;
- copiar contrato não cria complemento; excluir contrato remove o complemento;
- EDM: function e action presentes, `UsageCode` como `Edm.Int32` anulável.

## Verificação

Gates (necessários, nunca suficientes):

```
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context CommonDbContext \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web
yarn ts-typecheck && yarn lint      # frontend, dev server parado
```

Roteiro no navegador, stack **CEAGUI** (`STANDALONE`, profile `ceagui`):

1. Cadastrar natureza de **entrada** PF com 1,32 / 0,11 / 0,20.
2. Criar contrato de compra fixo: Add/Edit **sem** grade de impostos. Tentar aprovar → recusa
   nomeando o Complemento Fiscal, **com a mensagem visível na tela**.
3. Detail → Complemento Fiscal → Editar → escolher a natureza. O quadro "Impostos" mostra as 3
   linhas e o líquido; conferir contra a conta à mão. Aprovar → passa.
4. Trocar a natureza no contrato aprovado → linha "Natureza de operação" no Log de Alterações.
   Tentar "Limpar" → recusado.
5. Login `teste.comercial` (sem a permissão) → seção visível, sem "Editar".
6. Tela de Aprovação mostra quadro e aviso.
7. Imprimir o pré-contrato → linhas de Funrural + líquido; `FUNRURAL: BRUTO`.
8. Contrato PAF sem fixação → base zero; confirmar uma fixação → quadro atualiza.
9. Yokotobi (`SAPB1`, profile `yktb`) → nada mudou: sem seção, grade digitada presente, aprovação sem
   exigência.
10. Derrubar a stack **matando por PID**.

## Fora deste sub-projeto

- Título por fixação com composição por romaneio, trava de baixa, encerramento — sub-projeto 2.
- Compensação de adiantamento — sub-projeto 3.
- Natureza do contrato como padrão na Entrada/conciliação — sub-projeto 4.
- Retenção em `SAPB1` (Yokotobi) — plano próprio.
- Condição de pagamento, informações adicionais, pedido no complemento de compra — sem leitor hoje.
- Natureza em colunas/filtros das listas de contrato — sem pedido.

## Roadmap — decisões já tomadas para os sub-projetos seguintes

Registradas aqui para não se perderem; cada sub-projeto terá spec própria.

**Sub-projeto 2 — Valor entregue no título (obrigação firme por romaneio)**

- **Um título por fixação**, em FIX e em PAF — o título provisório que já existe (Fase 1) é o único
  que recebe baixa. **Não** se cria um título por romaneio.
- Os romaneios entram como **linhas de composição** do título: romaneio, peso líquido, preço, bruto,
  Funrural, líquido e **número da NF vinculada ou "Sem NF"** (da Conciliação Fiscal de Compras).
- O título ganha "Valor contratado" e **"Valor entregue"**. O evento é o romaneio **alocado** ao
  contrato (`PurchaseContractAllocation`) — o romaneio confirmado ainda não pertence a contrato; o
  desfazimento real é a exclusão da alocação.
- O volume alocado ao contrato **preenche as fixações em ordem de confirmação**; volume entregue
  ainda sem preço fixado **não vira valor** e não pode ser pago. Um romaneio pode aparecer dividido
  entre duas fixações.
- **Trava rígida:** baixa + compensação ≤ valor entregue **líquido de Funrural**. Pagar antes da
  entrega é só por adiantamento.
- **A movimentação de grão gera o financeiro independentemente da natureza da NF** —
  `CreatesFinancialDocument` da natureza não liga nem desliga nada neste fluxo.
- A NF **não** é pré-requisito de pagamento (ela pode nunca vir).
- Funrural por linha usa a natureza do **contrato** (este sub-projeto), com fotografia das alíquotas.
- Encerrar contrato passa a **ajustar** o título ao entregue em vez de cancelá-lo.
- Diverge de propósito do spec da Fase 1 (`2026-09-07-financial-documents-design.md`), que previa o
  firme nascendo do documento fiscal.

**Sub-projeto 3 — Compensação de adiantamento** contra o título (`AdvanceApplication` +
`AppliedAmount`), sob a mesma trava do entregue.

**Sub-projeto 4 — Escrituração simplificada** da NF do produtor: Entrada e conciliação puxam a
natureza do contrato como padrão.
