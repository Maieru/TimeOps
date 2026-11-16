# TimeOps

Planejamento de equipes e acompanhamento de implementações integrado ao **GitHub Projects**. O TimeOps administra equipes, funções, capacidades, ciclos, sprints, tarefas e apontamentos. O GitHub administra as issues, seus responsáveis e o status no Project.

Uma tarefa pode gerar implementações em vários produtos. Cada produto possui seu repositório e Project; cada implementação recebe uma issue independente. As estimativas, sprints, horas e responsáveis de um produto não são compartilhados automaticamente com os demais.

## Executar com Docker Compose

Requisitos: Docker com Compose, um GitHub App instalado na organização e repositórios/Projects existentes.

1. Copie `.env.example` para `.env` e preencha os valores.
2. Gere a chave privada do GitHub App e salve no caminho informado em `GITHUB_PRIVATE_KEY_FILE`.
3. Execute `docker compose up --build`.
4. Abra `http://localhost:5191` e entre com a conta do administrador inicial.

O Compose inicia PostgreSQL, aplica as migrações e persiste o banco e as chaves de sessão em volumes. O acesso publicado fica restrito ao localhost. O modo Development é destinado à execução local; para produção use HTTPS, `ASPNETCORE_ENVIRONMENT=Production` e um proxy configurado para encaminhar conexões WebSocket do Blazor. O proxy deve preservar o Host e enviar X-Forwarded-Proto. No Compose, informe seu IP em `TIMEOPS_TRUSTED_PROXY_IP`. Na execução direta, use `ReverseProxy__KnownProxies__0` (e índices seguintes, se necessário); somente proxies conhecidos têm os cabeçalhos aceitos. A entrega prevê uma instância do servidor.

Faça backup do banco antes de atualizar uma instalação existente. A migração também pode ser executada separadamente com `dotnet TimeOps.Web.dll --migrate`; nesse caso, mantenha `Database__MigrateOnStartup=false` no servidor normal. `/health` verifica o processo e `/ready` verifica acesso ao banco.

## Configurar o GitHub App

Registre um GitHub App para a organização e configure:

- **Callback URL:** `http://localhost:5191/signin-github` no desenvolvimento, ou `https://seu-endereco/signin-github` na instalação publicada.
- **Webhook URL:** `https://seu-endereco/webhooks/github`. Defina um segredo com pelo menos 20 caracteres. A instalação local funciona também por sincronização periódica, sem receber webhooks externos.
- **Repository permissions:** Issues — Read and write; Metadata — Read.
- **Organization permissions:** Projects — Read and write.
- **Eventos:** Issues e Projects v2 item, quando disponíveis na configuração da organização.

Instale o App na organização e conceda acesso aos repositórios dos produtos. Preencha organização, App ID, Installation ID, Client ID, Client Secret, caminho da chave e segredo do webhook. `GITHUB_BOOTSTRAP_ADMIN_ID` é o ID numérico da conta GitHub do administrador inicial, disponível na resposta `GET https://api.github.com/users/SEU_LOGIN`. Ele só é utilizado quando o banco ainda não possui usuários; não reativa contas posteriormente revogadas.

O login identifica a pessoa; os tokens da instalação são usados pelo servidor para criar e consultar issues e Projects. Credenciais não são enviadas ao navegador nem armazenadas em localStorage. O administrador autoriza as outras contas em **Acessos** e as vincula às equipes em **Planejamento**.

## Usar

1. Crie a equipe, configure calendário, integrantes e funções. Administradores controlam acessos globais; gestores administram suas equipes; integrantes registram suas horas e criam tarefas.
2. Defina a capacidade padrão por pessoa e função, em horas por dia. Crie um ciclo com a quantidade desejada de sprints e revise as datas. Cada sprint recebe uma cópia das capacidades padrão; ajustes em sprints existentes são explícitos.
3. Cadastre folgas individuais e da equipe. Cadastre os produtos com `organização/repositório` e número do Project. Consulte o GitHub e associe cada opção do campo **Status** a uma categoria do painel. É possível atualizar essa correspondência depois.
4. Em **Tarefas e implementações**, crie uma tarefa e selecione os produtos. Informe estimativa, restante e sprint de cada implementação, ou deixe-a no backlog.
5. Acompanhe a publicação de cada issue e atualize os dados após a sincronização. Responsável, título específico da issue e status são consultados do GitHub. Título e descrição da tarefa central são copiados na criação; edições posteriores não sobrescrevem as issues dos outros produtos.
6. Em **Apontar horas**, registre pessoa, data, implementação, sprint e horas realizadas. Correções e anulações preservam auditoria. Replanejar não transfere apontamentos antigos.
7. Consulte painel, visão pessoal, agrupamento por tarefa, timeline, histórico e burndown. Em **Exportar Excel**, selecione período e autor, confira a prévia e baixe o arquivo.

