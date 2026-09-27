# TimeOps

Painel local para comparar a capacidade da sprint com as horas `Completed` das Tasks no Azure DevOps Services. O cálculo e as decisões de negócio estão em [docs/](docs/README.md).

## Executar

Requisitos: .NET 10 SDK e um PAT do Azure DevOps com os escopos `Work Items (Read)` (`vso.work`) e `Project and Team (Read)` (`vso.project`). O titular do PAT precisa ter acesso de leitura à organização e ao projeto.

Na raiz do projeto, execute:

```powershell
dotnet run --project src/TimeOps.Web
```

Abra `http://127.0.0.1:5191` e informe a organização e o PAT no formulário **Conectar ao Azure DevOps**. Em seguida, selecione projeto, equipe e sprint. O botão **Trocar conexão** descarta o PAT da sessão e permite informar outro.

O aplicativo escuta apenas em localhost. O formulário envia o PAT ao processo Blazor local, que o mantém em memória para aquela sessão e consulta o DevOps por HTTPS. Recarregar a página ou reiniciar o aplicativo exige informar o PAT novamente. Nunca coloque o PAT em `appsettings.json` ou no Git. Esta versão não deve ser publicada em rede.

## Validar

```powershell
dotnet test TimeOps.slnx
```

Os testes automatizados cobrem cálculo, casos de uso, leitura simulada da API e dependências entre camadas. A validação contra uma organização real exige um PAT fornecido localmente e uma sprint de teste; ela ainda não foi executada neste ambiente.

## Estrutura

- `TimeOps.Domain`: regras puras, modelos e Result pattern.
- `TimeOps.Application`: casos de uso e contrato de integração.
- `TimeOps.Infrastructure`: REST do Azure DevOps, PAT, cache e tratamento de falhas.
- `TimeOps.Web`: interface Blazor e composição das dependências.

Dados de calendário, capacidade, áreas e Tasks vêm do DevOps. Completed é o valor **atual** do work item, mesmo ao selecionar uma data de referência anterior.
