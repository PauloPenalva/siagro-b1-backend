# Washout no Contrato de Venda — plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use `superpowers:subagent-driven-development` (recomendada) ou `superpowers:executing-plans` para implementar este plano tarefa a tarefa. Os passos usam checkbox (`- [ ]`) para acompanhamento.

**Objetivo:** registrar no contrato de venda a desistência de parte ou de todo o volume ainda não entregue (faturado). O washout tira o volume do saldo, reduz o provisório a receber da fixação e gera um título a receber do cliente com `max(preço da fixação − mercado, 0) × volume fixado + multa`. Ciclo Em aprovação → Aprovado/Rejeitado, estorno do aprovado, tela de aprovação no grupo Vendas.

**Arquitetura:**

- Espelho do washout de compra (`PurchaseContractWashout` e seus serviços, já em produção), com nomes de venda: entidade filha `SalesContractWashout`, agregados persistidos `SalesContract.WashedOutVolume`/`WashedOutUnfixedVolume` escritos só por `SalesContractsWashedOutVolumeService`, guarda compartilhada `SalesContractsWashoutGuard`, serviços Create/Approval/Reject/Reverse/Get e OData actions `SalesContractsWashout*`.
- Diferenças da venda: fórmula invertida; "entregue" = **alocado no ledger** (`SalesContractsRecalculateBalanceService.CalculateAllocatedAsync`); a aprovação recusa quando o provisório ficaria abaixo do já composto/baixado; origem nova `FinancialDocumentOrigin.SalesContractWashout = 8`; o título é livre para baixa (não é `IsSalesFirmReceivable`).
- Os serviços financeiros ganham o lado de venda nos pontos que a compra já tem: `FinancialDocumentsGenerateService` (volume restante), `FinancialDocumentsAdjustProvisionalService` (ajuste no lugar), `FinancialDocumentsGenerateWashoutReceivableService` (sobrecarga) e `FinancialDocumentsCancelService` (recusa).

**Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), ASP.NET Core OData v4, xUnit; OpenUI5 1.141 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-15-sales-contract-washout-design.md`. É a autoridade. Molde: `2026-09-14-purchase-contract-washout-design.md` e os planos executados `docs/superpowers/plans/2026-09-14-purchase-contract-washout-backend.md` (backend) e `siagro-b1-frontend/docs/superpowers/plans/2026-09-14-purchase-contract-washout-frontend.md`. Onde o plano de compra e o código atual divergem, vale o **código atual** (o código de compra citado aqui foi lido em 15/10/2026).

## Restrições globais

### Repositórios, branch e commits

- **Dois repositórios, branch `feature/sales-contract-washout` nos dois** (os dois já existem):
  - backend: `C:\Projetos\SiagroB1\siagro-b1-backend`, Tasks 1–8. O HEAD tem o spec.
  - frontend: `C:\Projetos\SiagroB1\siagro-b1-frontend`, Tasks 9–10.
- Rode `git branch --show-current` antes de cada `git add`. **Nunca push. Nunca merge.**
- Todo arquivo novo leva `git add <path>` logo depois de criado. O último passo de cada task é **stage**. O **controlador** commita com a mensagem dada.
- Formato da mensagem de commit:
  - assunto `tipo(escopo): descrição pt-BR, imperativo, minúscula, sem ponto`;
  - escopo `sales-contract` (Tasks 1–3, 5–10) ou `financial` (Task 4);
  - corpo com o PORQUÊ;
  - trailer `DB:` **só** nas tasks com migration: `DB: AddSalesContractWashouts` (Task 1) e `DB: AddSalesContractWashoutApprovalMenu` (Task 8);
  - fim com `Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>`, que o script do controlador preenche.

### Convenções e escopo

- Identificadores em inglês. Texto ao usuário em pt-BR. Comentários em pt-BR.
- O washout de **compra não muda de comportamento**. Nenhum texto de compra muda.
- "Washout" não se traduz na tela.

### Mensagens exatas (copiar literalmente)

Iguais à compra (copiadas de `PurchaseContractsWashoutGuard`, `…CreateService`, `…ApprovalService`, `…RejectService`, `…ReverseService`, `PurchaseContractsCloseService`, `PurchaseContractsCancelService`, `PurchaseContractsPriceFixationsCancelService`, `PurchaseContractsPriceFixationDeleteService`):

| # | Texto |
|---|---|
| W1 | `Contrato precisa estar aprovado para movimentar washout.` |
| W2 | `Os volumes do washout não podem ser negativos.` |
| W3 | `Informe o volume do washout.` |
| W4 | `O preço de mercado não pode ser negativo.` |
| W5 | `A multa não pode ser negativa.` |
| W6 | `Informe o motivo do washout.` |
| W7 | `O motivo do washout deve ter no máximo 500 caracteres.` |
| W8 | `Contrato de preço fixo não tem volume a fixar: informe só o volume fixado.` |
| W9 | `Informe a fixação de preço do volume fixado.` |
| W10 | `A fixação informada não pertence a este contrato.` |
| W11 | `A fixação informada não está confirmada.` |
| W12 | `Volume fixado excede o saldo da fixação. Disponível: {N3}, solicitado: {N3}.` |
| W13 | `Volume fixado excede o volume com preço ainda não entregue. Disponível: {N3}, solicitado: {N3}.` |
| W14 | `Volume não fixado excede o saldo a fixar. Disponível: {N3}, solicitado: {N3}.` |
| W15 | `Volume do washout excede o saldo físico do contrato. Disponível: {N3}, solicitado: {N3}.` |
| W16 | `Volume do washout excede o saldo não liberado do contrato. Disponível: {N3}, solicitado: {N3}. Encerre ou cancele a liberação antes.` |
| W17 | `Informe o vencimento do título a receber.` |
| W18 | `Só é possível aprovar washout em aprovação. Status atual: {Status}.` / `Só é possível rejeitar washout em aprovação. Status atual: {Status}.` / `Só é possível estornar washout aprovado. Status atual: {Status}.` |
| W19 | `Informe o motivo da rejeição.` / `Informe o motivo do estorno.` / `O motivo deve ter no máximo 500 caracteres.` / `O comentário deve ter no máximo 500 caracteres.` |
| W20 | `Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.` (Finished) / `Contrato precisa estar aprovado para estornar washout.` |
| W21 | `Contrato possui {n} washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de encerrar.` (spec §5.6) |
| W22 | `Contrato possui {n} washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de cancelar.` |
| W23 | `Fixação possui washout registrado. Rejeite ou estorne o washout antes de estornar a fixação.` |
| W24 | `Fixação possui washout registrado (mesmo rejeitado ou estornado) e não pode ser excluída. Rejeite a fixação em vez de excluí-la.` |
| W25 | `Washout não encontrado.` / `Fixação de preço não encontrada.` / `Washout sem valor não gera título a receber.` / `Informe o vencimento do título a receber do washout.` |
| W26 | Motivo do cancelamento do título no estorno: `Washout estornado` (`PurchaseContractsWashoutReverseService.ReceivableCancellationReason`, reaproveitada). |

Próprias da venda:

| # | Texto |
|---|---|
| V1 | `O provisório {código} já tem {composto N2 pt-BR} composto: o washout o deixaria em {novo N2 pt-BR}. Desfaça a composição antes de aprovar.` (spec §5.2) |
| V2 | Motivo de cancelamento do provisório zerado: `Volume lavado por washout` (spec §5.2) |
| V3 | S10 com a saída (spec §5.6): `O título {código} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout.` |
| V4 | `Título gerado por washout não pode ser cancelado aqui. Estorne o washout no contrato de venda {OriginDocNumber}.` (espelho do F4 da compra) |
| V5 | `Contrato de venda não encontrado.` |
| V6 | Recurso `SALES_CONTRACT_HAS_WASHOUTS`: pt-BR `Contrato de Venda possui washout registrado, não é possivel remover da aprovação.` / en `Sales contract has washouts; it cannot be withdrawn from approval.` (espelho de `PURCHASE_CONTRACT_HAS_WASHOUTS`, mesma grafia) |
| V7 | Log do contrato: `WO-{n}: {fixado N3}{ un} fixado + {não fixado N3}{ un} não fixado, R$ {valor N2} — {Em aprovação/Aprovado/Rejeitado/Estornado}` (mesmo formato da compra) |
| V8 | Formatter de origem: `SalesContractWashout` → `Washout de contrato de venda` |

### Armadilhas conhecidas

- `ODataActionParameters` pode chegar **nulo**. Parâmetro string do OData chega nulo: nunca `.ToString()` direto.
- Erro de action de washout volta como erro OData de verdade (`WashoutActionResults.FromException`, em `SiagroB1.Web/Actions/PurchaseContracts/WashoutActionResults.cs`, `internal`, mesmo assembly): reaproveite, não copie.
- Navegação e GET por chave dão **404** sem rota declarada à mão (`[HttpGet("odata/SalesContracts({key:guid})/Washouts")]`).
- `SingleResult.Create(IQueryable)` no GET por chave: materializado, o `$expand` volta nulo.
- `Edm.Decimal` chega como **string** no frontend: `Number(x)` antes de comparar. Enum em binding: `targetType: 'any'`.
- `--` dentro de comentário XML mata o fragmento sem erro. Todo XML alterado **tem de ser parseado**: `python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" <arquivos>`.
- `Select` com `selectedKey` de JSONModel: grave os itens **antes** de abrir o diálogo.
- EF InMemory não confere FK nem índice único: a FK `Restrict` washout → fixação/contrato só estoura (547) em banco real; as travas vêm dos serviços.
- As somas de `SalesContractsWashedOutVolumeService` leem o **banco**: quem muda status salva antes de recalcular (mesma ordem da compra).
- `FinancialDocumentsGenerateService`, `…AdjustProvisionalService` e `…GenerateWashoutReceivableService` buscam número por Dapper: **chame antes** de abrir a transação.
- `FinancialDocumentsCancelService.Cancel` é `internal static`: só dentro da Application.
- `yarn test` inteiro **nunca passa**. Gates do frontend: `yarn ts-typecheck && yarn lint && yarn ui5lint`. QUnit pelo navegador.
- Migrations:
  - Só `dotnet ef migrations add`, que não toca o banco.
  - **Nunca** `database update` fora da Task 11, e lá só com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito e conferido (`CEAGUI_SIAGRO_DEV`).
  - Nunca o profile `db-migration`.
