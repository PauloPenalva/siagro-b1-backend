# Conferência Fiscal — design

Data: 2026-10-20 · Modo: só STANDALONE · Repos: backend + frontend

## Por quê

Ninguém gera SPED Fiscal nem apuração de ICMS da CEAGUI hoje, e o usuário dispensou um livro
fiscal estilo Protheus (SFT/SF3). Antes de qualquer arquivo SPED/REINF, ele quer **conferir** as
notas de entrada e saída, CFOP e tributos do mês — "basicamente uma conferência do SPED Fiscal e
do evento R-2055". É a 1ª de 3 entregas (depois: Kardex fiscal; inventário com custo).

## O que é

Uma tela **Conferência Fiscal** no menu **Fiscal**, chamada direto do menu (pedido do usuário:
"sempre que possível cada tela poder ser chamada direto do menu"). Filtros: **filial** (opcional)
e **mês/ano** (obrigatório). Botão Consultar; resultado em abas, cada uma com grade
(`sap.ui.table.Table`) e exportação Excel:

| Aba | Equivale a | Conteúdo |
|---|---|---|
| Documentos | C100 + C170 | uma linha por item: E/S, data, modelo, série, número, chave, parceiro, CFOP, CST ICMS, base/alíq./ICMS, CST PIS/COFINS e valores, valor do item |
| Resumo | C190 | agrupado por E/S + CST ICMS + CFOP + alíquota: valor da operação, base, ICMS |
| Apuração ICMS | base do E110 | por CFOP: débitos (saídas) e créditos (entradas); totais e saldo (débitos − créditos) |
| R-2055 | prévia do evento | por produtor: CPF/CNPJ, tipo de produtor, base, INSS, RAT, SENAR, total — líquido de devoluções emitidas no mês |
| Inconsistências | — | uma linha por problema: documento, item, regra violada |

## Regras

- **Documentos escrituráveis**: situação Confirmado ou Retornado (fora Pendente e Cancelado);
  NF-e com situação Cancelada, Denegada ou Inutilizada fica fora; saída `WithoutTaxDocument` fica
  fora; `TaxDocumentKind` Nfse e Cte ficam fora (ISS e bloco D não são escopo).
- **Data do período**: saída = `InvoiceDate`; entrada = `PostingDate` (entrada/saída do SPED),
  caindo para `IssueDate` quando nulo.
- **Direção da linha pelo CFOP**, não pelo tipo de documento: 1/2/3 = entrada (crédito),
  5/6/7 = saída (débito). Devolução de compra emitida como entrada própria cai como saída pelo CFOP.
- Complementos (valor/ICMS) entram como documentos normais.
- Modelo: Nfe → "55"; Other → "01".
- **R-2055** é calculado direto no `FiscalConferenceService` (fotografia gravada na linha; período
  pela emissão), agrupado por produtor. A devolução é abatida no mês da DEVOLUÇÃO, não no mês da
  origem (prática da EFD-Reinf; decisão do usuário em 20/10), com o Funrural da própria linha da
  devolução, atribuído ao produtor da linha de origem. Produtor só com devolução no mês aparece negativo.
  O relatório avulso de Funrural continua abatendo na origem.
- **Inconsistências** (regras fixas):
  1. item sem CFOP; 2. item sem CST ICMS; 3. CFOP de direção contrária ao documento
  (entrada com CFOP 5/6/7 e não é devolução; saída com CFOP 1/2/3 e não é devolução);
  4. |base × alíquota ÷ 100 − ICMS| > 0,01; 5. NF-e (modelo 55) sem chave de 44 dígitos;
  6. NF-e própria confirmada sem estar Autorizada (fica fora dos totais e é listada aqui).

## Fora do escopo (decisão)

IPI e ICMS-ST (não existem no modelo); CT-e/bloco D; NFS-e; ajustes do E111, saldo credor
anterior e GIA; geração de arquivo SPED/REINF; PDF (só tela + Excel — YAGNI até pedirem).

## Como (decisões do Claude)

- Backend em `SiagroB1.Reports` (já tem EF, `ErpMode` e retorno JSON como o Dashboard):
  `POST /reports/FiscalConference` devolve JSON. Gateway já proxia `/reports/**`. Recusa em SAPB1.
- Menu: migration em `CommonContext`, key `fiscalConference` (= nome da rota), filho de `fiscal`,
  Order 5, StandaloneOnly, `ROLE_MENUS` ADMIN.
