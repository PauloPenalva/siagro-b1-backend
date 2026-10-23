# ECD (SPED Contábil) — geração do arquivo

Data: 2026-10-23 · Status: desenho. O usuário pulou as perguntas ("seguir"); o que não foi dito por
ele está marcado **[minha decisão]**.

## Intenção

3ª das 4 obrigações (SPED Fiscal → EFD Contribuições → **ECD/ECF** → GIA/DeSTDA). ECD e ECF são dois
projetos: **este é só a ECD**; a ECF (IRPJ/CSLL, LALUR/LACS) vem depois e parte da ECD. Objetivo: gerar,
por empresa (CNPJ raiz) e ano, o TXT da ECD (livro Diário geral, "G") importável no PVA do SPED Contábil,
a partir da contabilidade do próprio Siagro (fases 1-4, modelo Protheus: plano de contas, lançamentos
CT2, apuração de resultado, visões gerenciais BP/DRE) **[minha decisão: caminho principal do roteiro
de 16/10; exportação para sistema do contador é plano B]**.

## Metas
- `POST /reports/Ecd/Check` (JSON) e `POST /reports/Ecd` (TXT), SiagroB1.Reports, só STANDALONE.
- Tela Contabilidade → "ECD (SPED Contábil)": empresa (filial), ano, finalidade (original / substituta),
  Verificar (bloqueantes, avisos, totais débito = crédito, resultado do exercício) e Gerar arquivo — mesmo
  comportamento das telas do SPED Fiscal e da EFD Contribuições.
- Tela "Parâmetros da ECD" por empresa (filial matriz): número de ordem do livro, NIRE e data do
  arquivamento dos atos constitutivos (ou indicação de sem NIRE/cartório), indicador de grande porte,
  código do plano referencial (COD_PLAN_REF), responsável legal que assina (nome, CPF, código de
  qualificação do assinante da tabela da ECD, e-mail, telefone). Contabilista: dos Parâmetros do SPED
  Fiscal da matriz (CRC, CPF, nome, e-mail).

## Registros [minha decisão — Guia/Manual de Orientação do leiaute vigente para o ano-calendário]
- 0000 (LECD, período, nome, CNPJ, UF, IE, município, IND_SIT_ESP vazio, IND_SIT_INI_PER 0, IND_NIRE,
  IND_FIN_ESC, TIP_ECD 0, IND_GRANDE_PORTE, IND_MUDANC_PC 0, COD_PLAN_REF…), 0001, 0007 (outras
  inscrições: vazio/“00”), 0990.
- I001, I010 (IND_ESC "G", COD_VER_LC do leiaute), I030 (termo de abertura), I050 (plano de contas:
  data de inclusão, natureza do grupo 01 ativo/02 passivo/03 PL/04 resultado, sintética/analítica,
  nível, código, conta superior, nome), I051 (plano referencial da conta analítica, com centro de custo
  vazio), I052 (código de aglutinação das contas analíticas = linha da visão BP/DRE que contém a conta),
  I100 (centros de custo usados), I150/I155 (saldo inicial, débitos, créditos e saldo final por mês,
  conta analítica e centro de custo), I200/I250 (cada lançamento; partida dobrada vira duas linhas;
  IND_LCTO "N" normal, "E" encerramento para a apuração do resultado de dezembro), I350/I355 (saldos das
  contas de resultado antes do encerramento, em 31/12), I990.
- J001, J005 (período das demonstrações), J100 (balanço pela visão BP: código de aglutinação, nível,
  superior, grupo A/P, saldos inicial e final com indicador D/C), J150 (DRE pela visão DRE), J900 (termo
  de encerramento), J930 (signatários: responsável legal e contabilista com CRC), J990.
- K001 `1`, K990. 9 pelo `SpedRecordWriter`.

## Fora do escopo (não-metas)
- ECF (projeto seguinte). Livros auxiliares (R, A, Z), livro resumido (B), escrituração
  descentralizada por filial (cada filial só com seus lançamentos) — sai **centralizada** na matriz.
- J210 (DLPA/DMPL), J215, J800/J801 (notas explicativas e termo de verificação), bloco K (consolidação),
  0150/0180 (participantes e relacionamentos), I075 (histórico padronizado), I157 (transferência de plano).
- Assinatura digital e hash: são feitos no PVA.
- Situação especial (cisão, fusão, extinção) e período diferente do ano civil.

## Anti-metas
- Divergir dos relatórios contábeis do Siagro (Balancete, Razão, Balanço, DRE) do mesmo período: os
  saldos I155/J100/J150 têm de bater com eles (mesma fonte de linhas).
- Gerar arquivo com débito ≠ crédito em qualquer lançamento ou mês.

## Bloqueantes
Contabilidade desligada (sem data de corte) ou data de corte depois do início do ano; algum mês do ano
não fechado; conta analítica com movimento sem código referencial; conta sem tipo/natureza; lançamento
desbalanceado; conta analítica com movimento fora de qualquer linha das visões BP/DRE (sem I052);
parâmetros da ECD ou do contabilista ausentes; empresa sem matriz (/0001). Avisos: conta inativa com
movimento; J100 com ativo ≠ passivo + PL (mostra a diferença).

## Reaproveitamento
`SpedRecordWriter` (Fiscal), seleção de filiais por CNPJ raiz da EFD Contribuições (extrair o que for
comum), calculadoras de saldo dos relatórios contábeis (Balancete/Balanço/DRE) — não reescrever regra de
saldo.

## Deixado ao implementador
Nomes de classes, extração do código comum, layout das telas (padrão SPED Fiscal / Parâmetros do SPED),
testes.
