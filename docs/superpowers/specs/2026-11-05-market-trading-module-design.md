# Módulo Mercado (cotações, preço por cidade, boleta, hedge e posição) — desenho

Data: 2026-11-05 · Repos: `siagro-b1-backend` + `siagro-b1-frontend`

## 1. Intenção e porquê

O SiagroB1 vai ganhar um módulo de **Mercado / Comercialização** que responda à pergunta da
diretoria e do trader: **"quanto estamos expostos em soja e dólar, e quanto já está travado?"**.

A referência funcional é o Tagui/Siagro (Java 17 + Angular): telas `/terminal-cotacoes`,
`/tabela-frete`, `/tabela-precos`, `/ordem-compra`, `/solicitacao-contrato`, `/hedge-commodity`,
`/hedge-moeda`, `/exchange-for-physical`, mais o microserviço `cma-data-feed`. Esse sistema foi
construído sob demanda dos traders e virou uma colcha de retalhos — a Tagui teve resultados ruins
quando começou a operação. **Não é para portar o Tagui**: é para construir um módulo de produto,
vendável a outros clientes, seguindo as **práticas usuais de mercado** de trading/hedge de grãos.
Pode e deve simplificar.

## 2. Objetivos, não-objetivos, anti-objetivos

**Objetivos**
- Cotações em tempo real (CMA) **e** lançamento manual, sempre disponível.
- Preço de compra por cidade de origem calculado ao vivo, com boleta que vira contrato de compra.
- Registro de hedges de commodity, de moeda e EFP como as mesas fazem.
- Painel de posição: exposição física × bolsa × dólar, % hedge, **marcação a mercado** e
  **limites de exposição** com alerta.

**Não-objetivos (desta versão)**
- Boleta de venda, contrato "a fixar" e washout criados pela grade.
- Importação automática de extrato/nota de corretagem.
- Lançamentos no financeiro/contabilidade de ajuste diário, margem e resultado de hedge.
- Execução de ordens na corretora (o sistema registra, não negocia).

**Anti-objetivos (falha mesmo que "funcione")**
- Fórmulas/instrumentos fixos no código (soja/milho/portos hardcoded, uma calculadora por ativo).
- Número na tela sem origem rastreável (de onde veio a cotação, como o preço foi composto).
- Posição que não bate com o que a corretora e os contratos dizem.
- Depender da CMA para o módulo funcionar.

## 3. Restrições

- Stack existente: .NET 10 OData + Hangfire + SSE (padrão de `ScalesController` /
  `LiveReadingStore` / `ScaleLiveService.ts`); OpenUI5 TS no frontend. Nada de SignalR novo.
- Multi-empresa: tudo por empresa (banco); credenciais CMA por empresa.
- Funciona em STANDALONE e SAPB1 (contratos de compra/venda do SiagroB1 em ambos).
- Código em inglês, textos ao usuário em pt-BR; menus por migration em `MENU_ITEMS` com ícone
  `sap-icon://folder-blank` no módulo de topo.
- Cada cliente tem seu próprio contrato com a CMA (Streamer API, HTTPS, login → sessionId,
  canal persistente, ping a cada 30 s, `QuotesRequest` com `symbols:[{sourceId, symbol}]`;
  ver `C:\Projetos\Tagui\Siagro\cma-data-feed\...\infrastructure\streamer\StreamerConnection.java`).
  Valores da CMA vêm como texto, podem ter sufixo "S" (settlement) e "-" significa sem valor.

## 4. Entregas (ordem obrigatória — cada uma com plano próprio)

### 4.1 Cotações (base)
- **Instrumentos** (cadastro configurável): código/símbolo, `sourceId` CMA, bolsa (CBOT, B3, …),
  commodity (ou moeda), vencimento, moeda e unidade de cotação (US¢/bu, R$/sc, R$/US$), tamanho do
  lote e fator de conversão para t e saca 60 kg, tipo (futuro, spot, ponto da curva de dólar), ativo.
  Seed de exemplo: soja CBOT, milho CBOT, milho B3, dólar comercial, curva de dólar (30D…720D).
- **Cotação atual** por instrumento: último, compra, venda, variação %, máxima, mínima, abertura,
  fechamento/ajuste, data-hora, **origem** (CMA ou manual + usuário).
- **Fechamento diário** (snapshot) por instrumento — base da marcação a mercado e do histórico.
- **Lançamento manual** sempre disponível; vale para quem não tem CMA e para corrigir/completar.
- **Integração CMA** dentro do SiagroB1 (serviço em segundo plano no Web ou no worker — decisão do
  builder, justificada no plano): mantém sessão, assina instrumentos ativos, grava a cotação
  atual, publica atualização por SSE. Configuração por empresa (host, usuário, senha protegida).
