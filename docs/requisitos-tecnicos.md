# Requisitos técnicos

## Plataforma
C#/.NET 10, Blazor Web App com Interactive Server, PostgreSQL 17 e EF Core. Host em uma instância, sessão por cookie e comunicação interativa por WebSocket/SignalR. Docker Compose inclui banco, aplicação e volumes persistentes. HTTPS é obrigatório na instalação de produção. GitHub Pages não hospeda essa versão.

## Arquitetura e contratos
Manter Domain puro e Application dependente apenas de Domain. Infrastructure implementa IPlanningStore e IGitHubProjectsGateway; Web chama casos de uso e registra dependências. Planejamento, apontamentos, relatórios e sincronização usam Result para falhas esperadas. Cancelamento solicitado deve ser preservado.

IPlanningStore executa callbacks sem I/O externo em transações atômicas. O adaptador EF cria um contexto por operação, usa leitura consistente e serializa mutações com bloqueio consultivo do PostgreSQL antes da carga e alocação de IDs internos. IDs internos não são números de issues; vínculos externos usam node IDs do GitHub. O armazenamento atual trabalha com um retrato relacional da instalação por operação, adequado à primeira entrega com uma instância; ampliação de escala exige consultas por agregado e paginação de telas.

A migração inicial está versionada em Infrastructure/Migrations. Não usar EnsureCreated na produção. Database:MigrateOnStartup é opção explícita; --migrate permite executar a etapa separadamente. O primeiro administrador é provisionado por configuração apenas quando a tabela de usuários está vazia.

## Integração
Usar GitHub App com token de instalação de curta duração mantido no servidor. REST cria issues; GraphQL adiciona e consulta itens de Projects. O login usa o fluxo OAuth do GitHub App, consulta a identidade e exige conta ativa autorizada no banco. Não persistir tokens do usuário no navegador.

A criação local e os jobs são persistidos na mesma transação. Salvar a issue antes de adicioná-la ao Project. Marcar o início da criação antes da chamada externa para reconhecer interrupções. Identificador de correlação no corpo permite recuperar respostas incertas; não reenviar criação automaticamente se não houver confirmação.

O worker consulta a fila a cada 15 segundos. Atualização normal a cada cinco minutos; retentativas usam espera crescente e param após oito falhas até solicitação manual. Webhooks validam HMAC-SHA256, instalação e organização; deduplicam pelo identificador da entrega e consultam o estado atual. O endpoint limita o corpo a 1 MiB. Eventos maiores ou indisponíveis são recuperados pela reconciliação.

O cadastro consulta até 100 campos do Project e exige Status do tipo seleção. Um Project com mais campos é recusado explicitamente. As leituras de itens seguem paginação e não apresentam respostas parciais como completas. Status desconhecido, item arquivado, remoção e perda de permissão ficam visíveis como avisos.

## Operação e testes
Segredos vêm de variáveis, User Secrets ou arquivo de chave montado. Persistir chaves de proteção de sessão. /health indica processo ativo; /ready verifica banco. Logs operacionais usam códigos e tipos de falha, evitando tokens, chaves e respostas externas.

Testes abrangem domínio, autorização, concorrência, persistência relacional, publicação parcial/incerta, autoria de horas, burndown e Excel. E2E executa o servidor real com SQLite e GitHub simulado; o CI aplica a migração em PostgreSQL. Adaptadores e testes Azure DevOps são legados preservados e não são registrados na aplicação atual.