- Processos: matar **só por PID** (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`).

### Comandos de teste

- Backend, na raiz: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.
- Suíte inteira: `dotnet test SiagroB1.Application.Tests`. Antes da Task 1, rode uma vez e anote a contagem de verdes no ledger: ela é o piso de todas as tasks.

## Decisões do plano (lacunas do spec)

1. **Gerador do título a receber: sobrecarga, não serviço irmão.** `FinancialDocumentsGenerateWashoutReceivableService.EnqueueAsync(SalesContract, SalesContractWashout, string)` ao lado da de compra, as duas delegando a um `BuildAsync` privado. Menor diff: DI e construtores ficam iguais.
2. **Ajuste do provisório: método novo `EnqueueForSalesFixationAsync`** em `FinancialDocumentsAdjustProvisionalService`, com a trava V1 unificada: o novo valor (zero quando o volume zera) não pode ficar abaixo de `max(ComposedAmount, SettledAmount)`. Isso cobre o caso "zerar com composição", que na compra tem outra mensagem; na venda vale V1 (o spec só define V1). Sem composição nem baixa e volume zero, cancela com V2.
3. **"Faturado" na criação** = `CalculateAllocatedAsync` (ledger recalculado, não o `AllocatedVolume` persistido), como o spec manda. O saldo físico (W15) usa o `AllocatedVolume` persistido, como na compra.
4. **Consumidores conferidos no código:**
   - `SalesContract.AvaiableVolume`, `TotalAvailableToRelease`, `TotalAvailableToReleaseWithoutProvisioning`, `AvailableVolumeToPricing`: descontam (Task 1). Por eles, sem código novo, passam a descontar: `PhysicalAvailableToRelease`, `SalesShipmentReleasesCreateService`, `SalesShipmentReleasesApprovationService`, `SalesContractsReallocationCreateService` (saldo do destino) e o resultado do Recalcular Saldo.
   - SQL: `SalesContractsGetShipmentReleasesAvailableService` e `SalesContractsGetReconciliationTargetsService.Balance` (espelho declarado de `AvaiableVolume`) descontam (Task 2).
   - `SalesContractsGetNegativeBalancesService`: **não muda**. Ele aponta faturamento além do contratado (ledger); washout não fatura. Na compra não há equivalente.
   - Relatório de posição (`ContractPositionData.SalesPositionsAsync`): passa a somar o washout de venda em `WashedOut` e no `Balance` (Task 2). O relatório mensal só tem coluna `WashedOutPurchase`: **fica como está** (fora do spec).
   - Notificações: `NotificationEventType.Washout*` e os rótulos (backend e `Subscriptions.fragment.xml`) já são genéricos por tipo de documento. **Sem mudança.**
   - Log do contrato: o código de campo `Washout` e o rótulo do frontend (`formatContractChangeLogField`) já existem. Só entra a sobrecarga `DescribeWashout(SalesContractWashout, …)`.
5. **Travas em serviços existentes além do spec §5.6, por espelho da compra:** cancelar o contrato de venda com washout `InApproval` (W22) e excluir fixação com washout de qualquer status (W24, FK `Restrict`). Sem elas o contrato cancelado fica com washout órfão e a exclusão estoura 547 em banco real.
6. **Interceptor:** `FinishedContractMutationGuardInterceptor` passa a coletar chaves de venda **só** de `SalesContractWashout` e conferir em `SalesContract`. Os demais filhos de venda continuam fora (não é escopo).
7. **Frontend:** o total "Washout" do cabeçalho liga direto em `WashedOutVolume` do contexto OData, como o "Saldo" da venda já faz (`AvaiableVolume`); o DTO de totais ganha o campo pelo spec, mas a tela de venda não lê totais para saldo. Os fragmentos genéricos de compra (`WashoutReverseDialog`, `washoutApproval/fragments/WashoutDecisionDialog`) e os tipos `types/PurchaseContractWashout.ts` são **reaproveitados**; só o diálogo Registrar e a tabela ganham cópia de venda (textos de "cliente", ids próprios).
8. **Menu:** `salesContractsWashoutApproval`, Order **15** em `sales` (1–14 ocupados; o último é `storageTransactionsSales`, 14), ROLE_MENUS ADMIN com Id `9A3C5E71-2B4D-4F60-8E1A-7C9D0B2E4F53`.

## Review Focus

1. **Sinal invertido da fórmula.** Contrato acima do mercado cobra a diferença; mercado acima cobra só a multa.
   - Teste `Contract_above_market_charges_the_difference_plus_the_penalty` e `Market_above_contract_charges_only_the_penalty` (Task 1).
2. **Volume fixado já faturado (ledger) recusa**, mesmo com saldo físico e de fixação sobrando.
   - Teste `Fixed_volume_already_billed_is_refused` (Task 5).
3. **Aprovação com provisório composto** abaixo do novo valor recusa com V1 e não grava nada (status, título, provisório).
   - Teste `Approve_below_the_composed_amount_is_refused_and_nothing_changes` (Task 6).
4. **O título do washout é baixável direto e não entra na composição**, nem trava o encerramento.
   - Teste `Washout_receivable_settles_directly_and_is_not_composable` (Task 6).
5. **Gerador de provisórios desconta os washouts aprovados** em toda geração (reabertura e backlog), e o backlog conta `SkippedFullyWashedOut` na venda.
   - Testes `Sales_generator_discounts_approved_washouts` e `Fully_washed_sales_fixation_is_skipped_by_the_backlog` (Task 4).

---

## Task 1: Modelo — entidade, enum, colunas, origem 8, interceptor e migration

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Domain/Enums/SalesContractWashoutStatus.cs` |
| Criar | `SiagroB1.Domain/Entities/SalesContractWashout.cs` |
| Modificar | `SiagroB1.Domain/Entities/SalesContract.cs` (colunas, navegação, quatro saldos) |
| Modificar | `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs` |
| Modificar | `SiagroB1.Domain/Entities/ContractChangeLogFields.cs` (sobrecarga `DescribeWashout`) |
| Modificar | `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet e FKs, ao lado do bloco de `PurchaseContractWashout`, ~linha 488) |
| Modificar | `SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs` |
| Criar (gerado) | `SiagroB1.Migrations/AppContext/<timestamp>_AddSalesContractWashouts.cs` (+ `.Designer.cs`, snapshot) |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractWashoutEntityTests.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutTestData.cs` |
| Teste (modificar) | `SiagroB1.Application.Tests/Infra/FinishedContractMutationGuardInterceptorTests.cs` |

**Interfaces:**

- Produz:
  - `enum SalesContractWashoutStatus { InApproval = 0, Approved = 1, Rejected = 2, Reversed = 3 }`;
  - `SalesContractWashout` (tabela `SALES_CONTRACTS_WASHOUTS`) com `static decimal CalculateAmount(decimal marketPrice, decimal contractPrice, decimal fixedVolume, decimal penaltyAmount)` — **mesma ordem de parâmetros da compra**, sinal invertido;
  - `SalesContract.WashedOutVolume`, `SalesContract.WashedOutUnfixedVolume` (`DECIMAL(18,3) DEFAULT 0`), `SalesContract.Washouts`;
  - `FinancialDocumentOrigin.SalesContractWashout = 8`;
  - `ContractChangeLogFields.DescribeWashout(SalesContractWashout, string?)`;
  - `AppDbContext.SalesContractsWashouts`;
  - `SalesWashoutTestData` (sementes de teste, usadas nas Tasks 2–8).

- [ ] **Passo 0: baseline**

```bash
git -C C:/Projetos/SiagroB1/siagro-b1-backend branch --show-current   # feature/sales-contract-washout
dotnet test SiagroB1.Application.Tests
```

Anote a contagem de verdes.

- [ ] **Passo 1: sementes de teste**

`SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutTestData.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>
/// Sementes dos testes do washout de venda. Contrato de 1.000 KG, cliente C0001, preço 100; a fixação padrão é a
/// automática do preço fixo (1.000 @ 100). Mercado padrão 90: contrato acima do mercado, o cliente paga a diferença.
/// </summary>
internal static class SalesWashoutTestData
{
    public static SalesContract Contract(
        ContractType type = ContractType.Fixed,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 1_000m)
    {
        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume, status: status);
        contract.Code = "CV-001";
        contract.Type = type;
        contract.BranchCode = "01";
        contract.CardName = "CLIENTE TESTE";
        contract.StandardCurrency = CurrencyType.Brl;
        contract.StandardCashFlowDate = new DateTime(2026, 12, 31);
        contract.CreationDate = DateTime.Today;
        return contract;
    }

    public static SalesContractPriceFixation Fixation(
        SalesContract contract,
        decimal volume,
        decimal price = 100m,
        PriceFixationStatus status = PriceFixationStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contract.Key,
        FixationVolume = volume,
        FixationPrice = price,
        FinancialDueDate = new DateTime(2026, 12, 31),
        Status = status,
    };

    public static SalesContractWashout Washout(
        SalesContract contract,
        SalesContractPriceFixation? fixation = null,
        decimal fixedVolume = 0m,
        decimal unfixedVolume = 0m,
        SalesContractWashoutStatus status = SalesContractWashoutStatus.InApproval,
        int sequence = 1,
        decimal marketPrice = 90m,
        decimal penaltyAmount = 500m)
    {
        var contractPrice = fixation?.FixationPrice ?? 0m;

        return new SalesContractWashout
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            PriceFixationKey = fixedVolume > 0 ? fixation?.Key : null,
            Sequence = sequence,
            FixedVolume = fixedVolume,
            UnfixedVolume = unfixedVolume,
            ContractPrice = contractPrice,
            MarketPrice = marketPrice,
            PenaltyAmount = penaltyAmount,
            Amount = SalesContractWashout.CalculateAmount(marketPrice, contractPrice, fixedVolume, penaltyAmount),
            DueDate = new DateTime(2026, 10, 31),
            Reason = "Cliente desistiu",
            Status = status,
        };
    }

    /// <summary>Provisório a receber da fixação, no valor cheio.</summary>
    public static FinancialDocument Provisional(SalesContract contract, SalesContractPriceFixation fixation, string code = "FN-PROV-V1") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = contract.CardCode,
        BranchCode = "01",
        Direction = FinancialDirection.Receivable,
        Nature = FinancialDocumentNature.Provisional,
        Status = FinancialDocumentStatus.Open,
        Currency = CurrencyType.Brl,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = decimal.Round(fixation.FixationVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven),
        OriginType = FinancialDocumentOrigin.SalesContractPriceFixation,
        OriginKey = fixation.Key,
        OriginDocNumber = contract.Code,
        SalesContractKey = contract.Key,
    };

    /// <summary>Documento de saída confirmado de uma linha, com a alocação no ledger (o "faturado" da venda).</summary>
    public static (SalesInvoice Invoice, SalesContractAllocation Allocation) Billed(SalesContract contract, decimal volume)
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        var item = SalesContractsAllocationTestSupport.NewItem(invoice, contract.Key, releaseKey: null, volume);
        return (invoice, SalesContractsAllocationTestSupport.NewAllocation(contract.Key, item.Key!.Value, volume));
    }
}
```

> `SalesInvoiceItem.Key` é `Guid?` (o seed de firmes usa `line.Key!.Value`); se o compilador reclamar do `!.Value`, use `item.Key`. Leia `SalesContractsAllocationTestSupport.NewItem` antes.

- [ ] **Passo 2: testes que falham**

`SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractWashoutEntityTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §3.1 e §4.1: fórmula invertida e saldos que descontam o lavado.</summary>
public class SalesContractWashoutEntityTests
{
    [Fact]
    public void Contract_above_market_charges_the_difference_plus_the_penalty()
    {
        // (100 − 90) × 1.000 = 10.000 + 500 de multa
        Assert.Equal(10_500m, SalesContractWashout.CalculateAmount(90m, 100m, 1_000m, 500m));
    }

    [Fact]
    public void Market_above_contract_charges_only_the_penalty()
    {
        Assert.Equal(500m, SalesContractWashout.CalculateAmount(110m, 100m, 1_000m, 500m));
    }

    [Fact]
    public void Unfixed_only_washout_charges_only_the_penalty()
    {
        Assert.Equal(300m, SalesContractWashout.CalculateAmount(90m, 0m, 0m, 300m));
    }

    [Fact]
    public void The_difference_is_rounded_to_two_decimals()
    {
        // 0,005 × 333,333 = 1,666665 → 1,67
        Assert.Equal(1.67m, SalesContractWashout.CalculateAmount(2.5m, 2.505m, 333.333m, 0m));
    }

    [Fact]
    public void Origin_is_appended_with_a_fixed_number()
    {
        Assert.Equal(8, (int)FinancialDocumentOrigin.SalesContractWashout);
        Assert.Equal(3, (int)SalesContractWashoutStatus.Reversed);
    }

    [Fact]
    public void Washed_out_volume_reduces_the_physical_balance()
    {
        var contract = SalesWashoutTestData.Contract(totalVolume: 100_000m);
        contract.AllocatedVolume = 20_000m;
        contract.WashedOutVolume = 30_000m;

        Assert.Equal(50_000m, contract.AvaiableVolume);
    }

    [Fact]
    public void Washed_out_volume_reduces_both_balances_to_release()
    {
        var contract = SalesWashoutTestData.Contract(totalVolume: 100_000m);
        contract.WashedOutVolume = 10_000m;
        contract.SalesShipmentReleases.Add(SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 40_000m));

        Assert.Equal(50_000m, contract.TotalAvailableToRelease);
        Assert.Equal(50_000m, contract.TotalAvailableToReleaseWithoutProvisioning);
    }

    [Fact]
    public void Unfixed_washed_volume_is_no_longer_available_to_pricing()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined, totalVolume: 100_000m);
        contract.FixedVolume = 30_000m;
        contract.WashedOutUnfixedVolume = 20_000m;

        Assert.Equal(50_000m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public void Change_log_describes_code_volumes_amount_and_status()
    {
        var washout = new SalesContractWashout
        {
            Sequence = 2,
            FixedVolume = 1_000m,
            UnfixedVolume = 500m,
            Amount = 1_234.5m,
            Status = SalesContractWashoutStatus.InApproval,
        };

        Assert.Equal(
            "WO-2: 1.000,000 KG fixado + 500,000 KG não fixado, R$ 1.234,50 — Em aprovação",
            ContractChangeLogFields.DescribeWashout(washout, "KG"));
    }
}
```

Em `FinishedContractMutationGuardInterceptorTests.cs`, acrescente ao fim da classe (o `NewContext()` existente serve):

```csharp
    private static SalesContract NewSalesContract(ContractStatus status)
    {
        var contract = SalesContracts.SalesContractsAllocationTestSupport.NewContract(1_000m, status: status);
        contract.Code = "CV-001";
        return contract;
    }

    [Fact]
    public async Task AddingWashoutToFinishedSalesContract_Throws()
    {
        await using var ctx = NewContext();
        var sc = NewSalesContract(ContractStatus.Finished);
        ctx.SalesContracts.Add(sc);
        await ctx.SaveChangesAsync();

        ctx.SalesContractsWashouts.Add(new SalesContractWashout
        {
            Key = Guid.NewGuid(),
            SalesContractKey = sc.Key,
            Sequence = 1,
            UnfixedVolume = 10m,
            Reason = "falta",
        });

        await Assert.ThrowsAsync<DefaultException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task AddingWashoutToApprovedSalesContract_Succeeds()
    {
        await using var ctx = NewContext();
        var sc = NewSalesContract(ContractStatus.Approved);
        ctx.SalesContracts.Add(sc);
        await ctx.SaveChangesAsync();

        ctx.SalesContractsWashouts.Add(new SalesContractWashout
        {
            Key = Guid.NewGuid(),
            SalesContractKey = sc.Key,
            Sequence = 1,
            UnfixedVolume = 10m,
            Reason = "falta",
        });

        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.SalesContractsWashouts.CountAsync());
    }
```

O namespace do teste é `SiagroB1.Application.Tests.Infra`; `SalesContracts.SalesContractsAllocationTestSupport` resolve para `SiagroB1.Application.Tests.SalesContracts` (classe `internal`, mesmo assembly).

- [ ] **Passo 3: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractWashoutEntityTests|FullyQualifiedName~FinishedContractMutationGuardInterceptorTests"`

Esperado: não compila (`SalesContractWashout` não existe).

- [ ] **Passo 4: implementar o domínio**

`SiagroB1.Domain/Enums/SalesContractWashoutStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Ciclo do washout do contrato de venda. InApproval e Approved RESERVAM volume
/// (<c>SalesContract.WashedOutVolume</c>); Rejected e Reversed o devolvem. Os valores são
/// contrato com o banco e com o frontend — não renumere.
/// </summary>
public enum SalesContractWashoutStatus
{
    InApproval = 0,
    Approved = 1,
    Rejected = 2,
    Reversed = 3
}
```

`SiagroB1.Domain/Entities/SalesContractWashout.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Washout do contrato de venda (spec 2026-10-15): desistência registrada de parte ou de todo o volume NÃO
/// faturado. Tira o volume do saldo, reduz o provisório a receber da fixação e gera um título a receber do
/// cliente com a diferença de mercado e a multa. Espelho de <see cref="PurchaseContractWashout"/>.
/// </summary>
[Table("SALES_CONTRACTS_WASHOUTS")]
[Index(nameof(SalesContractKey), nameof(Sequence), IsUnique = true)]
public class SalesContractWashout : BaseEntity
{
    public Guid SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>Sequencial por contrato (1, 2, 3…). A tela exibe "WO-{Sequence}".</summary>
    public int Sequence { get; set; }

    /// <summary>Fixação cujo preço vale para o volume fixado. Nula quando <see cref="FixedVolume"/> é zero.</summary>
    public Guid? PriceFixationKey { get; set; }
    public virtual SalesContractPriceFixation? PriceFixation { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixedVolume { get; set; }

    /// <summary>Só em contrato a fixar (PAF); no preço fixo é sempre zero.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal UnfixedVolume { get; set; }

    /// <summary>Cópia do FixationPrice da fixação no momento do registro.</summary>
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal ContractPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal MarketPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PenaltyAmount { get; set; }

    /// <summary>Valor do título a receber. Ver <see cref="CalculateAmount"/>.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    /// <summary>Vencimento do título; obrigatório quando <see cref="Amount"/> é maior que zero.</summary>
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Reason { get; set; }

    public SalesContractWashoutStatus Status { get; set; } = SalesContractWashoutStatus.InApproval;

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ReversalReason { get; set; }

    /// <summary>Título a receber gerado na aprovação.</summary>
    public Guid? FinancialDocumentKey { get; set; }
    public virtual FinancialDocument? FinancialDocument { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// <c>round(max(contrato − mercado, 0) × volume fixado, 2) + multa</c>: sinal INVERTIDO em relação à compra. O
    /// cliente que desiste paga quando o mercado caiu abaixo do contrato; mercado acima vira zero e cobra-se só a
    /// multa — o cliente nunca recebe crédito. Mesma ordem de parâmetros de <see cref="PurchaseContractWashout.CalculateAmount"/>.
    /// </summary>
    public static decimal CalculateAmount(
        decimal marketPrice, decimal contractPrice, decimal fixedVolume, decimal penaltyAmount) =>
        decimal.Round(Math.Max(contractPrice - marketPrice, 0m) * fixedVolume, 2, MidpointRounding.ToEven)
        + decimal.Round(penaltyAmount, 2, MidpointRounding.ToEven);
}
```

`SalesContract.cs`:

- abaixo de `public ICollection<SalesContractChangeLog> ChangeLogs { get; set; } = [];` (~linha 156):

```csharp
    public ICollection<SalesContractWashout> Washouts { get; set; } = [];
```

- abaixo de `AllocatedVolume` (~linha 176):

```csharp
    /// <summary>
    /// Volume lavado (persistido, derivado): Σ (FixedVolume + UnfixedVolume) dos washouts InApproval
    /// + Approved. Recalculado exclusivamente por SalesContractsWashedOutVolumeService.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal WashedOutVolume { get; set; }

    /// <summary>
    /// Parte NÃO FIXADA de <see cref="WashedOutVolume"/>: volume a fixar de que o cliente desistiu e que, por isso,
    /// não pode mais ser fixado. Mesmo recálculo.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal WashedOutUnfixedVolume { get; set; }
```

- os quatro saldos (substitua as expressões, mantenha os comentários existentes e acrescente "Desconta o volume lavado (WashedOutVolume)."):

```csharp
    public decimal AvailableVolumeToPricing =>
        decimal.Round(TotalVolume - FixedVolume - WashedOutUnfixedVolume, 3, MidpointRounding.ToEven);

    public decimal AvaiableVolume =>
        decimal.Round(TotalVolume - AllocatedVolume - WashedOutVolume, 3, MidpointRounding.ToEven);

    public decimal TotalAvailableToRelease =>
        decimal.Round(TotalVolume - TotalShipmentReleases - WashedOutVolume, 2, MidpointRounding.ToEven);

    public decimal TotalAvailableToReleaseWithoutProvisioning =>
        decimal.Round(TotalVolume - TotalShipmentReleasesWithoutProvisioning - WashedOutVolume, 2, MidpointRounding.ToEven);
```

`FinancialDocumentOrigin.cs`, depois de `PurchaseFiscalReconciliation = 7`:

```csharp
    ,

    /// <summary>Título a receber do cliente gerado pela aprovação de um washout de venda (spec 2026-10-15).</summary>
    SalesContractWashout = 8
```

(acerte a vírgula na linha de `PurchaseFiscalReconciliation = 7`, sem a vírgula solta).

`ContractChangeLogFields.cs`, logo abaixo de `DescribeWashout(PurchaseContractWashout …)`:

```csharp
    /// <summary>Como o washout de venda aparece no log: mesmo formato do de compra.</summary>
    public static string DescribeWashout(SalesContractWashout washout, string? unitOfMeasureCode = null)
    {
        var unit = string.IsNullOrWhiteSpace(unitOfMeasureCode) ? "" : $" {unitOfMeasureCode}";

        return string.Format(
            PtBr,
            "WO-{0}: {1:N3}{2} fixado + {3:N3}{2} não fixado, R$ {4:N2} — {5}",
            washout.Sequence, washout.FixedVolume, unit, washout.UnfixedVolume, washout.Amount,
            DescribeWashoutStatus((PurchaseContractWashoutStatus)(int)washout.Status));
    }
```

> O cast reaproveita o switch de texto: os dois enums têm os mesmos valores (0–3) por contrato com o banco. Se preferir não fazer o cast, duplique o switch com `SalesContractWashoutStatus`.

- [ ] **Passo 5: infraestrutura**

`AppDbContext.cs`:

- DbSet, ao lado de `SalesContractsPriceFixations` (~linha 52):

```csharp
    public DbSet<SalesContractWashout> SalesContractsWashouts { get; set; }
```

- FKs, logo depois do bloco de `PurchaseContractWashout` (~linha 500):

```csharp
        // Washout de venda: mesmo desenho do de compra — Restrict para o contrato e para a fixação, NoAction para o título.
        modelBuilder.Entity<SalesContractWashout>()
            .HasOne(x => x.SalesContract).WithMany(x => x.Washouts)
            .HasForeignKey(x => x.SalesContractKey).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SalesContractWashout>()
            .HasOne(x => x.PriceFixation).WithMany()
            .HasForeignKey(x => x.PriceFixationKey).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SalesContractWashout>()
            .HasOne(x => x.FinancialDocument).WithMany()
            .HasForeignKey(x => x.FinancialDocumentKey).OnDelete(DeleteBehavior.NoAction);
```

`FinishedContractMutationGuardInterceptor.cs` — substitua o corpo inteiro da classe por:

```csharp
/// <summary>
/// Impede alterar (add/update/delete) sub-entidades vinculadas a um contrato encerrado
/// (<see cref="ContractStatus.Finished"/>). Compra: corretores, impostos, parâmetros de qualidade, fixações de preço
/// e washouts. Venda: washouts (spec 2026-10-15). Cobre todos os caminhos de mutação num único ponto.
/// </summary>
public class FinishedContractMutationGuardInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            var (purchaseKeys, salesKeys) = CollectContractKeys(eventData.Context);

            if (purchaseKeys.Count > 0 && await eventData.Context.Set<PurchaseContract>()
                    .AsNoTracking()
                    .AnyAsync(c => purchaseKeys.Contains(c.Key) && c.Status == ContractStatus.Finished, cancellationToken))
                throw Blocked();

            if (salesKeys.Count > 0 && await eventData.Context.Set<SalesContract>()
                    .AsNoTracking()
                    .AnyAsync(c => salesKeys.Contains(c.Key) && c.Status == ContractStatus.Finished, cancellationToken))
                throw Blocked();
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            var (purchaseKeys, salesKeys) = CollectContractKeys(eventData.Context);

            if (purchaseKeys.Count > 0 && eventData.Context.Set<PurchaseContract>()
                    .AsNoTracking()
                    .Any(c => purchaseKeys.Contains(c.Key) && c.Status == ContractStatus.Finished))
                throw Blocked();

            if (salesKeys.Count > 0 && eventData.Context.Set<SalesContract>()
                    .AsNoTracking()
                    .Any(c => salesKeys.Contains(c.Key) && c.Status == ContractStatus.Finished))
                throw Blocked();
        }

        return base.SavingChanges(eventData, result);
    }

    private static (HashSet<Guid> Purchase, HashSet<Guid> Sales) CollectContractKeys(DbContext context)
    {
        var purchase = new HashSet<Guid>();
        var sales = new HashSet<Guid>();

        // ⚠️ NÃO acrescente FinancialDocument a este switch, por mais que pareça "seguir o
        // padrão". O encerramento do contrato cancela o provisório no MESMO SaveChanges em que
        // grava Finished; incluído aqui, o próprio encerramento passaria a lançar
        // "Contrato encerrado: não é possível alterar dados vinculados ao contrato.", sem saída
        // pela tela — e qualquer baixa de documento de contrato encerrado também.
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // PurchaseContractComment está fora de propósito: comentário é anotação, não altera
            // valor nem saldo, e o requisito é justamente poder comentar contrato encerrado.
            // ContractDraft / ContractDraftSigner ficam FORA de propósito: minuta é documentação,
            // como anexo — um contrato encerrado pode ter aditivo ou distrato (spec 2026-09-21).
            Guid? purchaseKey = entry.Entity switch
            {
                PurchaseContractBroker b => b.PurchaseContractKey,
                PurchaseContractTax t => t.PurchaseContractKey,
                PurchaseContractQualityParameter q => q.PurchaseContractKey,
                PurchaseContractPriceFixation f => f.PurchaseContractKey,
                PurchaseContractWashout w => w.PurchaseContractKey,
                _ => null
            };

            if (purchaseKey.HasValue && purchaseKey.Value != Guid.Empty)
                purchase.Add(purchaseKey.Value);

            // Venda: só o washout (spec 2026-10-15). Os demais filhos de venda nunca passaram por esta guarda.
            if (entry.Entity is SalesContractWashout sw && sw.SalesContractKey != Guid.Empty)
                sales.Add(sw.SalesContractKey);
        }

        return (purchase, sales);
    }

    private static DefaultException Blocked() =>
        new("Contrato encerrado: não é possível alterar dados vinculados ao contrato.");
}
```

Mantenha os `using` do topo do arquivo.

- [ ] **Passo 6: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractWashoutEntityTests|FullyQualifiedName~FinishedContractMutationGuardInterceptorTests|FullyQualifiedName~PurchaseContractWashoutEntityTests"`

Esperado: PASS.

- [ ] **Passo 7: gerar a migration**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddSalesContractWashouts --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext
```

O `Up()` gerado deve conter **só**:

1. dois `AddColumn<decimal>` em `SALES_CONTRACTS` (`WashedOutVolume`, `WashedOutUnfixedVolume`), não nulos, default 0 — compare com `20260914194729_AddPurchaseContractWashouts.cs` (lá o `type` saiu `"DECIMAL(18,3)"`);
2. `CreateTable("SALES_CONTRACTS_WASHOUTS")` com FKs para `SALES_CONTRACTS` (Restrict), `SALES_CONTRACTS_PRICE_FIXATIONS` (Restrict) e `FINANCIAL_DOCUMENTS` (NoAction);
3. índices: único `(SalesContractKey, Sequence)`, `PriceFixationKey` e `FinancialDocumentKey`.

O `Down()` faz o inverso. Se aparecer qualquer outra coisa, é drift alheio: **pare e reporte**, não edite à mão. Acrescente acima da classe: `/// Washout do contrato de venda (spec 2026-10-15): tabela filha + volumes lavados persistidos.`

- [ ] **Passo 8: build, modelo pendente e suíte**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet test SiagroB1.Application.Tests
```

Esperado: build OK, "No changes have been made to the model" e a contagem do Passo 0 + os novos, todos verdes.

- [ ] **Passo 9: stage**

```bash
git add SiagroB1.Domain/Enums/SalesContractWashoutStatus.cs SiagroB1.Domain/Entities/SalesContractWashout.cs SiagroB1.Domain/Entities/SalesContract.cs SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs SiagroB1.Domain/Entities/ContractChangeLogFields.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/SalesContracts/Washouts SiagroB1.Application.Tests/Infra/FinishedContractMutationGuardInterceptorTests.cs
```

O controlador commita com:

```
feat(sales-contract): modelo do washout de venda

Registro filho SALES_CONTRACTS_WASHOUTS, espelho do washout de compra,
com a fórmula invertida: o cliente que desiste paga quando o mercado
caiu abaixo do contrato, nunca recebe crédito. O contrato ganha os
volumes lavados persistidos, e saldo, saldo a liberar e saldo a fixar
passam a descontá-los. Origem financeira nova SalesContractWashout (8).
O interceptor de contrato encerrado passa a barrar washout de venda.

DB: AddSalesContractWashouts

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 2: Volume lavado e consumidores de saldo

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashedOutVolumeService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsGetShipmentReleasesAvailableService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsGetReconciliationTargetsService.cs` (~linha 75) |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationCreateService.cs` (~linha 48) |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsTotalsService.cs` |
| Modificar | `SiagroB1.Domain/Dtos/SalesContractTotalsResponseDto.cs` |
| Modificar | `SiagroB1.Reports/Helpers/ContractPositionData.cs` |
| Modificar | `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registro do serviço novo) |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutBalanceConsumersTests.cs` |
| Teste (modificar) | `SiagroB1.Application.Tests/Reports/ContractPositionDataTests.cs` |

**Interfaces:**

- Produz `SalesContractsWashedOutVolumeService(AppDbContext)`:
  - `Task RecalculateAsync(SalesContract contract)` (não persiste);
  - `static Task<(decimal Total, decimal Unfixed)> ActiveVolumesAsync(AppDbContext, Guid contractKey, Guid? excludingWashoutKey = null)`;
  - `static Task<decimal> ActiveFixedVolumeAsync(AppDbContext, Guid fixationKey, Guid? excludingWashoutKey = null)`;
  - `static Task<decimal> ApprovedFixedVolumeAsync(AppDbContext, Guid fixationKey, Guid? excludingWashoutKey = null)`.
- Produz `ContractPositionData.SalesWashedOutQuery(AppDbContext, IQueryable<Guid>)`.
- `SalesContractTotalsResponseDto.WashedOutVolume`.

- [ ] **Passo 1: testes que falham**

`SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutBalanceConsumersTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §4: quem passa a descontar o lavado.</summary>
public class SalesWashoutBalanceConsumersTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<SalesContract> SeedAsync(SalesContract contract, params object[] children)
    {
        _db.Context.SalesContracts.Add(contract);
        foreach (var child in children) _db.Context.Add(child);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Recalculate_sums_only_active_washouts()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        await SeedAsync(contract,
            SalesWashoutTestData.Washout(contract, unfixedVolume: 100m, sequence: 1),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 50m, status: SalesContractWashoutStatus.Approved, sequence: 2),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 30m, status: SalesContractWashoutStatus.Rejected, sequence: 3),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 20m, status: SalesContractWashoutStatus.Reversed, sequence: 4));

        await new SalesContractsWashedOutVolumeService(_db.Context).RecalculateAsync(contract);

        Assert.Equal(150m, contract.WashedOutVolume);
        Assert.Equal(150m, contract.WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Approved_fixed_volume_ignores_in_approval_and_the_excluded_washout()
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        var approved = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Approved, sequence: 1);
        var pending = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 40m, sequence: 2);
        await SeedAsync(contract, fixation, approved, pending);

        Assert.Equal(100m, await SalesContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(140m, await SalesContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(40m, await SalesContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key, approved.Key));
    }

    [Fact]
    public async Task Fully_washed_contract_leaves_the_release_selector()
    {
        var contract = SalesWashoutTestData.Contract();
        contract.WashedOutVolume = 1_000m;
        await SeedAsync(contract);

        var service = new SalesContractsGetShipmentReleasesAvailableService(
            _db, NullLogger<SalesContractsGetShipmentReleasesAvailableService>.Instance);

        Assert.DoesNotContain(await service.Query().ToListAsync(), c => c.Key == contract.Key);
    }

    [Fact]
    public async Task Price_fixation_cannot_use_the_washed_unfixed_volume()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.WashedOutUnfixedVolume = 800m;
        await SeedAsync(contract, SalesWashoutTestData.Washout(contract, unfixedVolume: 800m));

        var service = new SalesContractsPriceFixationCreateService(
            _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
            TestNotificationOutbox.For(_db.Context), NullLogger<SalesContractsPriceFixationCreateService>.Instance);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(contract.Key,
            new SalesContractPriceFixation { FixationVolume = 300m, FixationPrice = 100m }, "tester"));

        Assert.Contains("Volume excede o saldo disponível para fixação", error.Message);
    }

    [Fact]
    public async Task Totals_expose_the_washed_volume()
    {
        var contract = SalesWashoutTestData.Contract();
        contract.WashedOutVolume = 250m;
        await SeedAsync(contract);

        var totals = await new SalesContractsTotalsService(_db.Context).GetTotals(contract.Key);

        Assert.Equal(250m, totals.WashedOutVolume);
    }
}
```

Em `ContractPositionDataTests.cs`, acrescente:

```csharp
    // Washout de venda (spec 2026-10-15): o saldo do relatório acompanha o AvaiableVolume, que agora desconta o lavado.
    [Fact]
    public async Task SalesWashout_ReducesTheBalanceLikeTheDomain()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = Sales("CV000001", total: 1_000m);
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesContractsWashouts.AddRange(
            new SiagroB1.Domain.Entities.SalesContractWashout
            {
                Key = Guid.NewGuid(), SalesContractKey = contract.Key, Sequence = 1, UnfixedVolume = 100m,
                Status = SalesContractWashoutStatus.Approved, Reason = "x",
            },
            new SiagroB1.Domain.Entities.SalesContractWashout
            {
                Key = Guid.NewGuid(), SalesContractKey = contract.Key, Sequence = 2, UnfixedVolume = 50m,
                Status = SalesContractWashoutStatus.Rejected, Reason = "x",
            });
        await Save(db);

        var position = (await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, new Request()))).Single();

        Assert.Equal(100m, position.WashedOut);
        Assert.Equal(900m, position.Balance);
    }
