# Requisitos de negócio

## Objetivo e escopo

Oferecer visibilidade sobre capacidade, horas registradas e trabalho restante da sprint, mantendo o Azure DevOps como fonte de verdade. Todos os requisitos abaixo são obrigatórios para o piloto. P0 identifica a métrica central e suas condições de confiabilidade; P1 identifica os indicadores e recursos complementares, também integrantes da primeira versão.

## Consulta e composição dos dados

| ID | Prioridade | Requisito |
| --- | --- | --- |
| RN-01 | P0 | Consultar uma organização Azure DevOps Services, permitindo selecionar projeto, equipe e sprint. |
| RN-02 | P0 | Selecionar automaticamente a sprint atual apenas quando houver uma única correspondência; caso contrário, solicitar seleção explícita. |
| RN-03 | P0 | Usar somente Tasks da iteração selecionada e das áreas configuradas para a equipe, respeitando a inclusão de subáreas. |
| RN-04 | P0 | Considerar cada Task uma vez, pelo seu ID, em qualquer estado. Não agregar horas de outros tipos de work item nem valores herdados de itens pai. |
| RN-05 | P0 | Atribuir o total atual de cada Task ao seu responsável atual, identificado por identidade estável. Não reconstruir autoria ou data de apontamentos. |
| RN-06 | P0 | Exibir a união das pessoas com capacidade e dos responsáveis pelas Tasks da consulta, incluindo pessoas sem tarefas ou sem capacidade. Exibir Tasks sem responsável em grupo separado. |
| RN-07 | P1 | Permitir abrir o detalhamento das Tasks de cada soma, com ID, título, responsável, estado, campos de esforço e link para o DevOps. |
| RN-08 | P0 | Exibir contexto, data de referência e horário da coleta, identificando o resultado como retrato atual. |

## Calendário e capacidade

**RN-09 — Fonte do calendário (P0).** Utilizar dias de trabalho da equipe, limites da sprint, capacidade por atividade e folgas individuais e coletivas cadastrados no DevOps. Não cadastrar calendário paralelo no TimeOps.

**RN-10 — Data de referência (P0).** Usar ontem no fuso configurado como padrão. Permitir uma data até hoje. Considerar o início e o fim da sprint inclusivos: datas anteriores à sprint resultam em zero dias decorridos; datas posteriores ao fim não acumulam capacidade adicional.

**RN-11 — Dias elegíveis (P0).** Um dia será elegível para uma pessoa se estiver dentro da sprint, for dia de trabalho da equipe e não pertencer à união das folgas individuais e coletivas. Intervalos de folga incluem início e fim. Folgas sobrepostas descontam cada dia uma única vez.

**RN-12 — Capacidade diária (P0).** Somar a capacidade das atividades da pessoa. Por exemplo, 6 h de desenvolvimento e 2 h de testes equivalem a 8 h/dia. Usar a configuração atual durante todo o período; não inferir alterações históricas.

## Métricas

Considere `C(p)` a capacidade diária da pessoa, `D(p)` a quantidade de dias elegíveis até a data de referência, `T(p)` todos os dias elegíveis da sprint e `F(p)` os dias elegíveis estritamente posteriores à referência.

| ID | Prioridade | Indicador | Cálculo |
| --- | --- | --- | --- |
| RN-13 | P0 | Horas esperadas | `E(p) = C(p) × D(p)` |
| RN-14 | P0 | Horas registradas | `H(p) = soma de CompletedWork das Tasks da pessoa` |
| RN-15 | P0 | Diferença de horas | `H(p) − E(p)`; negativa significa registro abaixo da expectativa |
| RN-16 | P0 | Cobertura | `100 × H(p) / E(p)`; não aplicável se `E(p) = 0` |
| RN-17 | P1 | Capacidade total | `C(p) × T(p)` |
| RN-18 | P1 | Capacidade restante | `C(p) × F(p)` |
| RN-19 | P1 | Trabalho restante | Soma de RemainingWork das Tasks da pessoa |
| RN-20 | P1 | Saldo de capacidade futura | Capacidade restante menos trabalho restante; negativo indica que o restante informado excede a capacidade futura |
| RN-21 | P1 | Estimativa original | Soma de OriginalEstimate, exibida separadamente de CompletedWork e RemainingWork |
| RN-22 | P1 | Progresso por quantidade | Contagem por categoria de estado; percentual de Tasks na categoria Completed sobre todas as Tasks da consulta; sem Tasks, não aplicável |

**RN-23 — Totais da equipe (P0).** Somar os valores das pessoas e incluir as horas das Tasks sem responsável no total de trabalho, discriminando esse grupo. Deduplicar por Task ID. Calcular a cobertura da equipe dividindo a soma de horas registradas pela soma de horas esperadas, nunca pela média dos percentuais individuais. Se faltar capacidade para alguma pessoa envolvida, sinalizar o total de capacidade conhecido como parcial e não apresentar diferença ou cobertura global como completas.

