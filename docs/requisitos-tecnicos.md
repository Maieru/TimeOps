# Requisitos técnicos

## Plataforma e limites da entrega

**RT-01.** Implementar futuramente o piloto em C#, .NET 10 LTS e ASP.NET Core, com Blazor Web App em modo interativo no servidor. A aplicação terá um único processo de implantação e não utilizará banco de dados.

**RT-02.** O piloto deverá escutar exclusivamente em interfaces de loopback. Será usado localmente, sem login próprio, para consultar uma organização configurada e uma equipe por vez. Compartilhamento em rede exigirá uma nova etapa de autenticação e hospedagem.

Este documento especifica o aplicativo; esta entrega documental não cria projetos .NET nem implementa integrações.

## Clean Architecture obrigatória

**RT-03.** Separar fisicamente as responsabilidades em quatro projetos. As dependências de código devem apontar para as camadas internas.

| Projeto | Responsabilidades | Referências permitidas |
| --- | --- | --- |
| TimeOps.Domain | Modelos e regras de negócio, valores de horas, calendário e cálculos puros das métricas | Nenhum outro projeto da solução |
| TimeOps.Application | Casos de uso, contratos internos e interfaces de integração | Domain |
| TimeOps.Infrastructure | Cliente REST do DevOps, autenticação por PAT, cache, mapeamento de dados externos e resiliência | Application e Domain |
| TimeOps.Web | Componentes Blazor, apresentação, configuração e composição da aplicação | Application; Infrastructure exclusivamente na composição |

**RT-04.** Domain e Application não poderão depender de ASP.NET Core, Blazor, SDK do Azure DevOps, implementação HTTP, armazenamento ou infraestrutura de cache. Domain não fará I/O nem acessará relógio global para calcular métricas; receberá dados e datas explicitamente.

**RT-05.** Interfaces para consultar o DevOps serão declaradas em Application e implementadas em Infrastructure. DTOs do serviço externo permanecerão em Infrastructure e serão convertidos para modelos internos antes de atravessar essa fronteira.

**RT-06.** Os componentes Blazor chamarão casos de uso de Application. Não conterão consultas HTTP, fórmulas de métricas ou referências a tipos concretos de Infrastructure. O acesso a Infrastructure em Web ficará restrito ao ponto de composição e ao registro de dependências.

**RT-07.** Application coordenará seleção de contexto, obtenção de dados, aplicação das regras de Domain e construção dos resultados. Infrastructure não decidirá regras de negócio. A injeção de dependências ocorrerá em Web.

**RT-08.** CQRS, MediatR e repositórios genéricos não são requisitos. Não introduzir essas abstrações apenas para caracterizar Clean Architecture.

## Result pattern obrigatório em todas as camadas

**RT-32.** Adotar Result pattern em Domain, Application, Infrastructure e Web. Operações que possam apresentar falhas esperadas deverão retornar `Result` ou `Result<T>`; operações assíncronas usarão `Task<Result>` ou `Task<Result<T>>`. A Web deverá tratar explicitamente sucesso e falha ao consumir os casos de uso, respeitando as assinaturas exigidas pelo framework para eventos e ciclo de vida.

**RT-33.** Definir os tipos comuns `Result`, `Result<T>` e `Error` em Domain, sem dependências externas de infraestrutura. Representar sucesso ou falha de forma mutuamente exclusiva, com valor acessível apenas no sucesso e erro obrigatório na falha. O erro deverá conter código estável, categoria e mensagem segura, sem exceções, respostas HTTP ou DTOs externos no contrato. A Web consumirá os resultados expostos por Application sem precisar adicionar referência direta a Domain.

**RT-34.** Domain retornará falhas de validação e de regras de negócio; Application propagará ou contextualizará os resultados; Infrastructure converterá falhas esperadas de integração, como autenticação, permissão, timeout e indisponibilidade, em erros padronizados após as tentativas cabíveis; Web traduzirá esses erros em mensagens e estados de interface. Não usar exceções como fluxo normal para falhas esperadas nem substituir falhas por sucesso com dados vazios.

**RT-35.** Exceções inesperadas deverão ser registradas de forma segura e tratadas na fronteira da aplicação, com apresentação de erro genérico ao usuário. Preservar o cancelamento solicitado pelo chamador, sem convertê-lo em indisponibilidade nem disparar novas tentativas. Avisos de qualidade de dados e métricas indisponíveis seguirão RT-10 e poderão integrar um resultado de consulta bem-sucedida; consultas incompletas seguirão RT-26 e não serão tratadas como sucesso completo.

## Casos de uso e contratos internos

