# TimeOps — produto

<!-- impeccable:product-schema 1 -->

## Platform
web

## Users
Líderes e integrantes de equipes internas, com acesso individual pelo GitHub e permissões por equipe.

## Product Purpose
Planejar ciclos, sprints e capacidades; criar uma tarefa com implementações independentes em vários produtos; acompanhar horas trabalhadas e execução no GitHub Projects.

## Positioning
O TimeOps controla planejamento e apontamentos. O GitHub controla issues, responsáveis e status. Cada número deve ser rastreável até a implementação, o apontamento e a configuração de capacidade que o compõem.

## Operating Context
Aplicação compartilhada em C#/.NET 10 e Blazor com interatividade no servidor, banco PostgreSQL e uma organização do GitHub.com. Login com GitHub, acessos concedidos por administrador. Instalação em uma instância; desenvolvimento por Docker Compose.

## Capabilities and Constraints
- Equipes têm calendários, funções configuráveis, folgas e capacidades por pessoa/função/sprint.
- Ciclos possuem quantidades configuráveis de sprints, sem sobreposição na mesma equipe.
- Produtos ligam um repositório a um Project existente. Tarefa central agrupa implementações; cada implementação recebe uma issue independente.
- Horas pertencem a quem trabalhou e à sprint do apontamento. Reatribuir uma issue ou replanejar não transfere essas horas.
- Estimativa, realizado e restante são independentes. Capacidade não é multiplicada pela quantidade de produtos.
- Painéis de equipe e pessoa, hierarquia por tarefa, timeline, histórico, burndown e Excel preservam a rastreabilidade.
- Publicação usa fila persistente e reconciliação. O sistema não repete automaticamente uma criação cujo resultado permaneça incerto.
- Capacidade, ciclos, sprints e horas ficam no TimeOps. Não há importação automática de dados do Azure DevOps nem sincronização desses campos para o Projects.

## Brand Commitments
Nome TimeOps, interface em português, horas decimais com duas casas na apresentação, fuso inicial America/Sao_Paulo. Preservar a identidade visual escura existente, com superfícies azuladas, tabelas legíveis e navegação lateral que se adapta ao mobile.

## Evidence on Hand
Código e testes em src/ e tests/. O fluxo integrado é verificado com GitHub simulado; a instalação real requer as credenciais e permissões do GitHub App da organização.

## Product Principles
1. Separar fonte de planejamento e fonte de execução sem copiar horas entre produtos.
2. Diferenciar zero, dado ausente, sincronização pendente e consulta incompleta.
3. Preservar autoria, sprint e auditoria dos apontamentos.
4. Apoiar planejamento; não classificar automaticamente produtividade individual.

## Accessibility & Inclusion
Rótulos explícitos, foco visível, estados de carregamento e erro, tabelas com rolagem interna em telas pequenas e informações que não dependem apenas de cor.