**RN-24 — Significado temporal (P0).** A data de referência afeta somente a capacidade esperada e restante. Os campos de esforço e as atribuições continuam atuais. A interface deve deixar explícita essa diferença, inclusive ao consultar sprints passadas. Não denominar o resultado como horas efetivamente trabalhadas até a data selecionada.

## Qualidade dos dados e apresentação

| ID | Prioridade | Regra |
| --- | --- | --- |
| RN-25 | P0 | Capacidade ausente resulta em expectativa, diferença e cobertura indisponíveis para a pessoa, com aviso. Não assumir 8 h. Capacidade explicitamente zero é válida. |
| RN-26 | P0 | Campo de esforço existente mas vazio contribui com zero para sua soma e gera aviso. Campo indisponível no processo torna a respectiva métrica e seus derivados indisponíveis. |
| RN-27 | P1 | Sinalizar Tasks sem responsável e Tasks concluídas com RemainingWork maior que zero. Não corrigir valores no DevOps. |
| RN-28 | P0 | Consultas incompletas ou sem dados essenciais de calendário não podem gerar totais aparentemente completos. Informar a limitação e permitir nova tentativa. |
| RN-29 | P1 | Apresentar português e horas decimais com duas casas. Preservar precisão nos cálculos e arredondar apenas na apresentação. |
| RN-30 | P0 | Não tratar horas registradas ou cobertura como classificação automática de produtividade individual. |

## Critérios de aceite

| ID | Cenário | Resultado esperado | Requisitos |
| --- | --- | --- | --- |
| CA-01 | 8 h/dia, 10 dias elegíveis e Completed total de 80 h | Expectativa 80 h, diferença 0 h, cobertura 100% | RN-13 a RN-16 |
| CA-02 | Mesma capacidade, Completed de 64 h | Expectativa 80 h, diferença −16 h, cobertura 80% | RN-13 a RN-16 |
| CA-03 | Mesma capacidade, Completed de 96 h | Expectativa 80 h, diferença +16 h, cobertura 120% | RN-13 a RN-16 |
| CA-04 | Dez dias de trabalho, com uma folga individual também registrada como coletiva | Nove dias elegíveis; expectativa 72 h para 8 h/dia | RN-09 a RN-13 |
| CA-05 | Atividades de 6 h/dia e 2 h/dia por dez dias elegíveis | Expectativa 80 h, sem duplicar dias | RN-12, RN-13 |
| CA-06 | Referência anterior ao início ou posterior ao fim da sprint | Zero antes do início; capacidade limitada à sprint após o fim | RN-10 |
| CA-07 | Consulta durante um dia de trabalho, sem alterar a referência padrão | Hoje não entra nas horas esperadas; pode entrar na capacidade restante | RN-10, RN-18 |
| CA-08 | Expectativa zero e Completed maior que zero | Mostrar horas e diferença; cobertura não aplicável, sem divisão por zero | RN-15, RN-16, RN-25 |
| CA-09 | Pessoa com Tasks, mas sem capacidade | Mostrar trabalho e aviso; expectativa e comparações indisponíveis | RN-06, RN-25 |
| CA-10 | Completed vazio em uma Task; em outro processo, campo inexistente | Primeiro caso: zero com aviso; segundo: métrica indisponível | RN-26 |
| CA-11 | Pessoa com capacidade e sem Tasks | Pessoa visível, trabalho zero e capacidade calculada | RN-06 |
| CA-12 | Pessoas homônimas, Task sem responsável e Task transferida | Identidades separadas; grupo sem responsável; total transferido acompanha responsável atual | RN-05, RN-06 |
| CA-13 | Task transferida para outra sprint com Completed acumulado | Total acompanha a sprint atual da Task, sem divisão histórica | RN-03, RN-05, RN-24 |
| CA-14 | Task retornada mais de uma vez e item pai com horas | Task contada uma vez; horas do item pai de outro tipo não somadas | RN-04 |
| CA-15 | Tasks em áreas fora da equipe ou em subáreas | Respeitar a configuração de áreas e inclusão de subáreas | RN-03 |
| CA-16 | Pessoas com 40/40 h e 0/80 h registradas/esperadas | Cobertura da equipe 33,33%, e não 50% | RN-23 |
| CA-17 | Capacidade futura 24 h e RemainingWork 32 h | Saldo futuro −8 h, com estimativa original exibida separadamente | RN-18 a RN-21 |
| CA-18 | Duas Tasks Completed entre quatro Tasks; depois, consulta sem Tasks | Progresso 50%; sem Tasks, não aplicável | RN-22 |
| CA-19 | Alteração da referência para uma data passada | Recalcular capacidade; manter Completed atual e explicar essa condição | RN-24 |
| CA-20 | Falha de permissão ou consulta parcialmente concluída | Informar erro; não mostrar totais incompletos como válidos | RN-28 |

## Fora do escopo inicial

Escrita no DevOps, apontamento de horas pelo aplicativo, reconstrução histórica, login corporativo, acesso simultâneo da equipe, consolidação entre equipes, notificações e exportação.
