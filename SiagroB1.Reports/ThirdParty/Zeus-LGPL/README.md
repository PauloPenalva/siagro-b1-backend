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
