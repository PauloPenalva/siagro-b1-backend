# Task 2 report - cadastros CTT, CTD e flags da conta

Status: DONE.

## What
- AccountingEntityRules (Services/Accounting): normalizacao (trim+upper, max 20), vigencia, hierarquia (pai existe, sintetico, auto-referencia/ciclo, tornar analitico com filhos) e exclusao com filhos. Assinatura ganhou `codeOf` e `where T : class` em relacao ao brief (necessarios para achar o proprio registro e usar AsNoTracking).
- CostCenterService: classe/pai/vigencia/hierarquia; duplicidade case-insensitive. CostCenterModel expoe Class, ParentCode, ValidFrom, ValidTo.
- AccountingItem: IAccountingItemService, AccountingItemModel, AccountingItemService, AccountingItemsController, OData set AccountingItems, registro em AddApplicationServices.
- LedgerAccountService/Model: 4 flags; "exige e nao aceita" gera DefaultException (centro de custo / item contabil).
- SAP: CostCenter projeta Class=Analytic; LedgerAccount flags aceita=true, exige=false.

## Evidence
- RED: build dos testes falhou com CS0246 (AccountingItemService / AccountingItemModel nao existem) antes da implementacao.
- GREEN: filtro Accounting|CostCenter|LedgerAccount: 23 passed, 0 failed.
- Suite completa Application.Tests: 6143 passed, 0 failed. `dotnet build SiagroB1.sln`: 0 erros.

## Concerns
- Brief diz "Requires=true forca Accepts=true" e tambem "Accepts=false com Requires=true da erro"; sao contraditorios, implementei o erro (nao ha como distinguir).
- Hierarquia carrega o conjunto inteiro do cadastro em memoria (cadastro pequeno).
- Nenhuma migration nesta task.