```

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesWashoutBalanceConsumersTests|FullyQualifiedName~ContractPositionDataTests"`

Esperado: não compila (`SalesContractsWashedOutVolumeService` não existe).

- [ ] **Passo 3: implementar**

`SalesContractsWashedOutVolumeService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Ponto ÚNICO das somas de washout de venda. <see cref="RecalculateAsync"/> é o único escritor de
/// <see cref="SalesContract.WashedOutVolume"/> e <see cref="SalesContract.WashedOutUnfixedVolume"/>. Espelho de
/// PurchaseContractsWashedOutVolumeService; os estáticos existem para quem não pode ganhar dependência no construtor
/// (encerramento, gerador de provisório). Todas as somas leem o BANCO: quem muda status salva antes.
/// </summary>
public class SalesContractsWashedOutVolumeService(AppDbContext context)
{
    /// <summary>Recalcula e atribui os dois agregados. NÃO persiste.</summary>
    public async Task RecalculateAsync(SalesContract contract)
    {
        var (total, unfixed) = await ActiveVolumesAsync(context, contract.Key);
        contract.WashedOutVolume = total;
        contract.WashedOutUnfixedVolume = unfixed;
    }

    /// <summary>Σ dos washouts ATIVOS (InApproval + Approved) do contrato.</summary>
    public static async Task<(decimal Total, decimal Unfixed)> ActiveVolumesAsync(
        AppDbContext context, Guid contractKey, Guid? excludingWashoutKey = null)
    {
        var rows = await context.SalesContractsWashouts
            .Where(w => w.SalesContractKey == contractKey
                        && (w.Status == SalesContractWashoutStatus.InApproval
                            || w.Status == SalesContractWashoutStatus.Approved)
                        && (excludingWashoutKey == null || w.Key != excludingWashoutKey))
            .Select(w => new { w.FixedVolume, w.UnfixedVolume })
            .ToListAsync();

        return (Round(rows.Sum(r => r.FixedVolume + r.UnfixedVolume)), Round(rows.Sum(r => r.UnfixedVolume)));
    }

    /// <summary>Volume fixado já reservado por washouts ATIVOS de uma fixação (trava de saldo).</summary>
    public static Task<decimal> ActiveFixedVolumeAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null) =>
        SumFixedAsync(context, fixationKey, excludingWashoutKey, onlyApproved: false);

    /// <summary>Volume fixado lavado por washouts APROVADOS de uma fixação (valor do provisório).</summary>
    public static Task<decimal> ApprovedFixedVolumeAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null) =>
        SumFixedAsync(context, fixationKey, excludingWashoutKey, onlyApproved: true);

    private static async Task<decimal> SumFixedAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey, bool onlyApproved)
    {
        var total = await context.SalesContractsWashouts
            .Where(w => w.PriceFixationKey == fixationKey
                        && (w.Status == SalesContractWashoutStatus.Approved
                            || (!onlyApproved && w.Status == SalesContractWashoutStatus.InApproval))
                        && (excludingWashoutKey == null || w.Key != excludingWashoutKey))
            .SumAsync(w => w.FixedVolume);

        return Round(total);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
```

`SalesContractsGetShipmentReleasesAvailableService.Query()`: no `Where`, troque `(p.TotalVolume - p.AllocatedVolume - …)` por `(p.TotalVolume - p.AllocatedVolume - p.WashedOutVolume - …)` e acrescente ao `<summary>`: "Desconta o volume lavado (WashedOutVolume), como a propriedade."

`SalesContractsGetReconciliationTargetsService` (~linha 75): `Balance = c.TotalVolume - c.AllocatedVolume - c.WashedOutVolume,` (o comentário acima já diz que é o espelho de `AvaiableVolume`).

`SalesContractsPriceFixationCreateService` (~linha 48):

```csharp
            // Volume não fixado lavado por washout não pode mais ser fixado (spec 2026-10-15, §4.2).
            if (contract.FixedVolume + contract.WashedOutUnfixedVolume + associationEntity.FixationVolume > contract.TotalVolume)
```

`SalesContractTotalsResponseDto`:

```csharp
    [JsonPropertyName("WashedOutVolume")]
    public decimal WashedOutVolume { get; set; }
```

`SalesContractsTotalsService.GetTotals`, no objeto devolvido: `WashedOutVolume = ctr.WashedOutVolume,`.

`ContractPositionData.cs`:

- abaixo de `PurchaseWashedOutQuery`:

```csharp
    /// <summary>Σ (fixado + não fixado) dos washouts de venda ATIVOS (em aprovação + aprovado).</summary>
    public static IQueryable<KeyedSum> SalesWashedOutQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.SalesContractsWashouts
            .Where(w => contractKeys.Contains(w.SalesContractKey)
                        && (w.Status == SalesContractWashoutStatus.InApproval
                            || w.Status == SalesContractWashoutStatus.Approved))
            .GroupBy(w => w.SalesContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(w => w.FixedVolume + w.UnfixedVolume) });
```

- em `SalesPositionsAsync`, depois de `var shortage = …`: `var washedOut = await ToDictionaryAsync(SalesWashedOutQuery(context, keys));`; dentro do `Select`, `var contractWashedOut = Round3(washedOut.GetValueOrDefault(c.Key));`, `WashedOut = contractWashedOut,` e:

```csharp
                // = SalesContract.AvaiableVolume com AllocatedVolume e WashedOutVolume recalculados.
                Balance = Round3(c.TotalVolume - contractDelivered - contractWashedOut),
```

`ServiceCollectionExtensions.cs`, perto de `services.AddScoped<SalesContractsCloseService>();` (~linha 288): `services.AddScoped<SalesContractsWashedOutVolumeService>();`.

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesWashoutBalanceConsumersTests|FullyQualifiedName~ContractPositionDataTests|FullyQualifiedName~ContractPosition"`

Esperado: PASS (o teste antigo `SalesBalance_EqualsTheDomainAfterARecalc` continua com `WashedOut` 0).

- [ ] **Passo 5: suíte e stage**

```bash
dotnet test SiagroB1.Application.Tests
git add SiagroB1.Application/Services/SalesContracts/SalesContractsWashedOutVolumeService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsGetShipmentReleasesAvailableService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsGetReconciliationTargetsService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationCreateService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsTotalsService.cs SiagroB1.Domain/Dtos/SalesContractTotalsResponseDto.cs SiagroB1.Reports/Helpers/ContractPositionData.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutBalanceConsumersTests.cs SiagroB1.Application.Tests/Reports/ContractPositionDataTests.cs
```

O controlador commita com:

```
feat(sales-contract): saldos do contrato de venda descontam o washout

Serviço único das somas de washout de venda (só ele escreve os
volumes lavados). Seletor de liberação, alvos da conciliação de saldos,
saldo a fixar, totais e o relatório de posição passam a descontar o
volume lavado, como o AvaiableVolume da entidade.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 3: Travas nos serviços existentes do contrato de venda

**Arquivos:**

| Ação | Caminho |
|---|---|
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsCloseService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationsCancelService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationDeleteService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWithdrawApprovalService.cs` |
| Modificar | `SiagroB1.Application/Services/SalesContracts/SalesContractsCancelService.cs` |
| Modificar | `SiagroB1.Commons/Resources/Resource.resx`, `Resource.pt-br.resx` (`SALES_CONTRACT_HAS_WASHOUTS`) |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutContractGuardsTests.cs` |
| Teste (modificar) | `SiagroB1.Application.Tests/Financials/SalesContractsCloseFinancialTests.cs` (linha 37, S10) |

**Interfaces:** nenhuma pública nova. Mensagens W21–W24, V3, V6.

- [ ] **Passo 1: testes que falham**

Em `SalesContractsCloseFinancialTests.cs`, linha 37, o texto esperado vira V3:

```csharp
        Assert.Equal("O título FN-PROV-V1 não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout.", error.Message);
```

`SalesWashoutContractGuardsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.6 e decisão 5 do plano: travas em serviços existentes.</summary>
public class SalesWashoutContractGuardsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsCloseService Close() =>
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context), TestNotificationOutbox.For(_db.Context),
            TestConfiguration.Empty(), NullLogger<SalesContractsCloseService>.Instance);

    private async Task SeedAsync(SalesContract contract, params object[] children)
    {
        _db.Context.SalesContracts.Add(contract);
        foreach (var child in children) _db.Context.Add(child);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Close_refuses_a_pending_washout()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        await SeedAsync(contract, SalesWashoutTestData.Washout(contract, unfixedVolume: 100m));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close().ExecuteAsync(contract.Key, "tester"));

        Assert.Equal("Contrato possui 1 washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de encerrar.", error.Message);
    }

    [Fact]
    public async Task Close_counts_approved_washouts_in_the_negative_balance_guard()
    {
        var contract = SalesWashoutTestData.Contract();
        var (invoice, allocation) = SalesWashoutTestData.Billed(contract, 900m);
        await SeedAsync(contract, invoice,
            SalesWashoutTestData.Washout(contract, unfixedVolume: 200m, status: SalesContractWashoutStatus.Approved));
        _db.Context.SalesContractsAllocations.Add(allocation);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("Contrato faturado além do volume contratado", error.Message);
        Assert.Contains("washout", error.Message);
    }

    [Fact]
    public async Task Close_of_paf_discounts_the_approved_washed_fixed_volume()
    {
        // 500 fixados e confirmados, 400 lavados (aprovados), 300 faturados: sobram 100 com preço para 300 entregues.
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = SalesWashoutTestData.Fixation(contract, 500m);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 400m, status: SalesContractWashoutStatus.Approved),
            SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 300m, shipped: 300m));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("Volume entregue sem preço fixado", error.Message);
        Assert.Contains("descontado o washout", error.Message);
    }

    [Fact]
    public async Task Reversing_a_fixation_with_an_active_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Approved));

        var service = new SalesContractsPriceFixationsCancelService(
            _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
            TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "tester"));

        Assert.Equal("Fixação possui washout registrado. Rejeite ou estorne o washout antes de estornar a fixação.", error.Message);
    }

    [Fact]
    public async Task Reversing_a_fixation_after_the_washout_was_reversed_is_allowed()
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Reversed));

        await new SalesContractsPriceFixationsCancelService(
                _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
                TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context))
            .ExecuteAsync(fixation.Key, "tester");

        Assert.Equal(PriceFixationStatus.InApproval,
            (await _db.Context.SalesContractsPriceFixations.AsNoTracking().SingleAsync(f => f.Key == fixation.Key)).Status);
    }

    [Fact]
    public async Task Deleting_a_fixation_with_any_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = SalesWashoutTestData.Fixation(contract, 300m, status: PriceFixationStatus.InApproval);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Rejected));

        var service = new SalesContractsPriceFixationDeleteService(
            _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
            NullLogger<SalesContractsPriceFixationDeleteService>.Instance);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "tester"));

        Assert.StartsWith("Fixação possui washout registrado (mesmo rejeitado ou estornado)", error.Message);
    }

    [Fact]
    public async Task Withdrawing_approval_with_any_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract();
        await SeedAsync(contract,
            SalesWashoutTestData.Washout(contract, unfixedVolume: 1m, status: SalesContractWashoutStatus.Rejected));

        var service = new SalesContractsWithdrawApprovalService(_db, new FakeStringLocalizer<Resource>(),
            TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context));

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ExecuteAsync(contract.Key, "tester"));

        Assert.Equal("SALES_CONTRACT_HAS_WASHOUTS", error.Message);
    }

    [Fact]
    public async Task Cancelling_the_contract_with_a_pending_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        await SeedAsync(contract, SalesWashoutTestData.Washout(contract, unfixedVolume: 100m));

        var service = new SalesContractsCancelService(_db, TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context), FinancialDocumentTestServices.CancellationGuard(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(contract.Key, null, "tester"));

        Assert.Equal("Contrato possui 1 washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de cancelar.", error.Message);
    }
}
```

> `BusinessException` e o `FakeStringLocalizer` que devolve a própria chave: confira em `Support/FakeStringLocalizer.cs` (linha 11) e no teste vizinho de retirada de aprovação. Se `BusinessException` não derivar de `ApplicationException`, o `ThrowsAsync<BusinessException>` acima é o correto.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesWashoutContractGuardsTests|FullyQualifiedName~SalesContractsCloseFinancialTests"`

Esperado: FAIL nos testes novos e no S10.

- [ ] **Passo 3: implementar**

`SalesContractsCloseService.cs`:

- em `ExecuteAsync`, antes de `if (contract.Type == ContractType.ToBeDetermined)`:

```csharp
        // ANTES da guarda de fixação: ela desconta o washout lavado ASSUMINDO que só sobrou Approved (espelho da compra).
        await GuardPendingWashoutsAsync(contract);
```

- `GuardPriceFixationAsync`: troque o bloco a partir de `var confirmedVolume = …` por:

```csharp
        var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
        var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);

        // Spec 2026-10-15 (washout de venda, §4.2): o volume fixado lavado saiu do contrato e não precisa de preço.
        // GuardPendingWashoutsAsync já rodou, então os ativos aqui são só os APROVADOS.
        var (washedOutTotal, washedOutUnfixed) = await SalesContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var pricedConfirmed = confirmedVolume - (washedOutTotal - washedOutUnfixed);

        if (pricedConfirmed < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado (descontado o washout): {pricedConfirmed:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
```

- `GuardNegativeBalanceAsync`, corpo:

```csharp
        var allocated = await SalesContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var (washedOut, _) = await SalesContractsWashedOutVolumeService.ActiveVolumesAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated - washedOut, 3, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N3}, " +
                $"alocado: {allocated:N3}, washout: {washedOut:N3}, saldo: {balance:N3}. " +
                "Ajuste as alocações na tela de conciliação de saldos antes de encerrar.");
```

- `GuardFinancialDocumentsAsync`, mensagem final (V3):