- **Terminal de Cotações**: abas por grupo, status da conexão, colunas Código/Descrição/Último/
  Compra/Var%/Máxima/Mínima/Abertura, atualização ao vivo. Conectar/desconectar só admin.

### 4.2 Preço por cidade + boleta
- **Destinos** (portos/pontos de entrega) em cadastro.
- **Tabela de frete**: município (FK do cadastro existente) × destino → R$/t, vigência, histórico.
- **Tabela de preços** por produto + safra; linhas = período de entrega com instrumento, prêmio,
  curva de dólar, data de recebimento/pagamento, destino, ajustes (elevação, juros, desconto % ou
  R$, margem, quebra técnica, funrural, coeficiente de frete), ativa.
- **Uma fórmula genérica** para qualquer instrumento (US$ ou R$). Referência do cálculo atual no
  Tagui: `Backend\...\domain\calculadorapreco\CalculadoraPrecoSojaCbotService.java` (soja, fator
  0,36745 US¢/bu→US$/t), `CalculadoraPrecoMilhoCbotService.java` (0,393678) e
  `CalculadoraPrecoMilhoB3Service.java` (R$/sc direto). O fator vem do instrumento, não do código.
  O resultado sempre expõe o **passo a passo** (bolsa → prêmio → dólar líquido → preço porto →
  descontos → frete → preço na origem).
- **Grade "Ordem de Compra"**: safra + cidade → linhas recalculadas ao vivo quando a cotação muda.
- **Boleta**: nasce da linha com preço/prêmio/dólar/frete/passo a passo **congelados**; comprador
  completa parceiro, quantidade, armazém, observações; envia para aprovação; aprovada → gera
  **contrato de compra preço fixo** do SiagroB1 (reusa `PurchaseContractsCreateService`) e guarda o
  vínculo. Rejeitada → motivo.
- **Alçada**: aprovação adicional da diretoria por volume, desvio de preço frente à tabela, ou se a
  boleta estourar limite de exposição.

### 4.3 Hedges
- **Contrapartes**: corretoras (bolsa) e bancos (câmbio), com contas.
- **Operação de bolsa** (commodity e futuro de dólar): data-hora, corretora/conta, instrumento,
  compra/venda, lotes, preço, corretagem/taxas, quantidade em t derivada do instrumento, status de
  conferência com a corretora (marcado no fim do dia).
- **Encerramento**: operação inversa ou rolagem; casamento PEPS; resultado realizado; o aberto vai
  para marcação a mercado.
- **Hedge de moeda** (NDF/termo): banco, US$, taxa contratada, data de liquidação, compra/venda;
  liquidação apura resultado contra PTAX do dia (lançada ou cotação).
- **EFP**: troca posição de bolsa por físico; vincula operação ao contrato de compra/venda,
  encerra a bolsa e fixa o preço do físico pela bolsa da troca.
- Vínculo **opcional** hedge ↔ contrato/fixação; a posição é calculada pelo total.
- Ajuste diário e margem: informativos, sem financeiro.

### 4.4 Posição
- **Commodity**, em t/sacas, por mês de vencimento: compras físicas preço fixo − vendas físicas
  preço fixo ± futuros abertos = **posição líquida**. Bolsa separada de prêmio/basis.
- **Dólar**, em US$, por mês: exposição de contratos/preços atrelados a US$ sem câmbio fechado ±
  hedges de moeda/futuros de dólar.
- **% hedge** (coberto ÷ exposto) por commodity e dólar.
- **Marcação a mercado** (cotação ao vivo e fechamento diário) da posição aberta e dos hedges;
  resultado realizado × não realizado.
- **Limites de exposição** definidos pela diretoria (por commodity/dólar, opcionalmente por mês);
  alerta na tela e notificação (reusa `NotificationGroup*`/outbox) quando ultrapassados.
- Público: trader e diretoria (perfis/roles próprios).

## 5. Decisões já tomadas pelo usuário
- Módulo novo de produto, pode simplificar em relação ao Tagui.
- CMA por cliente + lançamento manual sempre possível.
- Limites e marcação a mercado entram.
- Hedges: registra o que a corretora/banco devolve; trader fecha lotes ao longo do pregão.
- As quatro partes acima, nesta ordem, foram aprovadas como descritas.

