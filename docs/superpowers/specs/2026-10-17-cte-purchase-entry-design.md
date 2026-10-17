# CT-e de frete tomado no Documento de Entrada — design (2026-10-17)

> Aprovado pelo usuário em 17/10/2026. Escopo: só STANDALONE.

## Contexto e porquê

A filial contrata transporte. A transportadora, cadastrada como parceiro de negócio, emite um CT-e (modelo 57) contra
a filial, que é a tomadora do serviço. Hoje o Documento de Entrada não aceita esse documento. Ele conhece só a "NF-e"
(modelo 55, chave conferida) e o "Outro" (sem chave). Com isso, o frete pago não vira título a pagar, o crédito de
ICMS e PIS/COFINS do frete não é escriturado, e não se sabe qual nota cada frete transportou.

Pedido do usuário (17/10/2026):
- receber o XML do CT-e da transportadora;
- gerar o título a pagar;
- calcular os tributos e o crédito de ICMS **pela configuração da natureza de operação**, como nas demais entradas;
- vincular o CT-e à NF-e de **venda** da empresa, que é o caso comum (frete CIF por nossa conta);
- às vezes vincular à NF-e de **compra**, quando o frete da compra é por nossa conta (FOB).

A **regra** é escriturar o CT-e como serviço de transporte tomado. A **contra-nota** do CT-e é a exceção. Ela é a NF-e
de entrada própria com CFOP 1.931/2.931, prevista no RICMS/SP art. 316 e no Conv. ICMS 25/90, para transportador
autônomo ou de outra UF sem inscrição na UF de início. Ela **fica na fila**: o usuário vai trazer o XML real de uma
contra-nota emitida por outro cliente (Protheus) antes de desenharmos o conteúdo fiscal. Este desenho só não pode
fechar a porta para ela (ver §8).

## Objetivos
1. Escriturar um CT-e Normal (`tpCTe = 0`) no Documento de Entrada, importando o XML ou digitando.
2. Título a pagar à transportadora na confirmação, pelo caminho já existente de "outras compras".
3. Tributos calculados pela natureza da linha (CFOP, ICMS, PIS/COFINS, Efeito/crédito), iguais com ou sem XML.
4. Vínculo do CT-e com as NF-e transportadas, de saída ou de entrada, sugerido a partir das chaves do XML.

## Não objetivos
- A contra-nota do CT-e (fila, §8).
- CT-e de complemento, de anulação e substituto (`tpCTe` 1, 2 e 3).
- CT-e OS (modelo 67) e o CT-e emitido pela própria filial (somos tomadores, não transportadores).
- Consulta do protocolo do CT-e na SEFAZ. A NF-e de terceiro tem essa consulta; o CT-e fica só com a conferência
  local da chave. *Minha decisão:* não existe cliente de consulta de CT-e no projeto, e isso pode vir depois.
- Rateio do frete no custo da mercadoria. **Não existe custo de estoque no sistema** (`Usage.MovesFiscalInventory`
  está "sem efeito por ora"), então não há onde lançar.
- Modo SAPB1. SPED/EFD (registro D100) fica para o roadmap fiscal.

## Anti-objetivos (falhou mesmo que funcione)
- Título a pagar **duplicado** para o mesmo frete, por exemplo por dois CT-e com a mesma chave.
- Crédito de ICMS escriturado diferente do que a lei permite para o frete.
- Quebrar o caminho atual da NF-e (modelo 55) e da contra-nota do produtor.

## Desenho

### 1. Tipo de documento "CT-e"
- `TaxDocumentKind` ganha `Cte = 2`, modelo 57. Na tela, o campo "Tipo de documento" passa a oferecer "CT-e (frete)"
  para terceiro Normal.
- Chave obrigatória e conferida localmente, generalizando o `SupplierNfeKeyGuard`:
  - modelo `57` em vez de `55`;
  - DV e CNPJ do emitente = transportadora (o `CardCode` do documento);
  - número e série conferem com os da chave.
- Unicidade da chave pelo mesmo `PurchaseInvoiceChaveNFe.EnsureFreeAsync`, que já vale entre documentos válidos.
- A chave continua na coluna `ChaveNFe`. *Minha decisão:* não renomear a coluna, porque o nome é histórico e uma
  troca tocaria dezenas de pontos.
- O CT-e é sempre de **terceiro** e do tipo **Normal**. Não pode ser devolução nem complemento de valor.
- Campos novos no cabeçalho, preenchidos pelo XML e editáveis na digitação:
  - `FreightStartState` / `FreightEndState`: UF de início e de fim da prestação (`ide/UFIni`, `ide/UFFim`);
  - `FreightStartCity` / `FreightEndCity`: municípios, só informativos.

### 2. A linha do CT-e é um serviço
- O CT-e tem **uma linha**: o serviço de transporte, com valor = `vPrest/vTPrest`.
  - Pesos e quantidade não se aplicam.
  - Quantidade 1 e preço = valor da prestação.