**RT-09.** Application deverá oferecer casos de uso para listar projetos, equipes e sprints e consultar o painel de uma sprint, com atualização forçada opcional. Não será criada API pública no piloto.

| Contrato conceitual | Informações mínimas |
| --- | --- |
| Contexto da consulta | Organização, IDs de projeto/equipe/iteração, data de referência e fuso |
| Calendário e capacidade | Datas da sprint, dias de trabalho, folgas da equipe, identidade das pessoas, capacidades por atividade e folgas individuais |
| Task interna | ID, título, identidade do responsável, tipo, estado e categoria, área, iteração, campos de esforço e link |
| Resultado do painel | Contexto, métricas por pessoa/equipe, Tasks detalhadas, disponibilidade das métricas, avisos e horário da coleta |

**RT-10.** Distinguir valor zero, campo vazio, campo indisponível e consulta incompleta. Não representar todos esses estados como zero. Usar valores decimais nos cálculos de horas, com arredondamento apenas na apresentação.

**RT-11.** Calcular datas no fuso configurado, inicialmente `America/Sao_Paulo`, e tratar calendário e limites da sprint como datas de negócio. Normalizar os valores retornados pelo DevOps sem deslocar inadvertidamente as datas da sprint ou das folgas por conversão de fuso. O relógio será substituível em testes.

## Integração com Azure DevOps

**RT-12.** Usar APIs REST estáveis na versão 7.1. Consultar projetos, equipes, configurações de trabalho, áreas da equipe, iterações, capacidades, folgas e work items, além de metadados necessários para verificar campos e categorias de estado.

**RT-13.** Respeitar a iteração selecionada e os valores de área da equipe com sua opção de inclusão de subáreas. Deduplicar Tasks por ID. Não usar nome de exibição como chave de pessoa; mapear as identidades fornecidas pelo DevOps.

**RT-14.** Ler os campos `Microsoft.VSTS.Scheduling.CompletedWork`, `Microsoft.VSTS.Scheduling.OriginalEstimate` e `Microsoft.VSTS.Scheduling.RemainingWork`. Verificar disponibilidade para o tipo Task no processo consultado. Não inferir Completed a partir da estimativa, do estado ou do trabalho restante.

**RT-15.** Obter a categoria de estado a partir dos metadados do processo, sem presumir que estados concluídos se chamem Done ou Closed. A categoria Completed determina conclusão para o indicador por quantidade; a soma de esforço continua abrangendo todos os estados.

**RT-16.** Tratar paginação conforme cada endpoint, limites de resultados e leitura de work items em lotes. Recuperar todos os itens necessários antes de emitir um resultado completo. Detectar respostas truncadas, itens não recuperados e falhas em etapas intermediárias.

**RT-17.** A integração será somente de leitura: não criar, alterar ou excluir dados do DevOps. Operações de consulta que usem HTTP POST, como WIQL e leitura em lote, continuam permitidas; o critério é ausência de mutação, não apenas o verbo HTTP.

## Segurança e configuração

**RT-18.** Usar PAT com permissões mínimas de leitura requeridas pelos endpoints, incluindo leitura de work items e de projetos/equipes. Documentar na implementação os escopos efetivamente necessários e validar a conexão sem solicitar acesso total à organização.

**RT-19.** Permitir informar organização e PAT durante a execução, em formulário local. No Windows, oferecer persistência opcional, marcada por padrão, como credencial genérica do Gerenciador de Credenciais, limitada ao usuário do sistema e ao computador local. Recuperar a conexão ao iniciar nova sessão e oferecer ação explícita para apagar a credencial salva e descartar a conexão da sessão corrente. Sem esse cofre, manter o PAT somente na memória da sessão. Não persistir o PAT em appsettings, User Secrets, arquivos próprios, URLs, logs, cache de apresentação ou respostas para o navegador. Usar HTTPS nas chamadas ao DevOps. O piloto localhost não autentica cada visitante; executá-lo apenas em computador local confiável.

**RT-20.** Permitir informar a organização no formulário e configurar o fuso no servidor. Aceitar apenas o nome da organização, sem URL arbitrária; o cliente deverá construir as consultas exclusivamente para `dev.azure.com`.

**RT-21.** Mensagens de erro não deverão revelar token, cabeçalhos de autenticação ou respostas sensíveis. Os dados visíveis serão os acessíveis ao titular do PAT; o piloto não possui controle de acesso por usuário do aplicativo.

## Cache, atualização e tratamento de falhas

**RT-22.** Atualizar o painel ao selecionar o contexto e por botão manual. Manter dados temporariamente em memória por no máximo cinco minutos. A atualização manual deverá ignorar o cache. Exibir o horário real da coleta, não o momento de renderização.