```csharp
            throw new ApplicationException(
                $"O título {unsettledCode} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout.");
```

- método novo:

```csharp
    /// <summary>
    /// Washout em aprovação reserva volume mas ainda pode ser rejeitado. Encerrar com ele pendente
    /// congelaria o contrato com um volume "lavado" que ninguém decidiu.
    /// </summary>
    private async Task GuardPendingWashoutsAsync(SalesContract contract)
    {
        var pendingCount = await context.SalesContractsWashouts
            .CountAsync(w => w.SalesContractKey == contract.Key
                             && w.Status == SalesContractWashoutStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} washout(s) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");
    }
```

O título a receber do washout **não** entra em `GuardFinancialDocumentsAsync`: a trava de firme filtra `OriginType == SalesInvoice` e a de quitação filtra `Nature == Provisional`. Não mude essas consultas.

`SalesContractsPriceFixationsCancelService.cs`, logo antes do comentário `// Spec 2026-10-15 (regra 18)`:

```csharp
        // Voltar a fixação para InApproval deixaria o washout sem preço de referência e com o
        // provisório já reduzido (spec 2026-10-15, washout de venda §5.6).
        var hasActiveWashout = await context.SalesContractsWashouts.AnyAsync(w =>
            w.PriceFixationKey == fixation.Key &&
            (w.Status == SalesContractWashoutStatus.InApproval || w.Status == SalesContractWashoutStatus.Approved));

        if (hasActiveWashout)
            throw new ApplicationException(
                "Fixação possui washout registrado. Rejeite ou estorne o washout antes de estornar a fixação.");
```

`SalesContractsPriceFixationDeleteService.cs`, depois da checagem de status `InApproval`:

```csharp
            // A FK washout → fixação é Restrict para washout de QUALQUER status. O InMemory não vê a violação; o 547
            // só aparece em banco real — a trava tem de vir daqui.
            var hasWashout = await context.SalesContractsWashouts
                .AnyAsync(w => w.PriceFixationKey == fixation.Key);

            if (hasWashout)
                throw new ApplicationException(
                    "Fixação possui washout registrado (mesmo rejeitado ou estornado) e não pode ser excluída. " +
                    "Rejeite a fixação em vez de excluí-la.");
```

`SalesContractsWithdrawApprovalService.cs`, depois de `if (contract.HasInvoices) …`:

```csharp
        // Em rascunho o contrato pode ser excluído, e a FK Restrict do washout (de QUALQUER status)
        // barraria a exclusão com erro 547.
        if (await db.Context.SalesContractsWashouts.AnyAsync(w => w.SalesContractKey == contract.Key))
            throw new BusinessException(resource["SALES_CONTRACT_HAS_WASHOUTS"]);
```

`SalesContractsCancelService.cs`, logo depois da checagem de movimentos:

```csharp
        // Washout em aprovação reserva volume mas ainda pode ser rejeitado — cancelar o contrato com ele pendente o
        // deixaria órfão (espelho de PurchaseContractsCancelService).
        var pendingWashoutCount = await db.Context.SalesContractsWashouts
            .CountAsync(w => w.SalesContractKey == contract.Key
                             && w.Status == SalesContractWashoutStatus.InApproval);

        if (pendingWashoutCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingWashoutCount} washout(s) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de cancelar.");
```

Recursos, ao lado de `PURCHASE_CONTRACT_HAS_WASHOUTS`:

- `Resource.resx`:

```xml
    <data name="SALES_CONTRACT_HAS_WASHOUTS" xml:space="preserve">
        <value>Sales contract has washouts; it cannot be withdrawn from approval.</value>
    </data>
```

- `Resource.pt-br.resx`:

```xml
    <data name="SALES_CONTRACT_HAS_WASHOUTS" xml:space="preserve">
        <value>Contrato de Venda possui washout registrado, não é possivel remover da aprovação.</value>
    </data>
```

Os `.Designer.cs` não têm a chave de compra; não os altere. Parseie os dois `.resx` com o comando de XML das Restrições.

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesWashoutContractGuardsTests|FullyQualifiedName~SalesContractsCloseFinancialTests|FullyQualifiedName~SalesContracts"`

Esperado: PASS.

- [ ] **Passo 5: suíte e stage**

```bash
dotnet test SiagroB1.Application.Tests
git add SiagroB1.Application/Services/SalesContracts/SalesContractsCloseService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationsCancelService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationDeleteService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsWithdrawApprovalService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsCancelService.cs SiagroB1.Commons/Resources/Resource.resx SiagroB1.Commons/Resources/Resource.pt-br.resx SiagroB1.Application.Tests/SalesContracts/Washouts/SalesWashoutContractGuardsTests.cs SiagroB1.Application.Tests/Financials/SalesContractsCloseFinancialTests.cs
```

O controlador commita com:

```
feat(sales-contract): travas de washout no encerramento, fixação e aprovação

Encerrar recusa washout em aprovação e desconta o lavado no saldo
negativo e no preço do PAF; o provisório não quitado aponta o washout
como saída da entrega a menos. Estornar ou excluir fixação, retirar a
aprovação e cancelar o contrato recusam com washout, como na compra:
a FK Restrict só estouraria em banco real.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 4: Financeiro — gerador, ajuste do provisório, título a receber e cancelamento

**Arquivos:**

| Ação | Caminho |
|---|---|
| Modificar | `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateService.cs` |
| Modificar | `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateBacklogService.cs` |
| Modificar | `SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs` |
| Modificar | `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateWashoutReceivableService.cs` |
| Modificar | `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesWashoutTests.cs` |

**Interfaces:**

- `FinancialDocumentsGenerateService.EnqueueForSalesFixationAsync(SalesContract, SalesContractPriceFixation, string userName, decimal? remainingVolume = null)` — os três chamadores atuais (aprovação do contrato, aprovação da fixação, reabertura) não mudam.
- `FinancialDocumentsAdjustProvisionalService.EnqueueForSalesFixationAsync(SalesContract, SalesContractPriceFixation, decimal remainingVolume, string userName)` e `const string SalesWashedOutCancellationReason = "Volume lavado por washout"`.
- `FinancialDocumentsGenerateWashoutReceivableService.EnqueueAsync(SalesContract, SalesContractWashout, string userName) : Task<FinancialDocument>`.

- [ ] **Passo 1: testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesWashoutTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.SalesContracts.Washouts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15 (washout de venda) §5.2, §5.5, §5.6 e §6: as peças financeiras do lado de venda.</summary>
public class FinancialDocumentsSalesWashoutTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateService Generate() => FinancialDocumentTestServices.Generate(_db.Context);

    private FinancialDocumentsAdjustProvisionalService Adjust() =>
        new(_db.Context, Generate(), new FinancialDocumentChangeLogService(_db.Context));

    private FinancialDocumentsGenerateWashoutReceivableService Receivable() => new(
        _db.Context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE" }));

    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedAsync(
        params (decimal FixedVolume, SalesContractWashoutStatus Status)[] washouts)
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        var sequence = 1;
        foreach (var (fixedVolume, status) in washouts)
            _db.Context.SalesContractsWashouts.Add(
                SalesWashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume, status: status, sequence: sequence++));
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    private IQueryable<FinancialDocument> Receivables() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(d => d.Direction == FinancialDirection.Receivable);

    [Fact]
    public async Task Sales_generator_discounts_approved_washouts()
    {
        var (contract, fixation) = await SeedAsync(
            (400m, SalesContractWashoutStatus.Approved),
            (100m, SalesContractWashoutStatus.InApproval),
            (50m, SalesContractWashoutStatus.Reversed));

        await Generate().EnqueueForSalesFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        // (1.000 − 400) × 100
        Assert.Equal(60_000m, Assert.Single(Receivables()).NetAmount);
    }

    [Fact]
    public async Task Sales_generator_writes_nothing_for_a_fully_washed_fixation()
    {
        var (contract, fixation) = await SeedAsync((1_000m, SalesContractWashoutStatus.Approved));

        await Generate().EnqueueForSalesFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(Receivables());
    }

    [Fact]
    public async Task Fully_washed_sales_fixation_is_skipped_by_the_backlog()
    {
        await SeedAsync((1_000m, SalesContractWashoutStatus.Approved));

        var result = await new FinancialDocumentsGenerateBacklogService(_db, Generate()).ExecuteAsync(
            branchCode: null, fromDate: DateTime.Today.AddYears(-1), toDate: DateTime.Today.AddDays(1),
            dryRun: false, userName: "tester");

        Assert.Equal(1, result.SkippedFullyWashedOut);
        Assert.Equal(0, result.Generated);
        Assert.Empty(Receivables());
    }

    [Fact]
    public async Task Adjust_reduces_the_sales_provisional_in_place_and_logs_it()
    {
        var (contract, fixation) = await SeedAsync();
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForSalesFixationAsync(contract, fixation, 600m, "tester");
        await _db.Context.SaveChangesAsync();

        var reloaded = Assert.Single(Receivables());
        Assert.Equal(60_000m, reloaded.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Open, reloaded.Status);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, _db.Context.FinancialDocumentChangeLogs.Single().Field);
    }

    [Fact]
    public async Task Adjust_to_zero_cancels_the_sales_provisional()
    {
        var (contract, fixation) = await SeedAsync();
        _db.Context.FinancialDocuments.Add(SalesWashoutTestData.Provisional(contract, fixation));
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForSalesFixationAsync(contract, fixation, 0m, "tester");
        await _db.Context.SaveChangesAsync();

        var cancelled = Assert.Single(Receivables());
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Volume lavado por washout", cancelled.CancellationReason);
    }

    [Fact]
    public async Task Adjust_below_the_composed_amount_is_refused()
    {
        var (contract, fixation) = await SeedAsync();
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        provisional.ComposedAmount = 70_000m;
        _db.Context.FinancialDocuments.Add(provisional);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Adjust().EnqueueForSalesFixationAsync(contract, fixation, 600m, "tester"));

        Assert.Equal(
            "O provisório FN-PROV-V1 já tem 70.000,00 composto: o washout o deixaria em 60.000,00. Desfaça a composição antes de aprovar.",
            error.Message);
    }

    [Fact]
    public async Task Adjust_without_an_open_provisional_generates_one_with_the_remaining_volume()
    {
        var (contract, fixation) = await SeedAsync();

        await Adjust().EnqueueForSalesFixationAsync(contract, fixation, 700m, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(70_000m, Assert.Single(Receivables()).NetAmount);
    }

    [Fact]
    public async Task Receivable_is_a_firm_from_the_sales_washout_origin()
    {
        var (contract, fixation) = await SeedAsync();
        var washout = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m);

        var document = await Receivable().EnqueueAsync(contract, washout, "tester");

        Assert.Equal(FinancialDirection.Receivable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Firm, document.Nature);
        Assert.Equal(FinancialDocumentOrigin.SalesContractWashout, document.OriginType);
        Assert.Equal(washout.Key, document.OriginKey);
        Assert.Equal(contract.Code, document.OriginDocNumber);
        Assert.Equal(contract.Key, document.SalesContractKey);
        Assert.Null(document.PurchaseContractKey);
        Assert.Equal(1_500m, document.NetAmount);   // (100 − 90) × 100 + 500
        Assert.Equal(new DateTime(2026, 10, 31), document.DueDate);
        Assert.False(document.IsSalesFirmReceivable);
        Assert.False(document.IsBlockedForSettlement);
    }

    [Fact]
    public async Task Washout_receivable_cannot_be_cancelled_on_the_financial_screen()
    {
        var (contract, fixation) = await SeedAsync();
        var document = await Receivable().EnqueueAsync(
            contract, SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m), "tester");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            FinancialDocumentTestServices.Cancel(_db.Context).ExecuteAsync(document.Key, "teste", "tester"));

        Assert.Equal(
            "Título gerado por washout não pode ser cancelado aqui. Estorne o washout no contrato de venda CV-001.",
            error.Message);
    }
}
```

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsSalesWashoutTests"`

Esperado: não compila (sobrecargas inexistentes).

- [ ] **Passo 3: implementar**

`FinancialDocumentsGenerateService.cs` — `using SiagroB1.Application.Services.SalesContracts;` e substitua `EnqueueForSalesFixationAsync` por:

```csharp
    /// <param name="remainingVolume">
    /// Volume a faturar já calculado pelo chamador — só o ajuste do washout de venda passa este valor (roda antes do
    /// SaveChanges que grava o washout aprovado). Nulo = FixationVolume menos os washouts APROVADOS da fixação, lidos
    /// do banco: é o que impede a reabertura e o backlog de ressuscitarem o valor cheio (spec 2026-10-15, §5.5).
    /// </param>
    public async Task EnqueueForSalesFixationAsync(
        SalesContract contract, SalesContractPriceFixation fixation, string userName,
        decimal? remainingVolume = null)
    {
        var volume = remainingVolume ?? fixation.FixationVolume -
            await SalesContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(context, fixation.Key);

        // Fixação inteiramente lavada: não há o que receber.
        if (volume <= 0) return;

        await EnqueueAsync(
            direction: FinancialDirection.Receivable,
            originType: FinancialDocumentOrigin.SalesContractPriceFixation,
            originKey: fixation.Key,
            contractCode: contract.Code,
            cardCode: contract.CardCode,
            branchCode: contract.BranchCode,
            currency: contract.StandardCurrency ?? CurrencyType.Brl,
            contractType: contract.Type,
            volume: volume,
            price: fixation.FixationPrice,
            fixationDueDate: fixation.FinancialDueDate,
            contractCashFlowDate: contract.StandardCashFlowDate,
            paymentTermsText: fixation.PaymentDetails ?? contract.PaymentTerms,
            purchaseContractKey: null,
            salesContractKey: contract.Key,
            userName: userName);
    }
```

`FinancialDocumentsGenerateBacklogService.ProcessSalesContractsAsync` — `using SiagroB1.Application.Services.SalesContracts;` e, logo depois de `if (alreadyGenerated) { result.SkippedAlreadyGenerated++; continue; }`:

```csharp
                // Espelho do F3 da compra: fixação de venda toda lavada por washout Approved não tem provisório ativo
                // e não gera nada; sem este corte, contaria como "Generated" e ficaria pendente para sempre no DryRun.
                var remaining = fixation.FixationVolume -
                    await SalesContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(db.Context, fixation.Key);

                if (remaining <= 0) { result.SkippedFullyWashedOut++; continue; }
```

`FinancialDocumentsAdjustProvisionalService.cs` — acrescente a constante e o método:

```csharp
    /// <summary>Spec 2026-10-15 (washout de venda, §5.2).</summary>
    public const string SalesWashedOutCancellationReason = "Volume lavado por washout";

    /// <summary>
    /// Ajuste do provisório a RECEBER da fixação de venda. Diferente da compra numa coisa: com a composição do firme a
    /// receber (sub-projeto A), o novo valor — zero quando o volume zera — não pode ficar abaixo do já composto ou
    /// baixado; a aprovação é recusada (V1) e o operador desfaz a composição antes.
    /// </summary>
    /// <param name="remainingVolume">Volume da fixação que continua a receber, já descontados os washouts aprovados.</param>
    public async Task EnqueueForSalesFixationAsync(
        SalesContract contract, SalesContractPriceFixation fixation, decimal remainingVolume, string userName)
    {
        var document = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.SalesContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (document is null)
        {
            // Cancelado por volume zero (e agora estornado) ou dado antigo sem provisório.
            if (remainingVolume > 0)
                await generateService.EnqueueForSalesFixationAsync(contract, fixation, userName, remainingVolume);
            return;
        }

        var newAmount = remainingVolume <= 0
            ? 0m
            : decimal.Round(remainingVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven);

        if (newAmount < Math.Max(document.ComposedAmount, document.SettledAmount))
            throw new ApplicationException(
                $"O provisório {document.Code} já tem {document.ComposedAmount.ToString("N2", PtBr)} composto: " +
                $"o washout o deixaria em {newAmount.ToString("N2", PtBr)}. Desfaça a composição antes de aprovar.");

        if (remainingVolume <= 0)
        {
            FinancialDocumentsCancelService.Cancel(document, SalesWashedOutCancellationReason, userName);
            return;
        }

        if (newAmount == document.NetAmount) return;

        changeLog.Register(
            document.Key,
            FinancialDocumentChangeLogFields.NetAmount,
            document.NetAmount.ToString("N2", PtBr),
            newAmount.ToString("N2", PtBr),
            userName);

        document.NetAmount = newAmount;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
    }
```

Atualize o `<summary>` da classe: "Ajusta NO LUGAR o provisório (a pagar ou a receber) de uma fixação cujo volume mudou por washout."

`FinancialDocumentsGenerateWashoutReceivableService.cs` — substitua o corpo da classe por:

```csharp
    /// <summary>Washout de compra: título a receber do PRODUTOR, origem PurchaseContractWashout.</summary>
    public Task<FinancialDocument> EnqueueAsync(
        PurchaseContract contract, PurchaseContractWashout washout, string userName) =>
        BuildAsync(FinancialDocumentOrigin.PurchaseContractWashout, washout.Key, washout.Amount, washout.DueDate,
            washout.Reason, contract.Code, contract.CardCode, contract.CardName, contract.BranchCode,
            contract.StandardCurrency, purchaseContractKey: contract.Key, salesContractKey: null, userName);

    /// <summary>
    /// Washout de venda (spec 2026-10-15, §5.2 e §6): título a receber do CLIENTE, origem SalesContractWashout. Não é
    /// firme de documento de saída (IsSalesFirmReceivable falso): baixa em dinheiro, direto, e fica fora da
    /// composição e da trava de encerramento.
    /// </summary>
    public Task<FinancialDocument> EnqueueAsync(
        SalesContract contract, SalesContractWashout washout, string userName) =>
        BuildAsync(FinancialDocumentOrigin.SalesContractWashout, washout.Key, washout.Amount, washout.DueDate,
            washout.Reason, contract.Code, contract.CardCode, contract.CardName, contract.BranchCode,
            contract.StandardCurrency, purchaseContractKey: null, salesContractKey: contract.Key, userName);

    private async Task<FinancialDocument> BuildAsync(
        FinancialDocumentOrigin originType, Guid washoutKey, decimal amount, DateTime? washoutDueDate, string? reason,
        string? contractCode, string cardCode, string? contractCardName, string? branchCode, CurrencyType? currency,
        Guid? purchaseContractKey, Guid? salesContractKey, string userName)
    {
        if (amount <= 0)
            throw new ApplicationException("Washout sem valor não gera título a receber.");

        var dueDate = washoutDueDate
                      ?? throw new ApplicationException("Informe o vencimento do título a receber do washout.");

        var existing = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == originType &&
            x.OriginKey == washoutKey &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (existing is not null) return existing;

        var partner = await businessPartnerService.GetByIdAsync(cardCode);
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            BranchCode = branchCode,
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner?.CardName ?? contractCardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency ?? CurrencyType.Brl,
            NetAmount = amount,
            SettledAmount = 0m,
            OriginType = originType,
            OriginKey = washoutKey,
            OriginDocNumber = contractCode,
            PurchaseContractKey = purchaseContractKey,
            SalesContractKey = salesContractKey,
            Comments = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        context.FinancialDocuments.Add(document);
        return document;
    }
```

> Confira os tipos antes de compilar: `PurchaseContract.Code`/`SalesContract.Code` são `string?`, `CardCode` é `string` (required), `StandardCurrency` é `CurrencyType?`. Ajuste a assinatura de `BuildAsync` se algum diferir. Atualize o `<summary>` da classe para citar as duas origens.

`FinancialDocumentsCancelService.ExecuteAsync`, logo depois da recusa de `PurchaseContractWashout`:

```csharp
        // Spec 2026-10-15 (washout de venda, §5.6): mesmo motivo do F4 — o washout aprovado ficaria reduzindo saldo e
        // provisório sem título nenhum por trás.
        if (document.OriginType == FinancialDocumentOrigin.SalesContractWashout)
            throw new ApplicationException(
                "Título gerado por washout não pode ser cancelado aqui. " +
                $"Estorne o washout no contrato de venda {document.OriginDocNumber}.");
```

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsSalesWashoutTests|FullyQualifiedName~FinancialDocumentsWashoutTests|FullyQualifiedName~FinancialDocumentsGenerateBacklogServiceTests|FullyQualifiedName~PurchaseContractsWashout"`

Esperado: PASS (os de compra provam que a sobrecarga não mudou o título de compra).

- [ ] **Passo 5: suíte e stage**

```bash
dotnet test SiagroB1.Application.Tests
git add SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateBacklogService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateWashoutReceivableService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesWashoutTests.cs
```

O controlador commita com:

```
feat(financial): provisório e título a receber do washout de venda

O gerador do provisório de venda desconta os washouts aprovados em toda
geração (aprovação, reabertura, backlog). O ajuste no lugar recusa
quando o provisório ficaria abaixo do já composto ou baixado, porque a
composição do firme a receber não pode sobrar. O título do washout de
venda nasce firme, origem 8, livre para baixa direta e fora da
composição; cancelá-lo na tela do financeiro é recusado.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 5: Guarda e criação do washout de venda

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutGuard.cs` |
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutCreateService.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractsWashoutCreateServiceTests.cs` |

**Interfaces:**

- `internal static class SalesContractsWashoutGuard { static Task ValidateAsync(AppDbContext, SalesContract, SalesContractWashout, SalesContractPriceFixation?) }` — exige o contrato com `SalesShipmentReleases` carregado e `washout.Amount` calculado; todas as somas excluem o próprio washout.
- `SalesContractsWashoutCreateService(AppDbContext, SalesContractsWashedOutVolumeService, SalesContractsChangeLogService, ContractNotificationOutboxService)`, `Task<SalesContractWashout> ExecuteAsync(Guid salesContractKey, SalesContractWashout input, string userName)`.

- [ ] **Passo 1: testes que falham**

`SalesContractsWashoutCreateServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.1.</summary>
public class SalesContractsWashoutCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsWashoutCreateService Service() => new(
        _db.Context,
        new SalesContractsWashedOutVolumeService(_db.Context),
        new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>Preço fixo: 1.000 KG com a fixação automática de 1.000 @ 100.</summary>
    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedFixAsync()
    {
        var contract = SalesWashoutTestData.Contract();
        contract.FixedVolume = 1_000m;
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    /// <summary>A fixar: 1.000 KG, 300 fixados @ 100.</summary>
    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedPafAsync()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.FixedVolume = 300m;
        var fixation = SalesWashoutTestData.Fixation(contract, 300m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    private static SalesContractWashout Input(
        Guid? fixationKey, decimal fixedVolume, decimal unfixedVolume = 0m,
        decimal marketPrice = 90m, decimal penaltyAmount = 500m, bool noDueDate = false,
        string? reason = "Cliente desistiu") => new()
    {
        PriceFixationKey = fixationKey,
        FixedVolume = fixedVolume,
        UnfixedVolume = unfixedVolume,
        MarketPrice = marketPrice,
        PenaltyAmount = penaltyAmount,
        DueDate = noDueDate ? null : new DateTime(2026, 10, 31),
        Reason = reason,
    };

    private async Task RefusesAsync(Guid contractKey, SalesContractWashout input, string fragment)
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(contractKey, input, "tester"));
        Assert.Contains(fragment, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creates_in_approval_with_inverted_amount_sequence_log_notification_and_reserved_volume()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m), "tester");

        Assert.Equal(SalesContractWashoutStatus.InApproval, washout.Status);
        Assert.Equal(1, washout.Sequence);
        Assert.Equal(100m, washout.ContractPrice);
        Assert.Equal(1_500m, washout.Amount);   // (100 − 90) × 100 + 500

        var reloaded = await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(100m, reloaded.WashedOutVolume);
        Assert.Equal(0m, reloaded.WashedOutUnfixedVolume);
        Assert.Equal(ContractChangeLogFields.Washout, _db.Context.SalesContractsChangeLogs.Single().Field);
        Assert.Equal(NotificationEventType.WashoutCreated, _db.Context.NotificationOutboxMessages.Single().EventType);
    }

    [Fact]
    public async Task Second_washout_gets_the_next_sequence()
    {
        var (contract, fixation) = await SeedFixAsync();
        await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m), "tester");

        var second = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 50m), "tester");

        Assert.Equal(2, second.Sequence);
        Assert.Equal(150m, (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
    }

    [Fact]
    public async Task Market_above_the_contract_charges_only_the_penalty()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m, marketPrice: 120m), "tester");

        Assert.Equal(500m, washout.Amount);
    }

    [Fact]
    public async Task Fixed_volume_already_billed_is_refused()
    {
        // PAF: 300 fixados e confirmados, 200 já faturados (ledger). Sobram 100 com preço não entregues, embora o
        // saldo físico (800) e o da fixação (300) aceitassem 150.
        var (contract, fixation) = await SeedPafAsync();
        var (invoice, allocation) = SalesWashoutTestData.Billed(contract, 200m);
        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(allocation);
        var tracked = await _db.Context.SalesContracts.SingleAsync(c => c.Key == contract.Key);
        tracked.AllocatedVolume = 200m;
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 150m),
            "Volume fixado excede o volume com preço ainda não entregue");
    }

    [Fact]
    public async Task Fixed_volume_within_the_unbilled_priced_volume_is_accepted()
    {
        var (contract, fixation) = await SeedPafAsync();
        var (invoice, allocation) = SalesWashoutTestData.Billed(contract, 200m);
        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(allocation);
        await _db.Context.SaveChangesAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m), "tester");

        Assert.Equal(100m, washout.FixedVolume);
    }

    [Fact]
    public async Task Paf_washes_fixed_and_unfixed_volumes()
    {
        var (contract, fixation) = await SeedPafAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m, unfixedVolume: 200m), "tester");

        Assert.Equal(1_500m, washout.Amount);   // o não fixado entra só com a multa
        var reloaded = await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(300m, reloaded.WashedOutVolume);
        Assert.Equal(200m, reloaded.WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Unfixed_only_washout_does_not_hold_the_fixation()
    {
        var (contract, fixation) = await SeedPafAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 0m, unfixedVolume: 100m), "tester");

        Assert.Null(washout.PriceFixationKey);
        Assert.Equal(0m, washout.ContractPrice);
    }

    [Fact]
    public async Task Unfixed_volume_above_the_balance_to_price_is_refused()
    {
        var (contract, _) = await SeedPafAsync();

        await RefusesAsync(contract.Key, Input(null, 0m, unfixedVolume: 701m), "Volume não fixado excede o saldo a fixar");
    }

    [Fact]
    public async Task Fixed_contract_refuses_unfixed_volume()
    {
        var (contract, fixation) = await SeedFixAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10m, unfixedVolume: 10m),
            "Contrato de preço fixo não tem volume a fixar");
    }

    [Fact]
    public async Task Volume_above_the_unreleased_balance_is_refused()
    {
        var (contract, fixation) = await SeedFixAsync();
        _db.Context.SalesShipmentReleases.Add(SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 950m));
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 100m), "excede o saldo não liberado");
    }

    [Fact]
    public async Task Amount_above_zero_requires_a_due_date()
    {
        var (contract, fixation) = await SeedFixAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 100m, noDueDate: true), "Informe o vencimento do título a receber.");
    }

    [Fact]
    public async Task Not_approved_contract_is_refused()
    {
        var contract = SalesWashoutTestData.Contract(status: ContractStatus.Finished);
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(null, 0m, unfixedVolume: 1m), "Contrato precisa estar aprovado para movimentar washout.");
    }

    [Fact]
    public async Task Missing_reason_is_refused()
    {
        var (contract, fixation) = await SeedFixAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10m, reason: "  "), "Informe o motivo do washout.");
    }
}
```

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsWashoutCreateServiceTests"`

Esperado: não compila.

- [ ] **Passo 3: implementar**

`SalesContractsWashoutGuard.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Travas do washout de venda, compartilhadas pela criação e pela aprovação (que revalida com o estado de agora).
/// Todas as somas EXCLUEM o próprio washout. Espelho de PurchaseContractsWashoutGuard, com uma diferença (spec
/// 2026-10-15, §5.1): na venda a entrega é o faturamento, então o "entregue" é o alocado no ledger.
///
/// Exige o contrato carregado com <c>SalesShipmentReleases</c> (saldo a liberar) e <c>washout.Amount</c> calculado.
/// </summary>
internal static class SalesContractsWashoutGuard
{
    public static async Task ValidateAsync(
        AppDbContext context,
        SalesContract contract,
        SalesContractWashout washout,
        SalesContractPriceFixation? fixation)
    {
        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato precisa estar aprovado para movimentar washout.");

        if (washout.FixedVolume < 0 || washout.UnfixedVolume < 0)
            throw new ApplicationException("Os volumes do washout não podem ser negativos.");

        var total = washout.FixedVolume + washout.UnfixedVolume;
        if (total <= 0)
            throw new ApplicationException("Informe o volume do washout.");

        if (washout.MarketPrice < 0)
            throw new ApplicationException("O preço de mercado não pode ser negativo.");

        if (washout.PenaltyAmount < 0)
            throw new ApplicationException("A multa não pode ser negativa.");

        if (string.IsNullOrWhiteSpace(washout.Reason))
            throw new ApplicationException("Informe o motivo do washout.");

        if (washout.Reason.Length > 500)
            throw new ApplicationException("O motivo do washout deve ter no máximo 500 caracteres.");

        if (contract.Type != ContractType.ToBeDetermined && washout.UnfixedVolume > 0)
            throw new ApplicationException(
                "Contrato de preço fixo não tem volume a fixar: informe só o volume fixado.");

        var (activeTotal, activeUnfixed) = await SalesContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key, washout.Key);

        if (washout.FixedVolume > 0)
        {
            if (fixation is null)
                throw new ApplicationException("Informe a fixação de preço do volume fixado.");

            if (fixation.SalesContractKey != contract.Key)
                throw new ApplicationException("A fixação informada não pertence a este contrato.");

            if (fixation.Status != PriceFixationStatus.Confirmed)
                throw new ApplicationException("A fixação informada não está confirmada.");

            var washedFromFixation = await SalesContractsWashedOutVolumeService
                .ActiveFixedVolumeAsync(context, fixation.Key, washout.Key);
            var fixationBalance = fixation.FixationVolume - washedFromFixation;

            if (washout.FixedVolume > fixationBalance)
                throw new ApplicationException(
                    $"Volume fixado excede o saldo da fixação. Disponível: {fixationBalance:N3}, " +
                    $"solicitado: {washout.FixedVolume:N3}.");

            // Volume fixado ainda não entregue = confirmado − faturado (ledger RECALCULADO, não o AllocatedVolume
            // persistido) − lavado fixado ativo. Lavar volume já faturado sumiria com o preço daquela mercadoria.
            var confirmedVolume = await new SalesContractsFixedVolumeService(context).ConfirmedVolumeAsync(contract.Key);
            var billedVolume = await SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(context, contract.Key);
            var washedFixed = activeTotal - activeUnfixed;
            var pricedUndelivered = confirmedVolume - billedVolume - washedFixed;

            if (washout.FixedVolume > pricedUndelivered)
                throw new ApplicationException(
                    $"Volume fixado excede o volume com preço ainda não entregue. Disponível: {pricedUndelivered:N3}, " +
                    $"solicitado: {washout.FixedVolume:N3}.");
        }

        if (washout.UnfixedVolume > 0)
        {
            var availableToPricing = contract.TotalVolume - contract.FixedVolume - activeUnfixed;
            if (washout.UnfixedVolume > availableToPricing)
                throw new ApplicationException(
                    $"Volume não fixado excede o saldo a fixar. Disponível: {availableToPricing:N3}, " +
                    $"solicitado: {washout.UnfixedVolume:N3}.");
        }

        var physical = decimal.Round(contract.TotalVolume - contract.AllocatedVolume - activeTotal, 3, MidpointRounding.ToEven);
        if (total > physical)
            throw new ApplicationException(
                $"Volume do washout excede o saldo físico do contrato. Disponível: {physical:N3}, " +
                $"solicitado: {total:N3}.");

        var toRelease = decimal.Round(contract.TotalVolume - contract.TotalShipmentReleases - activeTotal, 3, MidpointRounding.ToEven);
        if (total > toRelease)
            throw new ApplicationException(
                $"Volume do washout excede o saldo não liberado do contrato. Disponível: {toRelease:N3}, " +
                $"solicitado: {total:N3}. Encerre ou cancele a liberação antes.");

        if (washout.Amount > 0 && washout.DueDate is null)
            throw new ApplicationException("Informe o vencimento do título a receber.");
    }
}
```

`SalesContractsWashoutCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Registra um washout de venda em aprovação. Já RESERVA o volume (WashedOutVolume). Exposto como a OData action
/// <c>SalesContractsWashoutCreate</c>. Espelho de PurchaseContractsWashoutCreateService.
/// </summary>
public class SalesContractsWashoutCreateService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task<SalesContractWashout> ExecuteAsync(
        Guid salesContractKey, SalesContractWashout input, string userName)
    {
        var contract = await context.SalesContracts
                           .Include(x => x.SalesShipmentReleases)
                           .FirstOrDefaultAsync(x => x.Key == salesContractKey)
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        var fixedVolume = decimal.Round(input.FixedVolume, 3, MidpointRounding.ToEven);

        // Washout só de volume não fixado não prende fixação: sem isso, bloquearia à toa o estorno da fixação.
        var fixation = fixedVolume > 0 && input.PriceFixationKey is { } fixationKey
            ? await context.SalesContractsPriceFixations.FirstOrDefaultAsync(x => x.Key == fixationKey)
              ?? throw new NotFoundException("Fixação de preço não encontrada.")
            : null;

        var washout = new SalesContractWashout
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            PriceFixationKey = fixation?.Key,
            FixedVolume = fixedVolume,
            UnfixedVolume = decimal.Round(input.UnfixedVolume, 3, MidpointRounding.ToEven),
            ContractPrice = fixation?.FixationPrice ?? 0m,
            MarketPrice = input.MarketPrice,
            PenaltyAmount = decimal.Round(input.PenaltyAmount, 2, MidpointRounding.ToEven),
            DueDate = input.DueDate?.Date,
            Reason = input.Reason?.Trim(),
            Status = SalesContractWashoutStatus.InApproval,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName,
            ApprovedBy = null,
            CanceledBy = null,
        };

        washout.Amount = SalesContractWashout.CalculateAmount(
            washout.MarketPrice, washout.ContractPrice, washout.FixedVolume, washout.PenaltyAmount);

        await SalesContractsWashoutGuard.ValidateAsync(context, contract, washout, fixation);

        var lastSequence = await context.SalesContractsWashouts
            .Where(w => w.SalesContractKey == contract.Key)
            .MaxAsync(w => (int?)w.Sequence);
        washout.Sequence = (lastSequence ?? 0) + 1;

        context.SalesContractsWashouts.Add(washout);

        // Recalcula pelo banco (sem o novo) e soma o novo, que ainda só está rastreado. O RowVersion do contrato e o
        // índice único (contrato, sequência) barram o registro concorrente.
        await washedOutVolumeService.RecalculateAsync(contract);
        contract.WashedOutVolume += washout.FixedVolume + washout.UnfixedVolume;
        contract.WashedOutUnfixedVolume += washout.UnfixedVolume;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            null,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            userName);

        notificationOutbox.Register(contract, NotificationEventType.WashoutCreated, userName);

        await context.SaveChangesAsync();

        return washout;
    }
}
```

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsWashoutCreateServiceTests"`

Esperado: PASS.

- [ ] **Passo 5: suíte e stage**

```bash
dotnet test SiagroB1.Application.Tests
git add SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutGuard.cs SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutCreateService.cs SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractsWashoutCreateServiceTests.cs
```

O controlador commita com:

```
feat(sales-contract): registrar washout no contrato de venda

O washout nasce em aprovação e já reserva o volume. As travas são as da
compra, com o "entregue" lido do ledger de alocações, porque na venda a
entrega é o faturamento: volume fixado já faturado não pode ser lavado.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 6: Aprovar e rejeitar

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutApprovalService.cs` |
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutRejectService.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractsWashoutApprovalServiceTests.cs` |

**Interfaces:**

- `SalesContractsWashoutApprovalService(AppDbContext, SalesContractsWashedOutVolumeService, SalesContractsChangeLogService, ContractNotificationOutboxService, FinancialDocumentsAdjustProvisionalService, FinancialDocumentsGenerateWashoutReceivableService)`, `Task ExecuteAsync(Guid washoutKey, string? comments, string approvedBy)`.
- `SalesContractsWashoutRejectService(AppDbContext, SalesContractsWashedOutVolumeService, SalesContractsChangeLogService, ContractNotificationOutboxService)`, `Task ExecuteAsync(Guid washoutKey, string? comments, string rejectedBy)`.

- [ ] **Passo 1: testes que falham**

`SalesContractsWashoutApprovalServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.2 e §5.3.</summary>
public class SalesContractsWashoutApprovalServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsWashoutApprovalService Approval() => new(
        _db.Context,
        new SalesContractsWashedOutVolumeService(_db.Context),
        new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context),
        new FinancialDocumentsAdjustProvisionalService(
            _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context)),
        new FinancialDocumentsGenerateWashoutReceivableService(
            _db.Context, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE" })));

    private SalesContractsWashoutRejectService Reject() => new(
        _db.Context,
        new SalesContractsWashedOutVolumeService(_db.Context),
        new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>
    /// Preço fixo 1.000 KG; fixação automática 1.000 @ 100 com provisório de 100.000; um washout em aprovação de
    /// <paramref name="fixedVolume"/> a mercado 90 + multa 500. Conta CX01 para a baixa.
    /// </summary>
    private async Task<(SalesContract Contract, SalesContractWashout Washout, FinancialDocument Provisional)> SeedAsync(
        decimal fixedVolume = 100m, decimal marketPrice = 90m, decimal penaltyAmount = 500m, decimal composedAmount = 0m)
    {
        var contract = SalesWashoutTestData.Contract();
        contract.FixedVolume = 1_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        provisional.ComposedAmount = composedAmount;
        var washout = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume,
            marketPrice: marketPrice, penaltyAmount: penaltyAmount);

        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, washout, provisional);
    }

    private Task<FinancialDocument> ReloadDocumentAsync(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);

    private Task<SalesContractWashout> ReloadAsync(Guid key) =>
        _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == key);

    private IQueryable<FinancialDocument> WashoutTitles() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(d => d.OriginType == FinancialDocumentOrigin.SalesContractWashout);

    [Fact]
    public async Task Approve_marks_approved_and_records_the_approver()
    {
        var (_, washout, _) = await SeedAsync();

        await Approval().ExecuteAsync(washout.Key, "ok", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(SalesContractWashoutStatus.Approved, reloaded.Status);
        Assert.Equal("diretoria", reloaded.ApprovedBy);
        Assert.Equal("ok", reloaded.ApprovalComments);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutApproved);
    }

    [Fact]
    public async Task Approve_reduces_the_fixation_provisional_in_place()
    {
        var (_, washout, provisional) = await SeedAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var reloaded = await ReloadDocumentAsync(provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Open, reloaded.Status);
        Assert.Equal(90_000m, reloaded.NetAmount);
    }

    [Fact]
    public async Task Approve_generates_the_receivable_and_links_it()
    {
        var (contract, washout, _) = await SeedAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var title = Assert.Single(WashoutTitles());
        Assert.Equal(FinancialDocumentNature.Firm, title.Nature);
        Assert.Equal(FinancialDirection.Receivable, title.Direction);
        Assert.Equal(1_500m, title.NetAmount);
        Assert.Equal(contract.Key, title.SalesContractKey);
        Assert.Equal(title.Key, (await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_without_amount_generates_no_receivable()
    {
        var (_, washout, _) = await SeedAsync(marketPrice: 120m, penaltyAmount: 0m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Empty(WashoutTitles());
        Assert.Null((await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_washing_the_whole_fixation_cancels_the_provisional()
    {
        var (_, washout, provisional) = await SeedAsync(fixedVolume: 1_000m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var reloaded = await ReloadDocumentAsync(provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, reloaded.Status);
        Assert.Equal("Volume lavado por washout", reloaded.CancellationReason);
    }

    [Fact]
    public async Task Approve_below_the_composed_amount_is_refused_and_nothing_changes()
    {
        // Provisório de 100.000 com 95.000 compostos; o washout de 100 o levaria a 90.000.
        var (_, washout, provisional) = await SeedAsync(composedAmount: 95_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Equal(
            "O provisório FN-PROV-V1 já tem 95.000,00 composto: o washout o deixaria em 90.000,00. Desfaça a composição antes de aprovar.",
            error.Message);
        Assert.Equal(SalesContractWashoutStatus.InApproval, (await ReloadAsync(washout.Key)).Status);
        Assert.Equal(100_000m, (await ReloadDocumentAsync(provisional.Key)).NetAmount);
        Assert.Empty(WashoutTitles());
    }

    [Fact]
    public async Task Washout_receivable_settles_directly_and_is_not_composable()
    {
        var (_, washout, provisional) = await SeedAsync();
        await Approval().ExecuteAsync(washout.Key, null, "diretoria");
        var title = Assert.Single(WashoutTitles());

        var composeError = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsComposeService(_db, new FinancialDocumentChangeLogService(_db.Context))
                .ExecuteAsync(provisional.Key, [title.Key], "ana"));
        Assert.Equal($"O título {title.Code} não é firme nem devolução de documento de saída.", composeError.Message);

        await new FinancialDocumentsSettleService(_db)
            .ExecuteAsync(title.Key, "CX01", 1_500m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await ReloadDocumentAsync(title.Key)).Status);
    }

    [Fact]
    public async Task Approve_revalidates_against_the_current_state()
    {
        var (contract, washout, _) = await SeedAsync();
        _db.Context.SalesShipmentReleases.Add(SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 950m));
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Contains("excede o saldo não liberado", error.Message);
    }

    [Fact]
    public async Task Approving_twice_is_refused()
    {
        var (_, washout, _) = await SeedAsync();
        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Equal("Só é possível aprovar washout em aprovação. Status atual: Approved.", error.Message);
    }

    [Fact]
    public async Task Reject_returns_the_volume_and_requires_a_reason()
    {
        var (contract, washout, provisional) = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reject().ExecuteAsync(washout.Key, " ", "diretoria"));
        Assert.Equal("Informe o motivo da rejeição.", error.Message);

        await Reject().ExecuteAsync(washout.Key, "sem respaldo", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(SalesContractWashoutStatus.Rejected, reloaded.Status);
        Assert.Equal("sem respaldo", reloaded.ApprovalComments);
        Assert.Equal(0m, (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key)).WashedOutVolume);
        Assert.Equal(100_000m, (await ReloadDocumentAsync(provisional.Key)).NetAmount);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutRejected);
    }
}
```

> A mensagem do compose (`FinancialDocumentsComposeService.EnsureComposable`) é escolhida pelo `SalesContractKey` do provisório: na venda vale `… não é firme nem devolução de documento de saída.`. Se o compose recusar antes por outra regra (ex.: provisório sem composto a baixar), leia `FinancialDocumentsComposeService.ExecuteAsync` e ajuste só a asserção da mensagem, mantendo a de que recusa.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsWashoutApprovalServiceTests"`

Esperado: não compila.

- [ ] **Passo 3: implementar**

`SalesContractsWashoutApprovalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Aprova o washout de venda: reduz o provisório a receber da fixação (no lugar, recusando abaixo do já composto)
/// e gera o título a receber do cliente. O volume já estava reservado desde o registro.
/// </summary>
public class SalesContractsWashoutApprovalService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsGenerateWashoutReceivableService receivableGenerator)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string approvedBy)
    {
        if (comments != null && comments.Length > 500)
            throw new ApplicationException("O comentário deve ter no máximo 500 caracteres.");

        var washout = await context.SalesContractsWashouts
                          .Include(x => x.SalesContract).ThenInclude(c => c!.SalesShipmentReleases)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != SalesContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível aprovar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.SalesContract
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        await SalesContractsWashoutGuard.ValidateAsync(context, contract, washout, washout.PriceFixation);

        // ANTES de qualquer mutação: o ajuste é também a trava V1 (provisório composto), e recusar aqui não deixa
        // nada rastreado para trás. Antes da transação também porque o gerador busca número por Dapper.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            // O banco ainda não vê este washout como aprovado: soma os OUTROS e subtrai este.
            var otherApproved = await SalesContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForSalesFixationAsync(
                contract, fixation, fixation.FixationVolume - otherApproved - washout.FixedVolume, approvedBy);
        }

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = SalesContractWashoutStatus.Approved;
        washout.ApprovedBy = approvedBy;
        washout.ApprovedAt = DateTime.Now;
        washout.ApprovalComments = comments?.Trim();
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = approvedBy;

        if (washout.Amount > 0)
        {
            var receivable = await receivableGenerator.EnqueueAsync(contract, washout, approvedBy);
            washout.FinancialDocumentKey = receivable.Key;
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            approvedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutApproved, approvedBy);

        // Salva o status ANTES de recalcular (a soma lê o banco).
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
```

> Diferença deliberada de ordem em relação à compra: o ajuste do provisório vem **antes** de mudar o status, para que a recusa V1 não deixe o washout marcado como aprovado no change tracker (o teste "nothing changes" relê com `AsNoTracking`, mas um `SaveChanges` posterior no mesmo escopo gravaria o status sujo).

`SalesContractsWashoutRejectService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>Rejeita o washout de venda em aprovação e devolve o volume reservado ao saldo.</summary>
public class SalesContractsWashoutRejectService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string rejectedBy)
    {
        if (string.IsNullOrWhiteSpace(comments))
            throw new ApplicationException("Informe o motivo da rejeição.");

        if (comments.Length > 500)
            throw new ApplicationException("O motivo deve ter no máximo 500 caracteres.");

        var washout = await context.SalesContractsWashouts
                          .Include(x => x.SalesContract)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != SalesContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível rejeitar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.SalesContract
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        await using var transaction = await context.Database.BeginTransactionAsync();

        washout.Status = SalesContractWashoutStatus.Rejected;
        washout.ApprovalComments = comments.Trim();
        washout.CanceledBy = rejectedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = rejectedBy;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            rejectedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutRejected, rejectedBy);

        // Salva ANTES de recalcular: recalcular antes somaria o rejeitado de volta.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
```

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsWashoutApprovalServiceTests"`

Esperado: PASS.

- [ ] **Passo 5: suíte e stage**

```bash
dotnet test SiagroB1.Application.Tests
git add SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutApprovalService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutRejectService.cs SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractsWashoutApprovalServiceTests.cs
```

O controlador commita com:

```
feat(sales-contract): aprovar e rejeitar washout de venda

A aprovação revalida as travas, reduz no lugar o provisório a receber
da fixação e gera o título do cliente, baixável direto. Se o provisório
já tiver composto mais do que o novo valor, a aprovação é recusada antes
de qualquer gravação: o operador desfaz a composição primeiro. A
rejeição devolve o volume ao saldo.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 7: Estornar

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutReverseService.cs` |
| Criar | `SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutsGetService.cs` |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractsWashoutReverseServiceTests.cs` |

**Interfaces:**

- `SalesContractsWashoutReverseService(AppDbContext, SalesContractsWashedOutVolumeService, SalesContractsChangeLogService, ContractNotificationOutboxService, FinancialDocumentsAdjustProvisionalService, FinancialDocumentsCancelService)`, `Task ExecuteAsync(Guid washoutKey, string? reason, string reversedBy)`. Motivo do título: `PurchaseContractsWashoutReverseService.ReceivableCancellationReason` (W26).
- `SalesContractsWashoutsGetService(AppDbContext)`: `QueryByContract(Guid)`, `QueryPending()`, `QueryByKey(Guid)`, todos `IQueryable<SalesContractWashout>` `AsNoTracking`, sem Include.

- [ ] **Passo 1: testes que falham**

`SalesContractsWashoutReverseServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.3/5.4.</summary>
public class SalesContractsWashoutReverseServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsAdjustProvisionalService Adjust() => new(
        _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context));

    private SalesContractsWashoutApprovalService Approval() => new(
        _db.Context, new SalesContractsWashedOutVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context), Adjust(),
        new FinancialDocumentsGenerateWashoutReceivableService(
            _db.Context, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE" })));

    private SalesContractsWashoutReverseService Reverse() => new(
        _db.Context, new SalesContractsWashedOutVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context), Adjust(), FinancialDocumentTestServices.Cancel(_db.Context));

    /// <summary>Washout de <paramref name="fixedVolume"/> registrado e APROVADO pelo serviço real.</summary>
    private async Task<(SalesContract Contract, SalesContractWashout Washout, FinancialDocument Provisional)> ApprovedAsync(
        decimal fixedVolume = 100m)
    {
        var contract = SalesWashoutTestData.Contract();
        contract.FixedVolume = 1_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        var washout = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");
        return (contract, washout, provisional);
    }

    private Task<FinancialDocument> ReloadDocumentAsync(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);

    [Fact]
    public async Task Reverse_cancels_the_title_restores_the_provisional_and_returns_the_volume()
    {
        var (contract, washout, provisional) = await ApprovedAsync();
        var titleKey = (await _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == washout.Key)).FinancialDocumentKey!.Value;

        await Reverse().ExecuteAsync(washout.Key, "lançado errado", "ana");

        var reloaded = await _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == washout.Key);
        Assert.Equal(SalesContractWashoutStatus.Reversed, reloaded.Status);
        Assert.Equal("lançado errado", reloaded.ReversalReason);

        var title = await ReloadDocumentAsync(titleKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, title.Status);
        Assert.Equal("Washout estornado", title.CancellationReason);

        Assert.Equal(100_000m, (await ReloadDocumentAsync(provisional.Key)).NetAmount);
        Assert.Equal(0m, (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key)).WashedOutVolume);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutReversed);
    }

    [Fact]
    public async Task Reverse_after_a_full_washout_generates_a_new_provisional()
    {
        var (_, washout, provisional) = await ApprovedAsync(fixedVolume: 1_000m);
        Assert.Equal(FinancialDocumentStatus.Canceled, (await ReloadDocumentAsync(provisional.Key)).Status);

        await Reverse().ExecuteAsync(washout.Key, "lançado errado", "ana");

        var active = await _db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesContractPriceFixation && d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();
        Assert.Equal(100_000m, Assert.Single(active).NetAmount);
    }

    [Fact]
    public async Task Reverse_is_refused_when_the_title_has_a_settlement()
    {
        var (_, washout, _) = await ApprovedAsync();
        var title = await _db.Context.FinancialDocuments.SingleAsync(d => d.OriginType == FinancialDocumentOrigin.SalesContractWashout);
        title.SettledAmount = 100m;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, "x", "ana"));

        Assert.Equal($"O documento {title.Code} possui baixas. Estorne a baixa antes.", error.Message);
        Assert.Equal(SalesContractWashoutStatus.Approved,
            (await _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == washout.Key)).Status);
    }

    [Fact]
    public async Task Reverse_on_a_finished_contract_asks_to_reopen()
    {
        var (contract, washout, _) = await ApprovedAsync();
        var tracked = await _db.Context.SalesContracts.SingleAsync(c => c.Key == contract.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, "x", "ana"));

        Assert.Equal("Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.", error.Message);
    }

    [Fact]
    public async Task Only_approved_washouts_can_be_reversed()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var washout = SalesWashoutTestData.Washout(contract, unfixedVolume: 10m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, "x", "ana"));

        Assert.Equal("Só é possível estornar washout aprovado. Status atual: InApproval.", error.Message);
    }

    [Fact]
    public async Task Reverse_requires_a_reason()
    {
        var (_, washout, _) = await ApprovedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, null, "ana"));

        Assert.Equal("Informe o motivo do estorno.", error.Message);
    }
}
```

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsWashoutReverseServiceTests"`

Esperado: não compila.

- [ ] **Passo 3: implementar**

`SalesContractsWashoutReverseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Estorna um washout de venda aprovado: cancela o título a receber, restaura o provisório da fixação (o gerador
/// cria um novo se ele tinha sido cancelado por volume zero) e devolve o volume ao saldo. Recusado se o título já
/// teve baixa ou se o contrato não está aprovado.
/// </summary>
public class SalesContractsWashoutReverseService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsCancelService financialDocumentsCancel)
{
    public async Task ExecuteAsync(Guid washoutKey, string? reason, string reversedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        if (reason.Length > 500)
            throw new ApplicationException("O motivo deve ter no máximo 500 caracteres.");

        var washout = await context.SalesContractsWashouts
                          .Include(x => x.SalesContract)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != SalesContractWashoutStatus.Approved)
            throw new ApplicationException(
                $"Só é possível estornar washout aprovado. Status atual: {washout.Status}.");

        var contract = washout.SalesContract
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato precisa estar aprovado para estornar washout.");

        // Primeiro o título: é a trava de baixa, e precisa falhar ANTES de qualquer mutação.
        if (washout.FinancialDocumentKey is { } receivableKey)
            await financialDocumentsCancel.EnqueueCancelByKeyAsync(
                receivableKey, PurchaseContractsWashoutReverseService.ReceivableCancellationReason, reversedBy);

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = SalesContractWashoutStatus.Reversed;
        washout.ReversalReason = reason.Trim();
        washout.CanceledBy = reversedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = reversedBy;

        // ANTES da transação: se o provisório tinha sido cancelado por volume zero, o gerador busca número por Dapper.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            var stillWashed = await SalesContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForSalesFixationAsync(
                contract, fixation, fixation.FixationVolume - stillWashed, reversedBy);
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            reversedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutReversed, reversedBy);

        // Salva ANTES de recalcular: recalcular antes somaria o estornado de volta.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
```

`SalesContractsWashoutsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Leituras do washout de venda. Devolve IQueryable sem Include: o [EnableQuery] compõe $expand/$select
/// (SalesContract, PriceFixation, FinancialDocument).
/// </summary>
public class SalesContractsWashoutsGetService(AppDbContext context)
{
    public IQueryable<SalesContractWashout> QueryByContract(Guid contractKey) =>
        context.SalesContractsWashouts.Where(x => x.SalesContractKey == contractKey).AsNoTracking();

    /// <summary>Fila de aprovação: washouts de venda em aprovação de todos os contratos.</summary>
    public IQueryable<SalesContractWashout> QueryPending() =>
        context.SalesContractsWashouts
            .Where(x => x.Status == SalesContractWashoutStatus.InApproval)
            .AsNoTracking();

    public IQueryable<SalesContractWashout> QueryByKey(Guid key) =>
        context.SalesContractsWashouts.Where(x => x.Key == key).AsNoTracking();
}
```

- [ ] **Passo 4: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsWashoutReverseServiceTests"`

Esperado: PASS.

- [ ] **Passo 5: suíte e stage**

```bash
dotnet test SiagroB1.Application.Tests
git add SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutReverseService.cs SiagroB1.Application/Services/SalesContracts/SalesContractsWashoutsGetService.cs SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractsWashoutReverseServiceTests.cs
```

O controlador commita com:

```
feat(sales-contract): estornar washout de venda

O estorno cancela o título a receber (recusado se já teve baixa),
restaura o provisório da fixação pela mesma fórmula — recriando-o se
tinha sido cancelado por volume zero — e devolve o volume ao saldo.
Contrato encerrado pede reabertura antes.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 8: Superfície OData, DI e menu da aprovação

**Arquivos:**

| Ação | Caminho |
|---|---|
| Criar | `SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutCreateController.cs` |
| Criar | `SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutApprovalController.cs` |
| Criar | `SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutRejectController.cs` |
| Criar | `SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutReverseController.cs` |
| Criar | `SiagroB1.Web/Controllers/SalesContractsWashoutsController.cs` |
| Modificar | `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (EntitySet ~linha 39; actions depois de `SalesContractsPriceFixationCancel`, ~linha 1249) |
| Modificar | `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` |
| Criar (gerado) | `SiagroB1.Migrations/CommonContext/<timestamp>_AddSalesContractWashoutApprovalMenu.cs` (+ `.Designer.cs`) |
| Teste (criar) | `SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractWashoutWebTests.cs` |

**Interfaces:**

- Actions: `SalesContractsWashoutCreate(SalesContractKey: Guid, Washout: SalesContractWashout)`, `SalesContractsWashoutApproval(Key, Comments?)`, `SalesContractsWashoutReject(Key, Comments?)`, `SalesContractsWashoutReverse(Key, Reason?)`. Nas três últimas, `Key` é a chave do **washout**.
- GET: `odata/SalesContracts({key})/Washouts`, `odata/SalesContractsWashouts` (fila `InApproval`), `odata/SalesContractsWashouts({key})`.

- [ ] **Passo 1: testes que falham**

`SalesContractWashoutWebTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Actions.SalesContracts;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

public class SalesContractWashoutWebTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_entity_set_is_exposed()
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet("SalesContractsWashouts"));
    }

    [Fact]
    public void The_contract_exposes_the_washed_volumes_and_the_navigation()
    {
        var contractType = BuildModel().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "SalesContract");

        Assert.NotNull(contractType.FindProperty("WashedOutVolume"));
        Assert.NotNull(contractType.FindProperty("WashedOutUnfixedVolume"));
        Assert.NotNull(contractType.FindProperty("Washouts"));
    }

    [Theory]
    [InlineData("SalesContractsWashoutCreate", "SalesContractKey,Washout")]
    [InlineData("SalesContractsWashoutApproval", "Key,Comments")]
    [InlineData("SalesContractsWashoutReject", "Key,Comments")]
    [InlineData("SalesContractsWashoutReverse", "Key,Reason")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public async Task The_pending_queue_lists_only_washouts_in_approval()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsWashouts.AddRange(
            SalesWashoutTestData.Washout(contract, unfixedVolume: 1m, sequence: 1),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 1m, status: SalesContractWashoutStatus.Approved, sequence: 2));
        await _db.Context.SaveChangesAsync();

        var pending = new SalesContractsWashoutsGetService(_db.Context).QueryPending().ToList();

        Assert.Equal(SalesContractWashoutStatus.InApproval, Assert.Single(pending).Status);
    }

    [Fact]
    public async Task Get_by_key_returns_a_single_result_so_expand_works()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var washout = SalesWashoutTestData.Washout(contract, unfixedVolume: 1m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var result = new SalesContractsWashoutsController(new SalesContractsWashoutsGetService(_db.Context))
            .GetByKey(washout.Key);

        var single = Assert.IsType<SingleResult<SalesContractWashout>>(result);
        Assert.Equal(washout.Key, Assert.Single(single.Queryable).Key);
    }

    [Fact]
    public async Task A_business_refusal_returns_an_odata_error_payload()
    {
        var context = _db.Context;
        var controller = new SalesContractsWashoutRejectController(new SalesContractsWashoutRejectService(
            context, new SalesContractsWashedOutVolumeService(context), new SalesContractsChangeLogService(context),
            TestNotificationOutbox.For(context)))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.RejectAsync(new ODataActionParameters { ["Key"] = Guid.NewGuid(), ["Comments"] = null });

        Assert.IsType<BadRequestODataResult>(result);   // "Informe o motivo da rejeição."
    }
}
```