- O motor de tributos exige produto com NCM e origem. *Minha decisão:* o usuário cadastra um produto "FRETE" (NCM
  `00000000`, porque o cadastro de produto exige 8 dígitos; origem 0) e o informa na linha.
  - A importação do XML sugere esse produto pelo "produto padrão de frete" da **filial**
    (`Branch.FreightServiceItemCode`). *Decisão do plano:* a importação ainda não conhece a natureza.
  - Se a filial não tiver o produto configurado, a linha vem sem produto e o usuário escolhe.
- A natureza da linha é de **entrada**, `RequiresContract = false` e `CreatesFinancialDocument = true`, ou seja,
  "outra compra". O título nasce pelo `FinancialDocumentsOtherPurchaseService`, já existente. Parcelas pela condição
  de pagamento e retenções, se a natureza tiver, saem do mesmo jeito, sem código novo de financeiro.
- O usuário cria naturezas como "Frete sobre vendas" e "Frete sobre compras", com CFOP 1.353/2.353 ou 1.352/2.352,
  conforme a atividade, e CSTs de crédito. Não há semente: o cadastro é do usuário.

### 3. Tributos
- Os tributos saem da natureza pelo motor atual (`PurchaseInvoicesTaxApplyService` + `TaxLineCalculator`). A
  diferença é de onde vem a UF:
  - **dentro ou fora do estado (CFOP 1.x ou 2.x):** UF de início da prestação × UF da filial, e não UF do fornecedor
    × UF da filial. A transportadora pode ser de PR e fazer um frete que começa em SP.
  - **alíquota interestadual automática (7% ou 12%):** UF de início × UF de fim da prestação.
- **ICMS recalculado pela natureza, não copiado do XML (decisão do usuário, 17/10/2026).** É o mesmo princípio da NF-e
  de terceiro (spec terceiro-chave D1). O operador muitas vezes digita o CT-e sem XML, e o resultado tem de ser o
  mesmo nos dois caminhos.
  - O ICMS do XML **não** é guardado na linha. O XML continua anexado em `XmlData` para consulta.
  - CT-e sem destaque (CST 60, ICMS por substituição): o usuário escolhe uma natureza sem crédito, por exemplo
    "Frete com ICMS-ST", com CST 90/41 e sem valor. O débito do tomador substituto fica para a contra-nota (fila).
  - *Minha decisão:* o CST e o valor do ICMS do XML ficam guardados no cabeçalho só como informação (`CteIcmsCst`,
    `CteIcmsValue`). O Detail **avisa**, sem recusar, quando eles divergem do recalculado: CST diferente ou diferença de
    valor maior que R$ 0,01. Isso pega a natureza errada antes da confirmação. O aviso fica no Detail, e não na
    importação, porque a importação ainda não conhece a natureza.
- **DIFAL:** o CT-e fica **fora** do `PurchaseInvoiceDifal` nesta entrega. *Minha decisão:* o DIFAL hoje usa a UF do
  fornecedor, que para o CT-e é a transportadora e daria a UF errada. Além disso, o frete de venda e o de compra para
  revenda estão vinculados a uma operação seguinte, então não têm DIFAL (LC 87/96, art. 2º, IV). DIFAL de frete de uso
  e consumo fica para depois.
- PIS/COFINS pela natureza, como hoje. O crédito sobre frete de venda no regime não cumulativo é decidido pelo CST da
  natureza.

### 4. Importação do XML
- O mesmo botão "Importar XML" aceita CT-e. O serviço detecta a raiz:
  - `cteProc` ou `CTe`, no namespace `http://www.portalfiscal.inf.br/cte`, vai para um leitor novo,
    `SupplierCteXmlReader`;
  - `nfeProc` / `NFe` continua indo para o leitor atual.
- O que é lido:
  - **chave:** `infCte/@Id`;
  - **identificação:** `ide/nCT`, `ide/serie`, `ide/dhEmi`, `ide/UFIni`, `ide/UFFim`, `ide/xMunIni`, `ide/xMunFim`,
    `ide/tpCTe`;
  - **emitente:** `emit/CNPJ`, que resolve a transportadora como parceiro pelo mesmo `ResolveCardCode` (erro claro se
    não houver cadastro);
  - **valor:** `vPrest/vTPrest`;
  - **ICMS:** o grupo `imp/ICMS`;
  - **observações:** `compl/xObs` vai para as informações complementares;
  - **NF-e transportadas:** `infCTeNorm/infDoc/infNFe/chave`.
- Recusas:
  - `tpCTe` diferente de 0 ("só CT-e normal nesta versão");
  - tomador que não é filial da empresa.
- Tomador do CT-e (*minha decisão*):
  - É identificado pelo CNPJ do grupo `toma3`/`toma4`. O `toma3` aponta para remetente, expedidor, recebedor ou
    destinatário.
  - A importação procura uma filial com esse CNPJ **completo** e preenche o `BranchCode` do rascunho. Não achou:
    recusa com "O tomador do CT-e (CNPJ x) não é uma filial cadastrada".
  - Na gravação e na confirmação, o CNPJ do tomador guardado no documento (coluna nova `FreightPayerTaxId`) deve ser o
    `TaxId` da filial do documento. Com isso a digitação também é conferida.
