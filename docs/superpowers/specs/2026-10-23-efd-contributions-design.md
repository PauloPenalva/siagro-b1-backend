# EFD Contribuições (PIS/COFINS) — geração do arquivo

Data: 2026-10-23 · Status: desenho. O usuário pulou as perguntas ("seguir"); o que não foi dito por ele
está marcado **[minha decisão]**.

## Intenção

2ª das 4 obrigações pedidas (SPED Fiscal → **EFD Contribuições** → ECD/ECF → GIA/DeSTDA). Gerar, por
empresa (CNPJ raiz) e mês, o TXT da EFD Contribuições importável no PVA, a partir das notas já
escrituradas, nos dois regimes: **cumulativo** (CEAGUI hoje: filiais 01 e 02, vendas CST 06/08/09,
compras CST 74/50) e **não cumulativo** (Tagui, alvo da troca do Protheus).

## Metas
- `POST /reports/EfdContributions/Check` (JSON) e `POST /reports/EfdContributions` (TXT ISO-8859-1),
  em SiagroB1.Reports, só STANDALONE.
- Tela Fiscal → "EFD Contribuições": mês/ano, finalidade (0 original / 1 retificadora + nº do recibo
  anterior), Verificar (bloqueantes, avisos, totais PIS/COFINS) e Gerar arquivo — mesmo comportamento da
  tela do SPED Fiscal (botão só com verificação atual sem bloqueantes).
- Arquivo cobre **todas as filiais com o mesmo CNPJ raiz** da filial escolhida (um 0140 e um C010 por
  estabelecimento); a matriz (CNPJ /0001) é o declarante do 0000.
- Natureza de operação (`USAGES`) ganha dois campos opcionais: **Natureza da receita (tabela 4.3.x)**
  e **Base de cálculo do crédito (NAT_BC_CRED, tabela 4.3.7)**.

## Reaproveitamento
`SpedRecordWriter` (Fiscal), `FiscalConferenceService.LoadLinesAsync` + `BookableInvoiceLines`, a lógica
de participantes/itens/unidades do `SpedFiscalDataLoader` (extrair o que for comum em vez de copiar), o
padrão `SpedFiscalValidator`/`SpedFiscalFileService`/controller, e os dados do contabilista de
`SpedFiscalSettings` da filial matriz.

## Registros [minha decisão, Guia Prático EFD-Contribuições, leiaute vigente em 2026]
- 0000 (COD_VER pela tabela do período, TIPO_ESCRIT 0/1, IND_SIT_ESP vazio, NUM_REC_ANTERIOR se 1,
  IND_NAT_PJ "00", IND_ATIV pela ActivityType dos parâmetros: 0 industrial→"0", 1 outros→"2" comércio),
  0001, 0100 (contabilista), 0110 (COD_INC_TRIB: 1 não cumulativo, 2 cumulativo, 3 ambos — derivado do
  `PisCofinsRegime` das filiais; IND_APRO_CRED 1; COD_TIPO_CONT 1; IND_REG_CUM 9 só quando há cumulativo),
  0140 por estabelecimento com seus 0150/0190/0200 aninhados (hierarquia do leiaute da EFD
  Contribuições: participantes, unidades e itens ficam sob o estabelecimento que os usa), 0990.
- A001 `1`. C001, C010 por estabelecimento, C100 + C170 para todas as NF-e (própria e de terceiro;
  sem consolidação C180/C190) com campos de PIS/COFINS da linha, C990. D001 `1` (CT-e fica fora, como no
  SPED Fiscal). F001 `1`. I001 `1`.
- M: não cumulativo → M100/M105 (crédito básico pela linha de entrada com CST 50-56, NAT_BC_CRED da
  natureza), M200/M205/M210 (contribuição sobre receitas CST 01/02 pela alíquota da linha), M400/M410
  (receitas CST 04/06/07/08/09 por natureza da receita), e o mesmo para COFINS em M500/M505/M600/M605/
  M610/M800/M810. Cumulativo → só M200/M205/M210 (COD_CONT 51) e M400/M410 (idem COFINS).
  M205 com código de receita DCTF: PIS 6912 / COFINS 5856 (não cumulativo), PIS 8109 / COFINS 2172
  (cumulativo).
- P001 `1`. 1001 `1` exceto quando sobra crédito do mês (1100 PIS e 1500 COFINS só do período corrente).
- 9 pelo writer.

## Fora do escopo (não-metas)
- Crédito presumido da agroindústria (Lei 10.925), créditos de ativo imobilizado/energia/aluguel (F120,
  F130, F100), retenções na fonte (F600), CPRB (bloco P), CT-e (bloco D), consolidação C180/C190.
- Saldos de crédito de períodos anteriores (1100/1500 só nascem do próprio mês) e ajustes M110/M220.
- Recálculo de tributo: lê o que está gravado na linha.

## Anti-metas
- Divergir da Conferência Fiscal do mesmo mês nos valores de PIS/COFINS das linhas.
- Gerar arquivo que o PVA rejeita por falta de NAT_REC/NAT_BC_CRED: isso é **bloqueante** na verificação.

## Bloqueantes
Os do SPED Fiscal que se aplicam (filial/participante/produto/chave/contabilista); linha sem CST de
PIS ou COFINS; receita CST 04/06/07/08/09 cuja natureza não tem "Natureza da receita"; crédito CST
50-56 em filial não cumulativa cuja natureza não tem "Base de cálculo do crédito"; filiais do mesmo
CNPJ raiz sem matriz (/0001) cadastrada; matriz sem Parâmetros do SPED Fiscal.

## Deixado ao implementador
Nomes de classes, extração do código comum com o SPED Fiscal, layout da tela (padrão SPED Fiscal), testes.
