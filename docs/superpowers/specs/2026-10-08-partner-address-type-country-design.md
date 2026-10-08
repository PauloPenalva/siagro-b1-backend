# Tipo e País no grid de endereços do parceiro — design

Data: 2026-10-08 · Repos: `siagro-b1-backend` + `siagro-b1-frontend` · Branch: `feature/partner-address-type-country` nos dois

## Objetivo

Na tela `business-partners/new` e `business-partners/{id}/edit`, o grid de endereços
(`webapp/view/parceirosNegocio/fragments/Addresses.fragment.xml`) tem **Tipo** e **País** como
`Input` de texto livre. O usuário digita a letra do tipo e o nome/código do país à mão.

- **Tipo** (`AdresType`) é um código fixo de 1 caractere herdado do CRD1 do SAP: `B` = Cobrança,
  `S` = Entrega. O backend depende desses dois valores (NF-e, CFOP, minuta, endereço do armazém
  filtram por `"B"`/`"S"`); qualquer outra letra quebra essas regras sem aviso.
- **País** (`Country`) guarda o ISO-2 (`BR`), mas não há tabela de países em lugar nenhum.

Sucesso = o usuário escolhe o tipo numa lista e o país num value help; nenhum valor fora do
domínio chega ao banco pelo grid.

## Decisões

| Decisão | Escolha | Por quê |
|---|---|---|
| Tipo | `Select` com 2 itens fixos | Domínio fechado de 2 valores; value help não se justifica |
| País | Tabela `COUNTRIES` + value help | Escolha do usuário; serve a parceiro do exterior e guarda o código BACEN |
| Fonte dupla (OCRY no SAPB1) | **Não** | Em SAPB1 o endereço não é gravável (`SAP\BusinessPartnerService` lança `NotImplementedException` em create/update; `IBusinessPartnerAddressService` nem é registrado). Mapear OCRY seria código sem caminho de escrita. Reabrir se o SAPB1 passar a editar endereço |
| FK `Address.Country → COUNTRIES` | **Não** | Decisão do usuário: sem restrição no banco. Dado ruim aparece quando a NF-e reclamar e o usuário corrige o cadastro. Também não se altera o tipo da coluna `Country` |
| Default do País em endereço novo | `BR` | Decisão do usuário |
| NF-e | **Inalterada** | `NfeXmlBuilder` continua com `cPais=1058/xPais=BRASIL` fixos. Exportação (UF `EX`) fora do escopo; `BacenCode` fica pronto para ela |
| Bug `ui>/editable` | Corrigir junto | Decisão do usuário (ver Frontend) |

## Backend

### Entidade `Country` (`SiagroB1.Domain/Entities/Country.cs`)

No molde de `Municipality` — tabela de referência só leitura, semeada pela migration:

```cs
[Table("COUNTRIES")]
public class Country
{
    [Key, Column(TypeName = "VARCHAR(2)")]  public required string Code { get; set; }      // ISO-2: BR
    [Column(TypeName = "NVARCHAR(100) NOT NULL")] public required string Name { get; set; } // BRASIL
    [Column(TypeName = "VARCHAR(4) NOT NULL")]    public required string BacenCode { get; set; } // 1058
}
```

`DbSet<Country> Countries` no `AppDbContext`. Índice único em `BacenCode`.

### Seed

- Fonte dos nomes e códigos BACEN: `EfisCloud/backend/src/main/resources/ibge/paises.txt`
  (234 linhas, `00000 NOME`, ASCII, sem acento — a tabela de países do BACEN/IBGE usada na NF-e).
  O código vai para `BacenCode` sem o zero à esquerda de 5 dígitos → 4 dígitos (`01058` → `1058`).
  O nome é gravado como está no arquivo (é o `xPais` oficial).
- Esse arquivo **não tem ISO-2**. O ISO-2 é atribuído **uma vez, ao gerar o `countries.txt`**
  (script descartável no scratchpad, não em runtime nem na migration): casamento automático por
  nome com a lista ISO 3166 (`RegionInfo`/`CultureInfo` pt-BR) + revisão manual das linhas que não
  casarem. O que vai para o repo é só o arquivo resultante.
  Entrada BACEN sem ISO-2 correspondente (territórios extintos, agrupamentos) **fica de fora** e
  é listada no commit. A chave é o ISO-2 porque é o que `Address.Country` já guarda (`BR`).
- Arquivo embutido `SiagroB1.Migrations/Seeds/countries.txt` (`ISO2|BACEN|NOME`) +
  `CountrySeed.cs` com `Read()` e `InsertBatches()`, no padrão de `MunicipalitySeed`.
- Migration `CreateCountries`: `CreateTable` + `foreach (var sql in CountrySeed.InsertBatches()) migrationBuilder.Sql(sql);`.
  `Down()` derruba a tabela.

### OData