> O último teste segue `PurchaseContracts/Washouts/WashoutActionErrorPayloadTests.cs`; confira lá o nome do método do controller de rejeição de compra e use o mesmo nome no de venda.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractWashoutWebTests"`

Esperado: não compila.

- [ ] **Passo 3: controllers**

`SalesContractsWashoutCreateController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Actions.PurchaseContracts;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsWashoutCreateController(
    SalesContractsWashoutCreateService service) : ODataController
{
    [HttpPost("odata/SalesContractsWashoutCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ODataActionParameters vem NULO quando o corpo não casa com o EDM.
            if (parameters is null
                || !parameters.TryGetValue("SalesContractKey", out var keyObj) || keyObj is not Guid contractKey)
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("Washout", out var washoutObj) || washoutObj is not SalesContractWashout washout)
                return BadRequest("Missing washout payload");

            await service.ExecuteAsync(contractKey, washout, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

`SalesContractsWashoutApprovalController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Web.Actions.PurchaseContracts;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsWashoutApprovalController(
    SalesContractsWashoutApprovalService service) : ODataController
{
    [HttpPost("odata/SalesContractsWashoutApproval")]
    public async Task<IActionResult> ApproveAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

            // Parâmetro string do OData chega null: nunca .ToString() direto.
            parameters.TryGetValue("Comments", out var commentsObj);

            await service.ExecuteAsync(key, commentsObj as string, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

`SalesContractsWashoutRejectController.cs`: igual ao de aprovação, com `SalesContractsWashoutRejectService`, rota `odata/SalesContractsWashoutReject` e o nome de método do controller de rejeição de compra (confira em `PurchaseContractsWashoutRejectController.cs`; o teste do Passo 1 usa `RejectAsync`).

`SalesContractsWashoutReverseController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Web.Actions.PurchaseContracts;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsWashoutReverseController(
    SalesContractsWashoutReverseService service) : ODataController
{
    [HttpPost("odata/SalesContractsWashoutReverse")]
    public async Task<IActionResult> ReverseAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

            parameters.TryGetValue("Reason", out var reasonObj);

            await service.ExecuteAsync(key, reasonObj as string, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

`SalesContractsWashoutsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Leitura do washout de venda. Toda mutação é OData action (SalesContractsWashout*). As rotas são declaradas à mão:
/// navegação e GET por chave dão 404 sem isso.
/// </summary>
public class SalesContractsWashoutsController(SalesContractsWashoutsGetService getService) : ODataController
{
    [HttpGet("odata/SalesContracts({key:guid})/Washouts")]
    [HttpGet("odata/SalesContracts/{key:guid}/Washouts")]
    [EnableQuery]
    public ActionResult<IQueryable<SalesContractWashout>> GetWashouts([FromRoute] Guid key) =>
        Ok(getService.QueryByContract(key));

    /// <summary>Fila da tela "Aprovação de Washouts" do grupo Vendas.</summary>
    [HttpGet("odata/SalesContractsWashouts")]
    [EnableQuery]
    public ActionResult<IQueryable<SalesContractWashout>> GetPending() =>
        Ok(getService.QueryPending());

    /// <summary>SingleResult sobre IQueryable: materializado, o $expand seria aceito e voltaria nulo.</summary>
    [HttpGet("odata/SalesContractsWashouts({key:guid})")]
    [HttpGet("odata/SalesContractsWashouts/{key:guid}")]
    [EnableQuery]
    public SingleResult<SalesContractWashout> GetByKey([FromRoute] Guid key) =>
        SingleResult.Create(getService.QueryByKey(key));
}
```

- [ ] **Passo 4: EDM e DI**

`ODataConfigurations.cs`:

- ao lado de `modelBuilder.EntitySet<PurchaseContractWashout>("PurchaseContractsWashouts");`:

```csharp
        modelBuilder.EntitySet<SalesContractWashout>("SalesContractsWashouts");
```

- depois do bloco `salesPriceFixationCancel` (~linha 1249):

```csharp
        // Washout do contrato de venda (spec 2026-10-15). A criação recebe a entidade inteira; nas demais, Key é a
        // chave do WASHOUT, não a do contrato.
        var salesWashoutCreate = modelBuilder.Action("SalesContractsWashoutCreate");
        salesWashoutCreate.Parameter<Guid>("SalesContractKey");
        salesWashoutCreate.EntityParameter<SalesContractWashout>("Washout");
        salesWashoutCreate.Returns<IActionResult>();

        var salesWashoutApproval = modelBuilder.Action("SalesContractsWashoutApproval");
        salesWashoutApproval.Parameter<Guid>("Key");
        salesWashoutApproval.Parameter<string>("Comments").Nullable = true;
        salesWashoutApproval.Returns<IActionResult>();

        var salesWashoutReject = modelBuilder.Action("SalesContractsWashoutReject");
        salesWashoutReject.Parameter<Guid>("Key");
        salesWashoutReject.Parameter<string>("Comments").Nullable = true;
        salesWashoutReject.Returns<IActionResult>();

        var salesWashoutReverse = modelBuilder.Action("SalesContractsWashoutReverse");
        salesWashoutReverse.Parameter<Guid>("Key");
        salesWashoutReverse.Parameter<string>("Reason").Nullable = true;
        salesWashoutReverse.Returns<IActionResult>();
```

`ServiceCollectionExtensions.cs`, ao lado de `services.AddScoped<SalesContractsWashedOutVolumeService>();` (Task 2):

```csharp
        services.AddScoped<SalesContractsWashoutCreateService>();
        services.AddScoped<SalesContractsWashoutApprovalService>();
        services.AddScoped<SalesContractsWashoutRejectService>();
        services.AddScoped<SalesContractsWashoutReverseService>();
        services.AddScoped<SalesContractsWashoutsGetService>();
```

(`FinancialDocumentsGenerateWashoutReceivableService` e `FinancialDocumentsAdjustProvisionalService` já estão registrados.)

- [ ] **Passo 5: rodar e ver passar**

Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractWashoutWebTests|FullyQualifiedName~PurchaseContractWashoutWebTests"`

Esperado: PASS.

- [ ] **Passo 6: migration de menu (CommonContext)**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddSalesContractWashoutApprovalMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o CommonContext
```

O `Up()`/`Down()` gerados devem sair **vazios** (o snapshot não muda). Se vier qualquer operação, pare e reporte. Preencha:

```csharp
    /// <summary>
    /// Menu da fila de aprovação de washouts do contrato de venda, no grupo "Vendas" (spec 2026-10-15). Order 15
    /// porque 1 a 14 já estão ocupados em "sales" (14 é Romaneios de Embarque).
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddSalesContractWashoutApprovalMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "salesContractsWashoutApproval", "Aprovação de Washouts", "sap-icon://folder-blank", true, false, 15, "sales" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "9A3C5E71-2B4D-4F60-8E1A-7C9D0B2E4F53", "ADMIN", "salesContractsWashoutApproval" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "9A3C5E71-2B4D-4F60-8E1A-7C9D0B2E4F53");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "salesContractsWashoutApproval");
        }
    }
```

Antes de gravar, confira que nenhuma migration nova de `CommonContext` (depois de `20261014010729`) já usa a Order 15 em `sales`: `grep -n '"sales"' SiagroB1.Migrations/CommonContext/*.cs`.

- [ ] **Passo 7: build, modelo pendente e suíte**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet test SiagroB1.Application.Tests
```

Esperado: build OK, "No changes" nos dois contextos, todos verdes.

- [ ] **Passo 8: stage**

```bash
git add SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutCreateController.cs SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutApprovalController.cs SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutRejectController.cs SiagroB1.Web/Actions/SalesContracts/SalesContractsWashoutReverseController.cs SiagroB1.Web/Controllers/SalesContractsWashoutsController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Migrations/CommonContext SiagroB1.Application.Tests/SalesContracts/Washouts/SalesContractWashoutWebTests.cs
```

O controlador commita com:

```
feat(sales-contract): actions OData e menu da aprovação de washouts de venda

Expõe o washout de venda (leitura por contrato, fila e chave) e as
actions de registrar, aprovar, rejeitar e estornar, com erro OData de
verdade para a tela mostrar a mensagem de negócio. O menu "Aprovação de
Washouts" entra no grupo Vendas, só para ADMIN, com a Key igual à rota.

DB: AddSalesContractWashoutApprovalMenu

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 9 (frontend): seção "Washouts" no contrato de venda

**Arquivos (em `C:\Projetos\SiagroB1\siagro-b1-frontend\webapp`):**

| Ação | Caminho |
|---|---|
| Modificar | `model/ServerRoutes.ts` (rotas das actions) |
| Modificar | `helpers/WashoutHelpers.ts` (`calculateSalesWashoutAmount`) |
| Modificar | `model/formatter.ts` (`formatFinancialOrigin`) |
| Criar | `view/salesContracts/fragments/SalesContractWashouts.fragment.xml` |
| Criar | `view/salesContracts/fragments/SalesContractWashoutDialog.fragment.xml` |
| Modificar | `view/salesContracts/Detail.view.xml` (total "Washout" no cabeçalho e seção) |
| Modificar | `controller/salesContracts/SalesContractsBaseController.ts` |
| Teste (criar) | `test/unit/helpers/WashoutHelpers.qunit.ts` (+ import em `test/unit/unitTests.qunit.ts`) |
| Teste (modificar) | `test/unit/model/formatter.qunit.ts` |

**Interfaces:**

- `calculateSalesWashoutAmount(marketPrice, contractPrice, fixedVolume, penaltyAmount): number` — espelho de `SalesContractWashout.CalculateAmount`.
- Handlers no controller de venda: `onOpenWashoutDialog`, `onCloseWashoutDialog`, `onWashoutValuesChange`, `onConfirmWashout`, `onReverseWashout`, `onCloseWashoutReverseDialog`, `onConfirmWashoutReverse`.
- Reaproveita: `types/PurchaseContractWashout.ts` (`WashoutDialogState`, `WashoutFixationOption`, `WashoutReverseState`), `formatNumberPtBr`, `formatter.formatWashoutCode/formatWashoutStatus/stateWashoutStatus`, fragmento `siagrob1.view.purchaseContracts.fragments.WashoutReverseDialog`.

- [ ] **Passo 1: testes QUnit que falham**

`test/unit/helpers/WashoutHelpers.qunit.ts`:

```ts
import { calculateSalesWashoutAmount, calculateWashoutAmount } from "siagrob1/helpers/WashoutHelpers";

QUnit.module("WashoutHelpers - prévia do valor do washout");

QUnit.test("venda: contrato acima do mercado cobra a diferença mais a multa", function (assert) {
	assert.strictEqual(calculateSalesWashoutAmount(90, 100, 1000, 500), 10500);
});

QUnit.test("venda: mercado acima do contrato cobra só a multa", function (assert) {
	assert.strictEqual(calculateSalesWashoutAmount(110, 100, 1000, 500), 500);
});

QUnit.test("compra continua com o sinal da compra", function (assert) {
	assert.strictEqual(calculateWashoutAmount(2.75, 2.5, 10000, 1000), 3500);
});
```

Em `unitTests.qunit.ts`, acrescente `import "./helpers/WashoutHelpers.qunit";`.

Em `formatter.qunit.ts`, no teste `"natureza Devolução, origem da conciliação e campo Composição do log"`:

```ts
	assert.strictEqual(formatter.formatFinancialOrigin("SalesContractWashout"), "Washout de contrato de venda");
```

- [ ] **Passo 2: helpers, rotas e formatter**

`helpers/WashoutHelpers.ts`, depois de `calculateWashoutAmount`:

```ts
/**
 * Espelho de SalesContractWashout.CalculateAmount (spec 2026-10-15), só para a prévia do diálogo da VENDA:
 * round(max(contrato - mercado, 0) x volume fixado, 2) + round(multa, 2). Sinal invertido em relação à compra.
 */
export function calculateSalesWashoutAmount(
  marketPrice: number,
  contractPrice: number,
  fixedVolume: number,
  penaltyAmount: number
): number {
  const difference = Math.max(contractPrice - marketPrice, 0) * fixedVolume;
  return roundTo2(difference) + roundTo2(penaltyAmount);
}
```

`model/ServerRoutes.ts`, depois do bloco `purchaseContractsWashout*`:

```ts
  // Washout do contrato de venda (spec 2026-10-15). Nas actions de decisão e de estorno, Key é a chave do WASHOUT.
  salesContractsWashoutCreate: '/SalesContractsWashoutCreate(...)',
  salesContractsWashoutApproval: '/SalesContractsWashoutApproval(...)',
  salesContractsWashoutReject: '/SalesContractsWashoutReject(...)',
  salesContractsWashoutReverse: '/SalesContractsWashoutReverse(...)',
```

`model/formatter.ts`, em `formatFinancialOrigin`, depois de `PurchaseFiscalReconciliation`:

```ts
    m.set("SalesContractWashout", "Washout de contrato de venda");
```

- [ ] **Passo 3: fragmentos**

`view/salesContracts/fragments/SalesContractWashouts.fragment.xml`: copie `view/purchaseContracts/fragments/PurchaseContractWashouts.fragment.xml` inteiro e troque **só**:

- `id="purchaseContractWashoutsTable"` → `id="salesContractWashoutsTable"`;
- `<Title text="Washouts do Contrato de Compra" />` → `<Title text="Washouts do Contrato de Venda" />`;
- tooltip do Registrar → `Registrar a desistência de volume não faturado pelo cliente`.

O resto (colunas, `$$ownRequest`, `FinancialDocument/Code`, visibilidade por `Status`/`ui>/readonly`) fica igual.

`view/salesContracts/fragments/SalesContractWashoutDialog.fragment.xml`: copie `view/purchaseContracts/fragments/WashoutDialog.fragment.xml` inteiro e troque **só**:

- `<Dialog id="purchaseContractWashoutDialog"` → `<Dialog id="salesContractWashoutDialog"`;
- `placeholder="Por que o produtor não vai entregar"` → `placeholder="Por que o cliente desistiu do volume"`.

Parseie os dois XML com o comando das Restrições.

- [ ] **Passo 4: Detail.view.xml**

No cabeçalho, no `VBox` de "Aguardando Liberação:" / "Liberado para embarque:", acrescente depois do HBox de "Liberado para embarque:":

```xml
          <HBox class="sapUiTinyMarginBottom">
            <Text text="Washout:" class="sapUiMediumMarginEnd" />
            <ObjectNumber number="{
              path: 'WashedOutVolume',
              type: 'sap.ui.model.odata.type.Double',
              formatOptions:{
                decimals: 3,
                decimalSeparator: ',',
                groupingEnabled: true,
                groupingSeparator: '.'
              }
            }" unit="{UnitOfMeasureCode}"  />
          </HBox>
```

Depois da seção "Fixações de Preço":

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Washouts">
				<uxap:subSections>
					<uxap:ObjectPageSubSection titleUppercase="false">
						 <core:Fragment fragmentName="siagrob1.view.salesContracts.fragments.SalesContractWashouts" type="XML" />
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

Parseie o XML.

- [ ] **Passo 5: controller**

`SalesContractsBaseController.ts` — imports:

```ts
import { sendJson } from "siagrob1/helpers/FetchHelpers";
import { calculateSalesWashoutAmount, formatNumberPtBr } from "siagrob1/helpers/WashoutHelpers";
import { WashoutDialogState, WashoutFixationOption, WashoutReverseState } from "siagrob1/types/PurchaseContractWashout";
```

Campos, ao lado de `_priceFixationDialog`:

```ts
  private _washoutDialog: Dialog;
  private _washoutReverseDialog: Dialog;