- Valor (*minha decisão*):
  - A linha usa `vTPrest`, que é a base do ICMS.
  - Se `vRec` (valor a receber) for diferente de `vTPrest`, a importação avisa e não ajusta nada; o usuário trata na
    tela.
  - Desconto em CT-e é raro, e mexer no total mudaria a base dos tributos.
- O rascunho nasce com:
  - `IssuerType = ThirdParty`, `InvoiceType = Normal` e `TaxDocumentKind = Cte`;
  - `TruckingCompanyCode` = a própria transportadora;
  - o XML guardado em `XmlData`, como já se faz com a NF-e.

### 5. Vínculo com as NF-e transportadas
- Tabela filha nova, `PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS`, ligada ao Documento de Entrada do CT-e:
  - `AccessKey` (44);
  - `SalesInvoiceKey?` (FK Restrict para o documento de saída);
  - `TransportedPurchaseInvoiceKey?` (FK Restrict para outro Documento de Entrada).
- Os vínculos nascem **na gravação** do CT-e, a partir do XML guardado. O rascunho da importação não os leva. Cada chave
  do XML é procurada pela `ChaveNFe`:
  - entre os documentos de saída;
  - entre os de entrada de **outros** documentos.
  - Achou: preenche a FK.
  - Não achou: guarda só a chave. *Minha decisão:* a nota pode ainda não ter sido lançada; o vínculo pode ser refeito
    depois por um botão "Refazer vínculos".
- Com o CT-e pendente, o usuário inclui uma chave (44 dígitos) ou remove um vínculo.
- *Minha decisão:* o vínculo é **informativo** nesta entrega. Não muda saldo, título nem tributo, e o CT-e pode ser
  confirmado sem nenhuma nota vinculada.
- O Detail do Documento de Saída e o do Documento de Entrada transportado ganham a seção somente leitura "CT-e de
  frete", com link para cada CT-e não cancelado que os referencia.

### 6. Confirmação, estorno e cancelamento
- Igual a "outra compra": confirmação gera os títulos; estorno e cancelamento cancelam os títulos em aberto e recusam
  se houver baixa.
- `SupplierNfeAuthorizationService`, a consulta à SEFAZ, **não roda** para CT-e.

### 7. Telas (frontend)
- Inclusão: "Importar XML" aceita CT-e. "Tipo de documento" ganha "CT-e (frete)".
- Com CT-e, o formulário:
  - mostra UF/município de início e fim;
  - esconde pesos e volumes;
  - mostra a seção **"NF-e transportadas"**, com chave, tipo (Saída/Entrada), número, parceiro e link, mais incluir,
    remover e "Refazer vínculos";
- Lista de Documentos de Entrada: o filtro e a coluna de tipo de documento distinguem NF-e, CT-e e Outro.
- Detail do Documento de Saída e do de Entrada: seção "CT-e de frete" (§5).

### 8. Porta aberta para a contra-nota (fila)
Pontos que a contra-nota vai exigir e que este desenho não deve impedir:
- `refCTe` no NFref do `NfeXmlBuilder`, que hoje só tem `refNFe`;
- grupo de ICMS com ST no builder e no motor;
- destinatário = a transportadora.

⚠️ `IsOtherPurchaseDocumentAsync` e `ApplyWithholdingsAsync` **não excluem a contra-nota** hoje. Se a contra-nota do
CT-e copiar a natureza de "outra compra", a confirmação dela geraria título **duplicado**. Quando a contra-nota for
feita, esses dois pontos precisam ignorar o `ProducerInvoiceKey` preenchido, ou a chave equivalente.

## Restrições
- Só STANDALONE. Os tributos dependem do `TaxCalculationGate`: STANDALONE e filial que emite NF-e.
- Migration nova no AppDbContext, sem menu novo. Ela traz o enum, as colunas de UF/município e a tabela de vínculo.
- O caminho da NF-e modelo 55, a contra-nota do produtor e os títulos de grão não mudam.

## Deixado ao construtor
- Nome exato das colunas e dos serviços.
- Como generalizar o guard de chave: um guard com o modelo como parâmetro ou dois guards.

## Verificação
1. Testes (`SiagroB1.Application.Tests`):
   - leitor do XML do CT-e: chave, UFs, valor e chaves das NF-e, mais o aviso de ICMS divergente;
   - recusa de tpCTe ≠ 0 e de tomador ≠ filial;
   - guard de chave modelo 57;
   - CFOP 1.x ou 2.x pela UF de início;
   - alíquota interestadual pela UF de início × fim;
   - vínculo achado e não achado;
   - título gerado na confirmação e cancelado no estorno;
   - caminho NF-e intacto.
2. `yarn ts-typecheck` + `yarn lint`.
3. Navegador em STANDALONE (profile `ceagui`), a partir da home:
   - importar um XML de CT-e de exemplo que referencia uma NF-e de venda existente;
   - conferir transportadora, UFs, valor, natureza "Frete sobre vendas", CFOP e ICMS;
   - conferir o vínculo à NF-e de venda;
   - confirmar e ver o título no Financeiro;
   - abrir a NF-e de venda e ver o CT-e na seção "CT-e de frete";
   - estornar e ver o título cancelado.