- `CountryService` concreto (`QueryAll()` com `AsNoTracking`), registrado uma vez para os dois
  modos — como `MunicipalityService`.
- `CountriesController` só leitura: `[EnableQuery(PageSize = 300)] Get()` e `Get(key)`.
- `modelBuilder.EntitySet<Country>("Countries")` em `ODataConfigurations`, ao lado de
  `Municipalities`. A tabela existe nos bancos dos dois modos (migration do `AppDbContext`).

### Validação do tipo

`AdresType` precisa ser `B` ou `S`. Um helper único (`AddressTypes.EnsureValid(string, IStringLocalizer)`)
lança `BusinessException(resource["BP_ADDRESS_INVALID_TYPE"])` e é chamado em:

- `BusinessPartnerService.CreateAsync` (deep insert dos endereços);
- `BusinessPartnerAddressService.Create`.

Mensagem nova em `Resource.resx` + `Resource.pt-br.resx`:
`BP_ADDRESS_INVALID_TYPE` = "Tipo de endereço inválido. Use Cobrança ou Entrega."

O `Update` não valida o tipo: `AdresType` é parte da chave
(`CardCode + AddressName + AdresType`, `AppDbContext.cs:249`) e não é alterado por PATCH.

`Country` não é validado contra a tabela (sem restrição, por decisão).

## Frontend

### Tipo — `Addresses.fragment.xml`

```xml
<Select
  selectedKey="{path: 'AdresType', targetType: 'any'}"
  forceSelection="false"
  required="true"
  width="100%"
  enabled="{= ${ui>/editable} &amp;&amp; %{@$ui5.context.isTransient} !== false }">
  <core:ListItem key="B" text="Cobrança" />
  <core:ListItem key="S" text="Entrega" />
</Select>
```

O Select fica **travado nas linhas já gravadas**: como `AdresType` é chave, trocar o tipo de um
endereço existente no `/edit` não seria persistido. Para mudar o tipo, remove-se a linha e
inclui-se outra. Linhas novas (transientes) ficam livres, inclusive em todo o `/new`.

### País — value help

```xml
<Input editable="{ui>/editable}" required="true" value="{Country}"
  showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openCountriesValueHelp" />
```

- `valueHelpOnly`, como o Município: sem texto livre. Exibe o código (`BR`); o endereço não
  guarda o nome e, sem FK, não há navegação para lê-lo.
- `dialogs/fragments/CountriesSelectDialog.fragment.xml`: `TableSelectDialog` sobre
  `{path:'/Countries', sorter:{path:'Name'}}`, colunas Código e País, busca por `Code`/`Name`.
- `CommonController.openCountriesValueHelp(ev)` →
  `void this.applyValueHelp(ev, "CountriesSelectDialog", ["Code", "Name"], "Code")`.

### Linha nova — `parceirosNegocio/BaseController.onAddAddress`

O payload do `create` ganha `AdresType: null` (o Select com `targetType 'any'` exige a
propriedade já existente na linha transiente) e `Country: "BR"`.

### Correção `ui>/editable`

Nenhum controller do parceiro define `ui>/editable`; o grid herda o valor da última tela que o
definiu (ex.: uma tela de detalhe que o põe `false` deixa o grid do parceiro somente leitura).
`Add.newRouteMatched` e `Edit.editRouteMatched` passam a fazer
`(this.getModel("ui") as JSONModel).setProperty("/editable", true)`, como `purchaseContracts/Edit`.

## Testes e verificação

Backend (`SiagroB1.Application.Tests`):
- `CountrySeedTests`: lê o recurso; contém `BR`/`1058`/`BRASIL`; ISO-2 e BACEN sem duplicata;
  todo ISO-2 com 2 letras maiúsculas e todo BACEN com 4 dígitos.
- `BusinessPartnerAddressService.Create` com `AdresType = "X"` → `BusinessException`; com `B`/`S` grava.
- `BusinessPartnerService.CreateAsync` com endereço de tipo inválido → `BusinessException`, nada gravado.

Frontend: `yarn ts-typecheck` + `yarn lint` + `yarn ui5lint`.

E2E no navegador (stack local, STANDALONE):
1. `/business-partners/new` → Incluir endereço: País vem `BR`, Tipo vazio; escolher "Entrega",
   trocar País pelo value help; salvar; reabrir em `/edit` e conferir `S` + país.
2. Em `/edit`, o Select do endereço já gravado está travado; o de uma linha recém-incluída não.
3. Navegar de uma tela que deixa `ui>/editable = false` para o parceiro → grid editável.

Migration aplicada só no banco de dev local; HOM/PRD seguem o fluxo normal de deploy.

## Fora do escopo

- Mapeamento OCRY / edição de endereço em SAPB1.
- FK e saneamento de `Address.Country` existente.
- País do destinatário na NF-e (exportação).