**RT-23.** Separar as entradas de cache pelo contexto consultado; não misturar projetos, equipes ou sprints. Recalcular métricas ao alterar a data de referência. Invalidar o cache ao trocar organização ou credencial e nunca usar o PAT em texto como chave de cache.

**RT-24.** Tratar limites de requisição e falhas transitórias com tentativas limitadas, espera progressiva e respeito a `Retry-After`. Suportar cancelamento e timeout. Não repetir indefinidamente falhas de autenticação ou autorização.

**RT-25.** Diferenciar PAT inválido ou expirado, permissão insuficiente, contexto inexistente, indisponibilidade temporária e dados incompletos. Exibir orientação acionável, sem substituir falhas por resultados vazios.

**RT-26.** Não combinar respostas parciais em um painel aparentemente completo. Caso uma atualização falhe e exista resultado anterior, ele poderá permanecer visível somente com identificação de desatualização, horário original e erro da tentativa. Resultados parciais não substituirão a última coleta completa no cache.

**RT-27.** Registrar duração, contexto técnico, erros e tentativas de integração, sem PAT ou conteúdo integral dos work items. As coletas entre endpoints não constituem uma transação do DevOps; o painel representa o conjunto coletado, sem promessa de snapshot atomicamente consistente.

## Interface

**RT-28.** Apresentar português, datas no fuso configurado e horas com duas casas decimais. Exibir sinais da diferença, unidades, disponibilidade e avisos textuais, sem depender apenas de cor.

**RT-29.** Mostrar seleção de projeto/equipe/sprint, referência de capacidade, horário da coleta, atualização manual, resumo da equipe, métricas por pessoa e detalhamento das Tasks. Explicitar que Completed é atual, inclusive ao selecionar datas passadas.

## Estratégia de testes e aceite técnico

| Camada/tema | Verificação exigida |
| --- | --- |
| Domain | Testes unitários sem infraestrutura cobrindo as fórmulas e os cenários CA-01 a CA-19 aplicáveis, calendário, folgas sobrepostas, capacidade zero/ausente e arredondamento |
| Application | Testes com integrações substituídas para seleção de sprint, coordenação da consulta, propagação de avisos, referência temporal e resultados incompletos |
| Infrastructure | Testes com servidor HTTP simulado para autenticação, campos/metadados, identidades, paginação, lotes, filtros, falhas, cancelamento e cache |
| Integração real | Smoke test explícito com organização de teste e PAT informado localmente no aplicativo; validar leitura de equipe/sprint e amostra de totais; nunca depender de segredo versionado |
| Web | Verificar navegação, idioma, detalhes, links, atualização manual e distinção entre indisponível, zero, incompleto e desatualizado |
| Arquitetura | Testes automatizados de referências e dependências de tipos impedindo violações de RT-03 a RT-07, incluindo uso de Infrastructure fora da composição em Web |
| Segurança | Confirmar binding somente em loopback, armazenamento no cofre do usuário do Windows, restauração e exclusão da credencial, descarte do PAT da sessão corrente ao trocar conexão e ausência do token em respostas, logs, cache de apresentação e arquivos versionados |
| Result pattern | Verificar invariantes de sucesso/falha, propagação entre camadas, conversão de falhas de integração, tratamento explícito na Web, cancelamento e ausência de detalhes sensíveis nos erros, conforme RT-32 a RT-35 |

**RT-30.** Os testes de arquitetura deverão falhar se Domain referenciar outro projeto, Application referenciar Infrastructure/Web, camadas internas dependerem dos frameworks proibidos ou componentes Web acessarem implementações externas diretamente.

**RT-31.** Antes da conclusão do piloto, executar build, testes unitários, testes de casos de uso, integração simulada e testes de arquitetura. Registrar separadamente o resultado do smoke test real e eventual impossibilidade de executá-lo por falta de credenciais ou ambiente.

## Rastreabilidade

- RT-09 a RT-16 implementam a coleta e os contratos necessários a RN-01 a RN-26.
- RT-22 a RT-27 sustentam a confiabilidade exigida por RN-08 e RN-28 e pelo cenário CA-20.
- RT-28 e RT-29 sustentam RN-07, RN-24 e RN-29.
- RT-03 a RT-08 e RT-30 tornam Clean Architecture um requisito verificável, não apenas uma convenção documental.
- RT-32 a RT-35 padronizam sucesso e falhas em todas as camadas, preservando as regras de disponibilidade de dados e tratamento de erros.

As premissas, limitações e referências oficiais estão no [índice da documentação](README.md). Os critérios funcionais completos estão nos [requisitos de negócio](requisitos-negocio.md).