### Respostas do usuário à revisão (2026-11-05)
- **Exposição física** = **saldo não entregue** dos contratos com preço fixado, incluindo as
  fixações de contratos PAF existentes. Mês de vencimento = **data de entrega** do contrato mapeada
  ao vencimento de bolsa correspondente (ex.: entrega em junho → ZSN). Sem preço fixado não entra
  na exposição de bolsa.
- **Exposição em dólar**: adicionar aos contratos de compra **e** venda moeda, preço/prêmio em US$
  e "câmbio fechado sim/não". **Só para quem contratou o módulo**: chave em `appsettings`
  (ex.: `Modules:Market:Enabled`). Desligada → campos ocultos nas telas, menus do módulo não
  aparecem, jobs/integração CMA não rodam.

## 6. Decisões do Claude (revisáveis pelo usuário)
- **Mapeamento entrega → vencimento**: configurável por instrumento/commodity (tabela mês de
  entrega → mês de bolsa), com seed para soja e milho CBOT/B3.
- **"Bolsa separada de prêmio"**: nesta versão é só a decomposição do preço fixado no painel;
  posição de basis independente fica para quando houver "a fixar".
- **EFP**: usa o fluxo de **fixação de preço existente** (contratos PAF) — cria uma fixação com
  o preço da bolsa da troca; não altera preço de contrato fixo.
- **Alçada**: faixas de volume e de desvio % (preço da boleta × preço calculado da tabela no
  momento) configuráveis por empresa; um nível adicional (diretoria). A aprovação da boleta
  substitui a do contrato: o contrato gerado **nasce aprovado**.
- **Fórmula**: os serviços do Tagui são o oráculo; o plano começa extraindo casos numéricos deles
  para os testes. Ordem dos ajustes igual à do Tagui (§4.2).
- **SAPB1**: a boleta gera contrato do SiagroB1 exatamente como um contrato manual faz em cada
  modo (parceiro/armazém pelos serviços já ramificados por modo).
- **Fechamento diário**: job Hangfire às 18:00 (America/Sao_Paulo) grava o snapshot a partir da
  última cotação; precedência **manual > ajuste "S" da CMA > último**. Fechamento pode ser
  corrigido à mão.

## 7. Deixado ao builder
- Modelagem exata das tabelas, nomes, serviços e telas, seguindo os padrões dos `CLAUDE.md`.
- Onde roda o cliente CMA (Web vs worker) e estratégia de reconexão.
- Layout das telas (seguir padrões UI5 existentes: listas com FilterBar, ObjectPage).

## 8. Decisões do Claude tomadas na execução (revisáveis pelo usuário)

### Parte 2 (2026-11-06)
- **Fórmula única**: prêmio sempre na unidade da cotação e somado à bolsa antes de qualquer ajuste; juros
  sempre sobre o preço porto. Reproduz o Tagui ao centavo para soja e milho CBOT; no milho B3 diverge
  centavos (o Tagui somava o prêmio depois do desconto e da margem). Frete por saca = R$/t × 0,06.
- **Dólar**: reta entre o dólar à vista (instrumento definido nos Parâmetros do Mercado) e o ponto da
  curva escolhido na linha, avaliada na data de pagamento, menos o "desconto do dólar" (NDF do Tagui).
- **Cotação usada**: compra (bid) quando existe; senão o último.
- **Nada materializado**: a grade é recalculada a cada leitura; linha sem cotação, dólar ou frete aparece
  com a situação e sem preço.
- **Tabela de frete**: uma linha por município × destino × "vigente a partir de"; a vigente é a mais
  recente não futura; as antigas são o histórico.
- **Boleta**: nasce já enviada para aprovação (sem rascunho); não é editável (cancela e refaz); número =
  sequência própria simples. Quantidade em sacas de 60 kg na tela, kg no banco.
- **Alçada**: dois limiares por empresa (volume em t, desvio % do preço negociado × calculado). Boleta que
  passa de um deles precisa também da diretoria. O estouro de limite de exposição entra na parte 4.
- **Permissões** em vez de só papéis: `MARKET_MANAGE`, `MARKET_TICKET_APPROVE`, `MARKET_BOARD_APPROVE`,
  com os papéis `MARKET_TRADER` e `MARKET_BOARD` semeados. A partir daqui, gravar instrumento e lançar
  cotação manual exigem `MARKET_MANAGE`.
- **Contrato gerado**: preço fixo, FOB, moeda R$, previsão de pagamento = data de pagamento da linha,
  unidade vinda dos Parâmetros do Mercado; passa pelos serviços existentes (criar → enviar → aprovar), então
  gera as mesmas notificações e o título provisório de um contrato manual.
