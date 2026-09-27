# TimeOps — produto

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Líderes e integrantes da equipe são os usuários previstos e consultam a mesma visão da sprint. No piloto, uma pessoa executa o aplicativo localmente; acesso simultâneo da equipe fica para uma etapa posterior.

## Product Purpose

O TimeOps permite acompanhar a capacidade planejada, as horas registradas e o trabalho restante de uma sprint do Azure DevOps Services. A comparação central mostra, por pessoa, as horas esperadas até uma data de referência, as horas em `Completed Work`, a diferença e a cobertura. Quando esses números divergem, o objetivo principal é identificar dados ausentes e ajustar o planejamento da sprint. Sucesso significa conseguir interpretar a diferença, localizar as Tasks que a compõem e reconhecer quando faltam dados para uma conclusão confiável.

## Positioning

O painel combina a capacidade diária, o calendário e as folgas cadastrados no DevOps com os campos de esforço das Tasks no escopo da equipe. Ele explicita a data usada para calcular capacidade, o horário da coleta e os avisos de dados incompletos. Essa combinação permite comparar horas sem presumir uma jornada padrão nem apresentar um total parcial como completo.

## Operating Context

O usuário abre o aplicativo em localhost, informa a organização e um PAT de leitura durante a execução, seleciona projeto, equipe e sprint e consulta o painel. A sprint atual é selecionada automaticamente quando há uma única correspondência. A referência de capacidade é ontem por padrão e pode ser alterada até hoje. O usuário pode atualizar os dados manualmente e abrir as Tasks para conferir seus campos e links no DevOps.

O resultado é um retrato dos work items no momento da consulta. A data de referência altera a capacidade calculada, mas não reconstrói os valores históricos de `Completed Work` nem distribui horas entre responsáveis anteriores.

## Capabilities and Constraints

- O Azure DevOps Services é a fonte de verdade para Tasks, áreas, iteração, identidades, calendário, capacidade e folgas. O TimeOps consulta esses dados sem alterá-los.
- O piloto funciona em uma aplicação Web C#/.NET 10 com Blazor interativo no servidor, restrita a localhost, sem banco de dados e sem login próprio. O PAT permanece somente na memória da sessão do servidor.
- A arquitetura segue Clean Architecture em quatro projetos e usa Result pattern em todas as camadas.
- Os cálculos usam horas decimais. A capacidade esperada soma atividades por dia elegível e desconta folgas; não assume oito horas quando a capacidade está ausente.
- `Completed Work`, `Original Estimate` e `Remaining Work` são métricas distintas. Campos vazios contribuem com zero e geram aviso; campos indisponíveis e consultas incompletas não devem parecer totais válidos.
- A interface apresenta métricas por pessoa e equipe, Tasks de cada soma, contagem por categoria de estado e avisos de qualidade dos dados.
- Não há escrita no DevOps, reconstrução histórica, consolidação entre equipes, notificações ou exportação no piloto.

## Brand Commitments

O nome do produto é **TimeOps**. A interface e as explicações de métricas usam português. Os indicadores comunicam horas com duas casas decimais, unidades explícitas e o significado de valores indisponíveis.

## Evidence on Hand

Os requisitos e critérios de aceite estão em [docs/requisitos-negocio.md](docs/requisitos-negocio.md) e [docs/requisitos-tecnicos.md](docs/requisitos-tecnicos.md). O aplicativo existente em `src/` demonstra o fluxo local e os cálculos; os testes automatizados em `tests/` verificam regras e integração simulada. A validação com uma organização real e um PAT não está documentada como concluída no repositório.

## Product Principles

1. Manter o DevOps como fonte de verdade e tornar cada total rastreável até suas Tasks e configurações de capacidade.
2. Distinguir zero, dado ausente, campo indisponível e consulta incompleta antes de apresentar comparações.
3. Explicar o significado temporal dos números: capacidade até a referência e esforço no estado atual.
4. Apoiar correção de dados e planejamento, sem classificar automaticamente a produtividade individual.

## Accessibility & Inclusion

As métricas devem trazer rótulos, unidades e avisos textuais; a interpretação não pode depender apenas de cor. As explicações das métricas devem ser acessíveis por teclado e toque.
