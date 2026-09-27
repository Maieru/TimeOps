# TimeOps

Painel local para comparar a capacidade da sprint com as horas `Completed` das Tasks no Azure DevOps Services. O cálculo e as decisões de negócio estão em [docs/](docs/README.md).

## Executar

Requisitos: .NET 10 SDK e um PAT do Azure DevOps com os escopos `Work Items (Read)` (`vso.work`) e `Project and Team (Read)` (`vso.project`). O titular do PAT precisa ter acesso de leitura à organização e ao projeto.

Na raiz do projeto, execute:

```powershell
dotnet run --project src/TimeOps.Web
```

Abra `http://127.0.0.1:5191` e informe a organização e o PAT no formulário **Conectar ao Azure DevOps**. Em seguida, selecione projeto, equipe e sprint. No Windows, **Manter conexão neste computador** vem marcado: a organização e o PAT são salvos no Gerenciador de Credenciais do Windows para o usuário que executa o aplicativo. Ao recarregar a página ou reiniciar o TimeOps, a conexão é recuperada. Desmarque a opção para usar o PAT apenas na sessão atual.

**Esquecer e trocar conexão** remove a credencial salva e descarta o PAT da sessão atual. Outras abas já abertas podem continuar com a conexão em memória até serem fechadas. Em sistemas sem Gerenciador de Credenciais do Windows, o PAT permanece somente na sessão. O aplicativo escuta apenas em localhost e consulta o DevOps por HTTPS; como o piloto não tem login próprio, use-o somente em um computador local confiável. Nunca coloque o PAT em `appsettings.json` ou no Git. Esta versão não deve ser publicada em rede.

## Validar

```powershell
dotnet test TimeOps.slnx
```

Os testes automatizados cobrem cálculo, casos de uso, leitura simulada da API, persistência com credencial sintética no Windows e dependências entre camadas. A validação contra uma organização real exige um PAT fornecido localmente e uma sprint de teste; ela ainda não foi executada neste ambiente.

## Estrutura

- `TimeOps.Domain`: regras puras, modelos e Result pattern.
- `TimeOps.Application`: casos de uso e contrato de integração.
- `TimeOps.Infrastructure`: REST do Azure DevOps, PAT, cache e tratamento de falhas.
- `TimeOps.Web`: interface Blazor e composição das dependências.

Dados de calendário, capacidade, áreas e Tasks vêm do DevOps. Completed é o valor **atual** do work item, mesmo ao selecionar uma data de referência anterior.