```

Métodos, logo depois de `refreshPriceFixationsList()` (os nomes de handler são os mesmos da compra; os fragmentos ligam `.onOpenWashoutDialog` etc.):

```ts
  /**
   * Abre "Registrar Washout" (spec 2026-10-15). Fixações e washouts ativos são lidos antes de abrir, para o Select
   * nascer com os itens; os saldos vêm do contexto OData (requestProperty garante o valor fora do $select). Tudo é
   * auxílio visual: as travas reais são as do servidor (SalesContractsWashoutGuard).
   */
  async onOpenWashoutDialog() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx) {
      MessageBox.alert("Contrato não carregado.");
      return;
    }

    const contractKey = ctx.getProperty("Key") as string;
    const isPaf = (ctx.getProperty("Type") as string) === "ToBeDetermined";
    const viewModel = this.getModel("viewModel") as JSONModel;

    let fixations: WashoutFixationOption[];
    let availablePhysical = 0;
    let availableToRelease = 0;
    let availableToPricing = 0;
    this.setBusy(true);
    try {
      fixations = await this.loadWashoutFixationOptions(contractKey);
      // Edm.Decimal chega como string: Number() antes de usar.
      availablePhysical = Number((await ctx.requestProperty("AvaiableVolume")) ?? 0);
      availableToRelease = Number((await ctx.requestProperty("TotalAvailableToRelease")) ?? 0);
      availableToPricing = isPaf ? Number((await ctx.requestProperty("AvailableVolumeToPricing")) ?? 0) : 0;
    } catch (err) {
      MessageBox.error((err as Error).message || "Erro ao carregar as fixações do contrato.");
      return;
    } finally {
      this.setBusy(false);
    }

    const firstAvailable = fixations.find(f => f.Remaining > 0) ?? fixations[0];

    const state: WashoutDialogState = {
      contractKey,
      isPaf,
      fixations,
      priceFixationKey: firstAvailable?.Key ?? "",
      contractPrice: firstAvailable?.Price ?? 0,
      fixedVolume: 0,
      unfixedVolume: 0,
      marketPrice: 0,
      penaltyAmount: 0,
      amount: 0,
      dueDate: null,
      reason: "",
      availablePhysical,
      availableToRelease,
      availableToPricing,
    };
    viewModel.setProperty("/washout", state);

    this._washoutDialog ??= await DialogHelper.createDialog(
      this,
      "siagrob1.view.salesContracts.fragments.SalesContractWashoutDialog"
    );

    this._washoutDialog?.open();
  }

  onCloseWashoutDialog() {
    this._washoutDialog?.close();
  }

  /** Recalcula a prévia do valor (fórmula da VENDA) sempre que fixação, volume, mercado ou multa mudam. */
  onWashoutValuesChange() {
    const viewModel = this.getModel("viewModel") as JSONModel;
    const state = viewModel.getProperty("/washout") as WashoutDialogState;
    const fixation = state.fixations.find(f => f.Key === state.priceFixationKey);
    const contractPrice = fixation?.Price ?? 0;

    viewModel.setProperty("/washout/contractPrice", contractPrice);
    viewModel.setProperty(
      "/washout/amount",
      calculateSalesWashoutAmount(
        Number(state.marketPrice ?? 0),
        contractPrice,
        Number(state.fixedVolume ?? 0),
        Number(state.penaltyAmount ?? 0)
      )
    );
  }

  async onConfirmWashout() {
    this.onWashoutValuesChange();

    const viewModel = this.getModel("viewModel") as JSONModel;
    const state = viewModel.getProperty("/washout") as WashoutDialogState;
    const fixedVolume = Number(state.fixedVolume ?? 0);
    const unfixedVolume = state.isPaf ? Number(state.unfixedVolume ?? 0) : 0;
    const reason = (state.reason ?? "").trim();

    if (fixedVolume < 0 || unfixedVolume < 0) {
      MessageBox.error("Os volumes do washout não podem ser negativos.");
      return;
    }
    if (fixedVolume + unfixedVolume <= 0) {
      MessageBox.error("Informe o volume do washout.");
      return;
    }
    if (fixedVolume > 0 && !state.priceFixationKey) {
      MessageBox.error("Selecione a fixação de preço do volume fixado.");
      return;
    }
    if (!reason) {
      MessageBox.error("Informe o motivo do washout.");
      return;
    }
    if (state.amount > 0 && !state.dueDate) {
      MessageBox.error("Informe o vencimento do título a receber.");
      return;
    }

    // Pré-checagens de saldo: só auxílio. O servidor continua sendo a autoridade — inclusive no volume já
    // faturado, que o cliente não calcula.
    const selectedFixation = state.fixations.find(f => f.Key === state.priceFixationKey);
    if (fixedVolume > 0 && fixedVolume > (selectedFixation?.Remaining ?? 0)) {
      MessageBox.error(
        `Volume fixado excede o saldo da fixação (${formatNumberPtBr(selectedFixation?.Remaining ?? 0, 3)}).`
      );
      return;
    }
    if (state.isPaf && unfixedVolume > state.availableToPricing) {
      MessageBox.error(
        `Volume não fixado excede o saldo a fixar (${formatNumberPtBr(state.availableToPricing, 3)}).`
      );
      return;
    }
    if (fixedVolume + unfixedVolume > state.availablePhysical) {
      MessageBox.error(
        `Volume do washout excede o saldo físico do contrato (${formatNumberPtBr(state.availablePhysical, 3)}).`
      );
      return;
    }
    if (fixedVolume + unfixedVolume > state.availableToRelease) {
      MessageBox.error(
        `Volume do washout excede o saldo não liberado do contrato (${formatNumberPtBr(state.availableToRelease, 3)}).`
      );
      return;
    }

    // Diálogo ABERTO e ocupado durante o invoke: se o servidor recusar, nada do que foi digitado se perde.
    this._washoutDialog?.setBusy(true);
    this.setBusy(true);

    const oModel = this.getView().getModel() as ODataModel;
    const action = oModel.bindContext(this.api.salesContractsWashoutCreate);
    action.setParameter("SalesContractKey", state.contractKey);
    action.setParameter("Washout", {
      PriceFixationKey: fixedVolume > 0 ? state.priceFixationKey : null,
      FixedVolume: fixedVolume,
      UnfixedVolume: unfixedVolume,
      MarketPrice: Number(state.marketPrice ?? 0),
      PenaltyAmount: Number(state.penaltyAmount ?? 0),
      DueDate: state.dueDate ?? null,
      Reason: reason,
    });

    try {
      await action.invoke();
      MessageToast.show("Washout enviado para aprovação.");
      this.onCloseWashoutDialog();
      this.refreshContractTotals();
      this.refreshWashoutsList();
    } catch (err) {
      MessageBox.error((err as Error).message || "Erro ao registrar washout.");
    } finally {
      this._washoutDialog?.setBusy(false);
      this.setBusy(false);
    }
  }

  async onReverseWashout() {
    const oTable = this.byId("salesContractWashoutsTable") as Table;
    const selected = oTable.getSelectedIndex();

    if (selected < 0) {
      MessageBox.alert("Selecione um washout para estornar.");
      return;
    }

    const ctx = oTable.getContextByIndex(selected);

    if ((ctx.getProperty("Status") as string) !== "Approved") {
      MessageBox.error(
        "Só é possível estornar washout aprovado. Washout em aprovação é rejeitado na tela de aprovação."
      );
      return;
    }

    const amount = Number(ctx.getProperty("Amount") ?? 0);
    const fixedVolume = Number(ctx.getProperty("FixedVolume") ?? 0);
    const clauses: string[] = ["o volume volta ao saldo"];
    if (fixedVolume > 0) {
      clauses.push("o provisório da fixação é restaurado");
    }
    if (amount > 0) {
      clauses.push(`o título a receber de R$ ${formatNumberPtBr(amount, 2)} é cancelado`);
    }
    const summaryBody =
      clauses.length === 1
        ? clauses[0]
        : `${clauses.slice(0, -1).join(", ")} e ${clauses[clauses.length - 1]}`;
    const state: WashoutReverseState = {
      key: ctx.getProperty("Key") as string,
      summary: `WO-${ctx.getProperty("Sequence") as number}: ${summaryBody}.`,
      reason: "",
    };
    (this.getModel("viewModel") as JSONModel).setProperty("/washoutReverse", state);

    // Fragmento genérico da compra: só liga viewModel>/washoutReverse e os handlers abaixo.
    this._washoutReverseDialog ??= await DialogHelper.createDialog(
      this,
      "siagrob1.view.purchaseContracts.fragments.WashoutReverseDialog"
    );

    this._washoutReverseDialog?.open();
  }

  onCloseWashoutReverseDialog() {
    this._washoutReverseDialog?.close();
  }

  async onConfirmWashoutReverse() {
    const state = (this.getModel("viewModel") as JSONModel).getProperty("/washoutReverse") as WashoutReverseState;
    const reason = (state.reason ?? "").trim();

    if (!reason) {
      MessageBox.error("Informe o motivo do estorno.");
      return;
    }

    this._washoutReverseDialog?.setBusy(true);
    this.setBusy(true);

    const oModel = this.getView().getModel() as ODataModel;
    const action = oModel.bindContext(this.api.salesContractsWashoutReverse);
    action.setParameter("Key", state.key);
    action.setParameter("Reason", reason);

    try {
      await action.invoke();
      MessageToast.show("Washout estornado.");
      this.onCloseWashoutReverseDialog();
      this.refreshContractTotals();
      this.refreshWashoutsList();
    } catch (err) {
      MessageBox.error((err as Error).message || "Erro ao estornar washout.");
    } finally {
      this._washoutReverseDialog?.setBusy(false);
      this.setBusy(false);
    }
  }

  /**
   * Fixações confirmadas do contrato com o saldo lavável de cada uma (volume menos washouts em aprovação e
   * aprovados). Lido por fetch: dado de diálogo, e o filtro é feito aqui para não depender de $filter sobre enum.
   */
  private async loadWashoutFixationOptions(contractKey: string): Promise<WashoutFixationOption[]> {
    type FixationRow = { Key: string; FixationDate?: string; FixationVolume: number | string; FixationPrice: number | string; Status: string };
    type WashoutRow = { PriceFixationKey?: string; FixedVolume: number | string; Status: string };

    const [fixations, washouts] = await Promise.all([
      sendJson("GET", `/odata/SalesContracts(${contractKey})/PriceFixations`),
      sendJson("GET", `/odata/SalesContracts(${contractKey})/Washouts`),
    ]);

    if (!fixations.ok) throw new Error(fixations.message);
    if (!washouts.ok) throw new Error(washouts.message);

    const activeWashouts = ((washouts.data as { value?: WashoutRow[] })?.value ?? [])
      .filter(w => w.Status === "InApproval" || w.Status === "Approved");

    return ((fixations.data as { value?: FixationRow[] })?.value ?? [])
      .filter(f => f.Status === "Confirmed")
      .map(f => {
        const volume = Number(f.FixationVolume ?? 0);
        const price = Number(f.FixationPrice ?? 0);
        const washed = activeWashouts
          .filter(w => w.PriceFixationKey === f.Key)
          .reduce((sum, w) => sum + Number(w.FixedVolume ?? 0), 0);
        const remaining = volume - washed;
        const date = f.FixationDate ? new Date(f.FixationDate).toLocaleDateString("pt-BR") : "";

        return {
          Key: f.Key,
          Price: price,
          Remaining: remaining,
          Text: `${date} · ${formatNumberPtBr(volume, 3)} @ ${formatNumberPtBr(price, 8)} · saldo ${formatNumberPtBr(remaining, 3)}`,
        };
      });
  }

  /** Mesmo motivo de refreshPriceFixationsList: a tabela tem $$ownRequest e cache próprio. */
  private refreshWashoutsList() {
    const oBinding = (this.byId("salesContractWashoutsTable") as Table)
      .getBinding("rows") as ODataListBinding;
    oBinding?.refresh();

    this.refreshChangeLogs();
  }
```

> `refreshContractTotals()` da venda já faz `ctx.refresh()`, que repinta "Saldo", "Aguardando Liberação" e o "Washout" novo do cabeçalho. Confira se `FetchHelpers.sendJson` devolve `{ ok, data, message }` (é o que a compra usa).

- [ ] **Passo 6: gates**

```bash
cd C:/Projetos/SiagroB1/siagro-b1-frontend
python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" webapp/view/salesContracts/Detail.view.xml webapp/view/salesContracts/fragments/SalesContractWashouts.fragment.xml webapp/view/salesContracts/fragments/SalesContractWashoutDialog.fragment.xml
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Esperado: tudo limpo. QUnit dos testes novos pelo navegador (`/test/unit/unitTests.qunit.html` no dev server), conferidos na Task 11.

- [ ] **Passo 7: stage**

```bash
git -C C:/Projetos/SiagroB1/siagro-b1-frontend branch --show-current   # feature/sales-contract-washout
git -C C:/Projetos/SiagroB1/siagro-b1-frontend add webapp/model/ServerRoutes.ts webapp/helpers/WashoutHelpers.ts webapp/model/formatter.ts webapp/view/salesContracts/fragments/SalesContractWashouts.fragment.xml webapp/view/salesContracts/fragments/SalesContractWashoutDialog.fragment.xml webapp/view/salesContracts/Detail.view.xml webapp/controller/salesContracts/SalesContractsBaseController.ts webapp/test/unit/helpers/WashoutHelpers.qunit.ts webapp/test/unit/unitTests.qunit.ts webapp/test/unit/model/formatter.qunit.ts
```

O controlador commita com:

```
feat(sales-contract): seção Washouts no contrato de venda

Registrar e estornar washout no detalhe do contrato de venda, com a
prévia do valor pela fórmula da venda (contrato acima do mercado cobra
a diferença) e os saldos do contrato. O cabeçalho ganha o total
"Washout" e a origem do título aparece como "Washout de contrato de
venda".

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 10 (frontend): tela "Aprovação de Washouts" de Vendas

**Arquivos (em `C:\Projetos\SiagroB1\siagro-b1-frontend\webapp`):**

| Ação | Caminho |
|---|---|
| Criar | `controller/salesContracts/washoutApproval/Main.controller.ts` |
| Criar | `view/salesContracts/washoutApproval/Main.view.xml` |
| Modificar | `manifest.json` (rota e target) |

**Interfaces:** rota `salesContractsWashoutApproval`, padrão `sales-contracts/washout-approval` — **igual** à Key do menu da Task 8. Reaproveita o fragmento `siagrob1.view.purchaseContracts.washoutApproval.fragments.WashoutDecisionDialog`.

- [ ] **Passo 1: manifest**

Em `routing.routes`, depois de `sales-contracts/price-fixation-approval`:

```json
        {
          "pattern": "sales-contracts/washout-approval",
          "name": "salesContractsWashoutApproval",
          "target": "salesContractsWashoutApproval"
        },
```

Em `routing.targets`, depois de `salesContractsPriceFixationApproval`:

```json
        "salesContractsWashoutApproval": {
          "id": "salesContractsWashoutApproval",
          "level": 1,
          "name": "siagrob1.view.salesContracts.washoutApproval.Main",
          "clearControlAggregation": true
        },
```

Confira o JSON: `node -e "JSON.parse(require('fs').readFileSync('webapp/manifest.json','utf8'))"`.

- [ ] **Passo 2: view**

`view/salesContracts/washoutApproval/Main.view.xml`: copie `view/purchaseContracts/washoutApproval/Main.view.xml` e troque:

- `controllerName="siagrob1.controller.purchaseContracts.washoutApproval.Main"` → `controllerName="siagrob1.controller.salesContracts.washoutApproval.Main"`;
- o comentário → `<!-- O backend (SalesContractsWashoutsGetService.QueryPending) já restringe a InApproval. -->`;
- `path: '/PurchaseContractsWashouts'` → `path: '/SalesContractsWashouts'` e `$expand: 'PurchaseContract'` → `$expand: 'SalesContract'`;
- tooltip "Ver Contrato" → `Abrir o contrato de venda do washout selecionado`;
- coluna `Fornecedor` → `Cliente`;
- `{PurchaseContract/Code}` → `{SalesContract/Code}` e `{PurchaseContract/CardName}` → `{SalesContract/CardName}`.

Parseie o XML.

- [ ] **Passo 3: controller**

`controller/salesContracts/washoutApproval/Main.controller.ts`: copie `controller/purchaseContracts/washoutApproval/Main.controller.ts` e troque:

- JSDoc: `Fila de washouts em aprovação, de todos os contratos de venda. Espelha purchaseContracts/washoutApproval.` e `@namespace siagrob1.controller.salesContracts.washoutApproval`;
- `getRoute("purchaseContractsWashoutApproval")` → `getRoute("salesContractsWashoutApproval")`;
- `getProperty("PurchaseContract/Key")` → `getProperty("SalesContract/Key")` e `navTo("purchaseContractsDetail", …)` → `navTo("salesContractsDetail", { id: contractKey, "?query": { readonly: "true" } })`;
- `this.api.purchaseContractsWashoutApproval : this.api.purchaseContractsWashoutReject` → `this.api.salesContractsWashoutApproval : this.api.salesContractsWashoutReject`;
- `getProperty("PurchaseContract/Code")` → `getProperty("SalesContract/Code")`;
- o fragmento continua `"siagrob1.view.purchaseContracts.washoutApproval.fragments.WashoutDecisionDialog"` (genérico).

O resumo do diálogo continua `Valor a receber: R$ …` (o título é a receber nos dois lados).

- [ ] **Passo 4: gates**

```bash
cd C:/Projetos/SiagroB1/siagro-b1-frontend
python -c "import sys,xml.dom.minidom as m; [m.parse(f) for f in sys.argv[1:]]" webapp/view/salesContracts/washoutApproval/Main.view.xml
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Esperado: limpo.

- [ ] **Passo 5: stage**

```bash
git -C C:/Projetos/SiagroB1/siagro-b1-frontend add webapp/controller/salesContracts/washoutApproval/Main.controller.ts webapp/view/salesContracts/washoutApproval/Main.view.xml webapp/manifest.json
```

O controlador commita com:

```
feat(sales-contract): tela de aprovação de washouts de venda

Fila dos washouts de venda em aprovação, no grupo Vendas, com aprovar,
rejeitar (motivo obrigatório) e ver o contrato em modo leitura. A rota
tem o mesmo nome da Key do menu criado pela migration do backend.

Co-Authored-By: Claude {MODELO} <noreply@anthropic.com>
```

---

## Task 11: Verificação no navegador (CEAGUI dev)

**Feita pelo controlador.** Não há código novo; uma correção que surgir vira commit próprio na task de origem.

- [ ] **Passo 1: migrations no CEAGUI dev**

```bash
cd C:/Projetos/SiagroB1/siagro-b1-backend
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations list --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Os `list` mostram `AddSalesContractWashouts` e `AddSalesContractWashoutApprovalMenu` como pendentes e a connection string aponta `CEAGUI_SIAGRO_DEV` (sem expor a senha). Só então:

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Passo 2: subir a stack**

Conforme a memória `run-stack-locally`: profile `ceagui` no Web e no Gateway, `yarn start:dev` no frontend. Faça login de novo (o menu é lido no login) e confira "Vendas → Aprovação de Washouts".

- [ ] **Passo 3: roteiro (spec §8, "Navegador"), contrato CV000001**

Antes, anote do CV000001: tipo, saldo, "Aguardando Liberação", fixações confirmadas e o provisório a receber (código e valor) da seção Financeiro. Se o CV000001 não tiver volume não faturado e não liberado, ou não tiver provisório, escolha outro contrato de venda aprovado da CEAGUI dev e registre o código no ledger.

1. **Registrar** washout no CV000001: fixação, volume fixado pequeno, mercado **abaixo** do preço da fixação, multa, vencimento, motivo. A prévia mostra `(preço − mercado) × volume + multa`. Ele aparece "Em Aprovação"; "Washout" do cabeçalho e "Saldo" mudam pelo volume. O log tem a linha `WO-1: …`.
   - Tente lavar mais que o volume com preço não faturado: recusa W13 com a mensagem do servidor (não "Communication error").
2. **Aprovar** em "Vendas → Aprovação de Washouts". "Ver Contrato" abre o detalhe em modo leitura.
3. Conferir no contrato:
   - saldo e "Washout";
   - o provisório reduzido no lugar (log `Valor`);
   - o título em **Contas a Receber**, origem "Washout de contrato de venda", com "Cancelar" recusado (V4).
   - Se o provisório tiver composto acima do novo valor, a aprovação recusa com V1: confira o texto.
4. **Encerrar** depois de quitar:
   - sem quitar o provisório, a recusa é V3, com "Entrega a menos se resolve por washout.";
   - baixe o título do washout direto (baixa em dinheiro); quite o provisório pela composição dos firmes; encerre.
   - Se quitar não for viável no dado da CEAGUI dev, registre a recusa V3 e siga.
5. **Estornar e rejeitar**:
   - num contrato aprovado (reabra o CV000001, se encerrou), estorne um washout aprovado **sem baixa**: o título cancela ("Washout estornado"), o provisório volta, o volume volta. Com baixa, a recusa pede para estornar a baixa;
   - registre outro e **rejeite** na fila (motivo obrigatório): o volume volta.
6. QUnit: `/test/unit/unitTests.qunit.html` mostra verdes os testes de `WashoutHelpers` e `formatFinancialOrigin`.

- [ ] **Passo 4: limpeza**

- Estorne/rejeite o que foi criado só para teste, quando a tela permitir.
- Derrube os processos **por PID** e confira as portas de novo.
- Anote no ledger os códigos criados (washouts, títulos).

---

## Autorrevisão

**Cobertura do spec:**

| Seção do spec | Task |
|---|---|
| §3 Entidade, enum, índice, "WO-{n}" | 1 |
| §3.1 Cálculo invertido | 1 (entidade), 9 (prévia) |
| §4.1 Colunas e serviço único | 1, 2 |
| §4.2 AvaiableVolume, saldos a liberar, a fixar | 1 |
| §4.2 SQL do saldo a liberar, trava da fixação, totais e DTO | 2 |
| §4.2 Encerramento (negativo e PAF) e interceptor | 3, 1 |
| §5.1 Criar (faturado pelo ledger) | 5 |
| §5.2 Aprovar, trava V1, título a receber | 4, 6 |
| §5.3/5.4 Rejeitar e estornar | 6, 7 |
| §5.5 Gerador e backlog | 4 |
| §5.6 Travas (encerrar, S10, fixação, retirar aprovação, cancelar título) | 3, 4 |
| §6 Origem 8 e gerador do título | 1, 4 |
| §7 Detalhe, diálogo, cabeçalho, formatter | 9 |
| §7 Tela de aprovação e menu | 8, 10 |
| §8 xUnit e navegador | 1–8, 11 |

**Tipos:** `SalesContractsWashedOutVolumeService.{ActiveVolumesAsync, ActiveFixedVolumeAsync, ApprovedFixedVolumeAsync}`, `FinancialDocumentsAdjustProvisionalService.EnqueueForSalesFixationAsync(contract, fixation, remainingVolume, user)`, `FinancialDocumentsGenerateService.EnqueueForSalesFixationAsync(contract, fixation, user, remainingVolume?)` e `FinancialDocumentsGenerateWashoutReceivableService.EnqueueAsync(SalesContract, SalesContractWashout, user)` têm o mesmo nome e assinatura em todas as tasks.

**Pontos que o implementador confere no código (registrados, não placeholders):**

- `SalesInvoiceItem.Key` nullable no `SalesWashoutTestData.Billed` (Task 1);
- tipos exatos de `Code`/`CardName`/`StandardCurrency` na assinatura de `BuildAsync` (Task 4);
- nome do método do controller de rejeição de compra, repetido no de venda (Task 8);
- mensagem do compose quando o provisório for de venda (Task 6).