As funções de trabalho, como desenvolvimento e QA, são configuráveis e diferentes das permissões de administrador, gestor e integrante. A soma das capacidades de uma pessoa não pode ultrapassar 24 horas/dia. O total de apontamentos de uma pessoa também não pode ultrapassar 24 horas na mesma data.

## Métricas e histórico

- Realizado soma os apontamentos ativos da pessoa na sprint informada. Não muda quando a issue troca de responsável.
- Estimativa e restante são valores independentes mantidos no TimeOps. Registrar horas ou concluir a issue não altera automaticamente o restante.
- Capacidade esperada usa calendário, folgas e capacidade da sprint até a referência; o padrão inicial da referência é ontem, no fuso da equipe.
- O filtro de produto seleciona esforço; a capacidade da equipe não é multiplicada nem dividida entre produtos.
- Múltiplos responsáveis, ausência de responsável e pessoas sem vínculo na equipe mantêm o restante em um grupo sem responsável único, sem rateio implícito.
- O burndown usa os eventos locais de restante e replanejamento, incluindo entradas e saídas de sprint. Histórico com restante ausente não é apresentado como curva completa. O dia atual é parcial, e datas futuras não têm valores reais.
- A timeline agrupa implementações por tarefa central e usa o intervalo das sprints planejadas. Tarefas sem datas aparecem com aviso.

A exportação mantém as sete colunas **x**, **Atividade**, **OP/PMC/PGP**, **Nome da OP/PMC/PGP**, **Data**, **Horas** e **Comentários**. Cada linha representa um apontamento ativo, usando a data trabalhada e a pessoa que trabalhou. A tarefa central ocupa a coluna de agrupamento; a quarta e a sétima ficam vazias. Correções são refletidas no arquivo atual, e a auditoria permanece no banco. Nomes e títulos são texto literal no Excel.

## Publicação e recuperação

A criação local e sua fila de publicação são uma transação. O servidor salva o vínculo da issue antes de adicioná-la ao Project. Uma falha num produto não desfaz os demais. Falhas são retomadas com espera crescente, até oito tentativas, e ficam disponíveis para sincronização manual.

Um timeout na criação pode significar que o GitHub criou a issue sem devolver a resposta. Nesse caso, o servidor procura o comentário de correlação `timeops-implementation` nas issues do repositório. Se não puder confirmar o resultado, não cria outra automaticamente. Um gestor pode conferir o repositório e usar **Autorizar nova criação**; uma nova consulta verifica se a issue já existe antes de liberar a tentativa.

Webhooks são autenticados por HMAC-SHA256 e deduplicados pelo ID da entrega. O processamento consulta o estado atual do GitHub. A reconciliação ocorre a cada cinco minutos, e o worker verifica sua fila a cada 15 segundos. A interface apresenta última sincronização, publicações pendentes e falhas de acesso. Remover uma issue do Project não apaga dados locais nem causa recriação automática.

## Desenvolvimento e validação

Requisitos: SDK .NET 10, Node e PostgreSQL 17 para executar o aplicativo. Configure `ConnectionStrings__TimeOps` e as opções `GitHub__...` como variáveis de ambiente ou User Secrets. Não coloque credenciais em `wwwroot`.

```sh
dotnet restore TimeOps.slnx
dotnet run --project src/TimeOps.Web -- --migrate
dotnet run --project src/TimeOps.Web
```

```sh
dotnet test tests/TimeOps.Tests
node --test tests/interaction/*.test.mjs
dotnet build tests/TimeOps.E2E
node tests/TimeOps.E2E/bin/Debug/net10.0/.playwright/package/cli.js install chromium
dotnet test tests/TimeOps.E2E
```

Para usar Chromium já instalado, defina `E2E_BROWSER_PATH` com seu executável. Os testes HTTP e de navegador usam o servidor real com banco SQLite relacional e GitHub simulado, sem credenciais reais. Screenshots de desktop e mobile ficam em `.impeccable/review/`. O CI também aplica as migrações em PostgreSQL. Para executar toda a suíte HTTP/E2E com PostgreSQL, defina `TIMEOPS_TEST_POSTGRES` com a conexão de um banco exclusivo de testes; cada aplicação cria um schema isolado nesse banco.

A solução mantém Domain, Application, Infrastructure e Web. Domain contém regras puras; Application coordena planejamento, relatórios e sincronização; Infrastructure implementa EF Core e REST/GraphQL; Web contém Blazor, autenticação, webhook e composição. Falhas esperadas usam `Result`.

Esta migração substitui o fluxo ativo do Azure DevOps sem importar dados anteriores. Os adaptadores e testes legados permanecem no repositório como referência e regressão; não são registrados no aplicativo com servidor. O host estático e os cenários antigos de navegador também permanecem como referência, fora da suíte E2E ativa. A publicação automática no GitHub Pages foi substituída por validação do servidor no CI.
