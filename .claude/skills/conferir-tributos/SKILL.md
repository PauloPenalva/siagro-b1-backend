---
name: conferir-tributos
description: Confere (audita) os tributos de NF-e do Siagro B1 — ICMS (redução, diferimento CST 51, alíquota interestadual), PIS/COFINS (Tema 69, suspensão de cereais), IBS/CBS da reforma 2026, totais da nota, Funrural/Senar/RAT, retenções e DIFAL — recalculando a partir do XML e comparando com as regras do motor fiscal do backend. Use sempre que o usuário pedir para conferir, validar, auditar, revisar ou "bater" impostos/tributos de uma nota fiscal, XML, procNFe, DANFE ou nota de entrada/saída/devolução/contranota, quando perguntar por que um valor de ICMS/PIS/COFINS/CBS/IBS/Funrural ficou diferente do esperado, quando suspeitar de rejeição da SEFAZ por valores, ou quando quiser comparar o que o Siagro calculou com o XML autorizado ou com o XML do fornecedor — mesmo que não use a palavra "conferir".
---

# Conferir tributos de NF-e (Siagro B1)

O objetivo é dizer, com evidência, **se os tributos de uma nota estão certos e, se não estiverem,
onde está o erro**. Essa pode ser a conta do XML, a regra do motor, o cadastro (natureza de operação)
ou o XML do fornecedor. A pessoa que pede normalmente é o desenvolvedor do Siagro ou alguém do fiscal,
e quer o diagnóstico para corrigir — não uma aula de tributação.

## Fluxo

1. **Achar o(s) XML(s).** Pode ser um arquivo que o usuário indicou, uma pasta, ou uma nota no banco.
   Para nota no banco/API, veja a seção 6 de `references/regras-siagro.md` (tabelas `*_NFE_XMLS`,
   `Kind=2`, ou o endpoint `NfeXml`). XMLs de homologação ficam em `../nfe-homologacao-testes/` e
   `../dados-ceagui-efiscloud*/`.

2. **Rodar o conferidor** — ele faz a aritmética de forma determinística, então não refaça as contas na mão:
   ```bash
   python .claude/skills/conferir-tributos/scripts/conferir_nfe.py <arquivo.xml|pasta> [...]
   ```
   (caminho relativo a `siagro-b1-backend/`; use `--json` se for processar a saída).
   O script recalcula item a item: vProd; base/valor de ICMS com redução; ICMS51 (vICMSOp,
   vICMSDif, vICMS); alíquota interestadual; FCP/ST quando existirem; IPI; PIS/COFINS com
   detecção do Tema 69; IBS/CBS com gRed e pAliqEfet; coerência de CST; e todos os totais
   (ICMSTot, vNF, IBSCBSTot). Aceita arredondamento half-up e half-even dentro de 1 centavo.
   Eventos e CT-e são ignorados.

3. **Interpretar à luz das regras do Siagro.** Leia `references/regras-siagro.md` — especialmente
   quando houver ALERTA, quando a nota foi emitida pelo Siagro (emitente = filial do cliente), ou
   quando a pergunta é "o Siagro calculou certo?". O script conhece a aritmética genérica da NF-e;
   a referência explica o que o motor do Siagro **deveria** produzir (fórmulas, ordem de
   arredondamento, o que ele não suporta). Um ALERTA pode ser comportamento esperado do Siagro.
   Por exemplo, o Siagro recalcula notas de terceiros pela natureza de operação.

4. **Comparar com o backend quando fizer sentido.** Se a dúvida é "o XML bate com o que está na
   tela/banco?", leia o snapshot das linhas (`*_INVOICES_ITEMS`) e compare campo a campo com o XML.
   O `NfeXmlBuilder` só copia o snapshot, então diferença ali é bug de mapeamento. Diferença
   entre snapshot e fórmula é bug do motor ou de cadastro. Confirme no código citado antes de
   apontar culpado — os números de linha da referência podem ter mudado.

5. **Classificar a causa** de cada divergência relevante em uma destas:
   - **Conta do XML** — valor ≠ base × alíquota, total ≠ soma dos itens.
   - **Motor do Siagro** — a fórmula aplicada difere da especificação/código.
   - **Cadastro** — natureza de operação, NCM, origem, alíquota IBS/CBS ou UF errados.
   - **XML do fornecedor** — erro de quem emitiu, não do Siagro.
   - **Regra tributária a confirmar** — enquadramento que depende da legislação da UF
     (diferimento, cBenef, suspensão). Para esses casos, **sinalize e recomende confirmar com o
     contador**, em vez de afirmar.

## Regras de cereais que valem checar sempre

- **ICMS diferido (CST 51)** em operações internas com grãos é comum (ex.: SP). Confira se o
  `cBenef` foi informado e se vICMSOp − vICMSDif = vICMS.
- **PIS/COFINS suspensos** na compra de cereal de produtor/cerealista para quem industrializa
  (Lei 10.925/2004, art. 9º) — CST 74 na entrada, 09 na saída do vendedor. Uma entrada de grão
  com CST 01/02 merece um alerta.
- **Funrural/RAT/Senar** na compra de produtor rural: não tem campo no XML, só em `infCpl`.
  Confira pelos campos `Funrural*` da linha e pela fórmula da referência.
- **IBS/CBS 2026** é ano de teste: CBS 0,9% e IBS UF 0,1%, com base = operação − ICMS − PIS − COFINS.
  cClassTrib 200036 (produtos agropecuários in natura) leva redução de 60%.

## Formato da resposta

Comece pelo veredito (uma linha por nota: ✅ ok / ⚠️ alertas / ❌ divergências). Depois traga
uma tabela só com o que importa:

| Nota/Item | Campo | Esperado | No XML | Causa provável | O que fazer |
|---|---|---|---|---|---|

Feche com os próximos passos concretos, por exemplo:
- "corrigir a natureza X no cadastro";
- "teste novo em `TaxCalculatorTests` reproduzindo o caso";
- "pedir carta de correção/nota complementar ao fornecedor";
- "confirmar enquadramento com o contador".

Seja direto: o usuário quer saber o que está errado e onde mexer. Não repita os INFO do script
a menos que expliquem uma divergência. Escreva em pt-BR. Se for corrigir código, siga o fluxo
do projeto (`superpowers:systematic-debugging` e TDD, conforme o CLAUDE.md).

## Limites

- Esta skill confere a conta e a coerência. Ela não substitui o enquadramento tributário feito por
  um contador. Benefícios e alíquotas internas variam por UF e mudam com frequência.
- Não altere notas autorizadas, banco ou cadastros só para "fazer bater". Proponha a correção.
- Não exponha o certificado `.pfx`, a senha dele nem as credenciais do `appsettings.json` em
  relatórios ou respostas.
