# TimeOps

## Objetivo

O TimeOps será um aplicativo Web em C# para acompanhar métricas da sprint a partir do Azure DevOps Services. A principal informação será a comparação, por pessoa, entre a capacidade esperada nos dias decorridos e a soma de Completed nas Tasks atribuídas a ela.

Exemplo: uma pessoa com capacidade de 8 horas por dia, após 10 dias úteis sem folgas, terá 80 horas esperadas. Se suas Tasks somarem 64 horas em Completed, o aplicativo exibirá diferença de −16 horas e cobertura de 80%.

Os indicadores apoiam o acompanhamento de capacidade, planejamento e preenchimento de dados. Não constituem avaliação automática de produtividade.

## Documentos

- [Requisitos de negócio](requisitos-negocio.md): escopo, fórmulas, regras e critérios de aceite.
- [Requisitos técnicos](requisitos-tecnicos.md): Clean Architecture, integração, segurança, operação e testes.

O aplicativo e os testes estão implementados; os documentos registram as regras e os limites do piloto.

## Decisões aprovadas

| Tema | Decisão |
| --- | --- |
| Fonte de verdade | Azure DevOps Services, com integração somente de leitura |
| Abrangência | Uma organização; seleção de projeto, equipe e sprint; uma equipe por consulta |
| Público | Ferramenta destinada à equipe interna, com piloto usado localmente por uma pessoa |
| Acesso inicial | Localhost; organização e PAT informados no aplicativo; persistência opcional no Gerenciador de Credenciais do Windows por usuário, sem login próprio no piloto |
| Plataforma | C#, .NET 10 LTS e Blazor Web App interativo no servidor |
| Arquitetura | Clean Architecture obrigatória, com quatro projetos e dependências verificadas |
| Result pattern | Obrigatório em todas as camadas para representar sucesso e falhas esperadas das operações |
| Persistência | Sem banco de dados; credencial opcional no cofre do Windows e cache de métricas temporário em memória |
| Data de referência | Ontem por padrão; seleção de outra data até hoje |
| Calendário e capacidade | Exclusivamente os dados cadastrados no DevOps |
| Atribuição de horas | Retrato atual das Tasks, sem reconstrução histórica |
| Apresentação | Português, horas com duas casas decimais e fuso inicial America/Sao_Paulo |

## Premissas e limitações

- Os campos de esforço e a capacidade são preenchidos em **horas**. Story points não serão convertidos em horas.
- A equipe mantém seus dias de trabalho, folgas e capacidade no DevOps; feriados precisam estar representados nas folgas.
- A capacidade atualmente cadastrada será aplicada a todo o período calculado, mesmo que tenha sido alterada durante a sprint.
- Completed é um total atual do work item, não um apontamento diário por pessoa. Transferências de responsável e de sprint podem deslocar todo esse total na visualização.
- Alterar a data de referência modifica a expectativa de capacidade, mas não recupera o valor histórico de Completed.
- O acesso compartilhado não faz parte do piloto: requer uma etapa própria de autenticação e hospedagem.

## Glossário

| Termo | Significado no TimeOps |
| --- | --- |
| Sprint ou iteração | Período e agrupamento de trabalho configurados no DevOps |
| Task | Work item do tipo Task, unidade de soma dos campos de esforço |
| Capacidade diária | Soma das capacidades das atividades de uma pessoa na sprint |
| Horas esperadas | Capacidade acumulada nos dias elegíveis até a data de referência |
| Completed | Campo Microsoft.VSTS.Scheduling.CompletedWork, apresentado como horas registradas |
| Original Estimate | Campo Microsoft.VSTS.Scheduling.OriginalEstimate, estimativa original |
| Remaining Work | Campo Microsoft.VSTS.Scheduling.RemainingWork, trabalho restante |
| Cobertura | Razão entre horas registradas e horas esperadas |
| Retrato atual | Dados presentes no DevOps no momento da coleta, sem reconstrução do passado |
| PAT | Personal Access Token utilizado pelo servidor para autenticar consultas |

## Evoluções fora do piloto

Acesso simultâneo da equipe, login corporativo, consolidação entre equipes, séries históricas, notificações e exportação. O piloto também não altera work items ou configurações do DevOps.

## Referências oficiais

- [.NET: versões e suporte](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [Arquitetura de aplicações Web com ASP.NET Core](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Conceitos de sprints e capacidade](https://learn.microsoft.com/en-us/azure/devops/boards/sprints/scrum-key-concepts?view=azure-devops)
- [Campos numéricos de esforço e planejamento](https://learn.microsoft.com/en-us/azure/devops/boards/queries/query-numeric?view=azure-devops)
- [REST API: capacidade por pessoa e sprint](https://learn.microsoft.com/en-us/rest/api/azure/devops/work/capacities/get-capacities-with-identity-ref-and-totals?view=azure-devops-rest-7.1)
- [REST API: configurações de trabalho da equipe](https://learn.microsoft.com/en-us/rest/api/azure/devops/work/?view=azure-devops-rest-7.1)
- [REST API: folgas da equipe](https://learn.microsoft.com/en-us/rest/api/azure/devops/work/teamdaysoff/get?view=azure-devops-rest-7.1)
- [Autenticação com PAT](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops)
- [REST API: atualizações de work items](https://learn.microsoft.com/en-us/rest/api/azure/devops/wit/updates/list?view=azure-devops-rest-7.1)
- [Gerenciamento seguro de credenciais no Windows](https://learn.microsoft.com/en-us/windows/win32/secbp/handling-passwords)
- [Autenticação com Microsoft Entra ID, para evolução futura](https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/entra?view=azure-devops)
