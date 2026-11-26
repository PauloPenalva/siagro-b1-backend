# DANFE da Zeus (DFe.NET) — LGPL-2.1

Arquivos copiados **sem alteração** de https://github.com/ZeusAutomacao/DFe.NET, commit `08743cd5f5b82769a26ad9ae0093b1b0ae60c74f`,
licença LGPL-2.1 (ver `LICENSE`). O layout `NFeRetrato.frx` e as classes do `NFe.Danfe.OpenFast`
não são publicados no NuGet, por isso estão aqui.

Os `.cs` compilam, sem alteração, em **`NFe.Danfe.Base.dll`** (projeto `NFe.Danfe.Base.csproj`, nesta
pasta). O nome do assembly é o que o `NFeRetrato.frx` referencia no script, e a DLL separada mantém o
código LGPL substituível pelo usuário (LGPL-2.1 §6). O `SiagroB1.Reports` referencia o projeto e não
compila estes arquivos. `LICENSE`, `README.md` e o `.frx` vão para a saída do build e do publish.

Regra: **não editar estes arquivos.** Qualquer alteração obriga a publicar o diff (LGPL). Para
atualizar, copie de novo do repositório e registre o novo commit acima.

## Arquivo modificado

`NFe.Danfe.Base/NFe/NFeRetratoRtc.frx` é obra **derivada** do `NFeRetrato.frx` (mesmo commit da Zeus acima),
modificada em 26/11/2026 para a NT 2026.010 v1.00 (DANFE com a Reforma Tributária). Continua sob LGPL-2.1 e é
entregue aberto, em texto, junto com a instalação. A regra "não editar" vale para todos os demais arquivos desta
pasta. A lista das mudanças feitas no leiaute entra aqui conforme forem aplicadas.
