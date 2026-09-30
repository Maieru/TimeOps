# TimeOps

Painel para comparar a capacidade da sprint com as horas `Completed` das Tasks no Azure DevOps Services. O cálculo e as decisões de negócio estão em [docs/](docs/README.md).

## Executar

Requisitos para desenvolvimento: .NET 10 SDK. Para consultar dados, um PAT do Azure DevOps com os escopos `Work Items (Read)` (`vso.work`) e `Project and Team (Read)` (`vso.project`). O titular precisa ter acesso de leitura à organização e ao projeto.

```powershell
dotnet run --project src/TimeOps.Web
```

Abra `http://localhost:5191`, informe organização e PAT e selecione projeto, equipe e sprint. O TimeOps usa **Blazor WebAssembly standalone**: interface, cálculos, cache e consultas executam no navegador. O servidor local serve apenas arquivos estáticos.

## PAT no cliente

**Manter conexão neste navegador** vem marcado, preservando a opção de lembrar conexão. Organização e PAT são salvos em `localStorage` do perfil do navegador, por origem e caminho base da aplicação. Recarregar a página recupera a conexão. Desmarcar a opção remove a credencial anteriormente salva e usa o PAT apenas na memória desta aba, até recarregar ou fechar.

**Esquecer e trocar conexão** remove a credencial salva e descarta a conexão da aba atual. Outras abas já abertas podem continuar com o PAT em memória até recarregar ou fechar. Pessoas que usam o mesmo perfil do navegador e endereço do aplicativo compartilham esse armazenamento; outros dispositivos ou perfis não recebem o PAT.

O PAT sai do navegador somente no cabeçalho de autenticação das consultas HTTPS a `dev.azure.com`, nunca para o host estático do TimeOps. Não coloque PAT em arquivos de configuração, Git, URLs ou segredos do workflow. Tudo que estiver em `wwwroot` será público.

O armazenamento do navegador **não é um cofre criptografado**: scripts da mesma origem e quem tem acesso ao perfil podem lê-lo. Use a opção de lembrar somente em um perfil confiável. A separação da chave por caminho evita colisões, mas não isola scripts de outros sites na mesma origem. Se o navegador bloquear o armazenamento, o aplicativo informa o erro sem expor o token; permita o armazenamento para conectar. A credencial antiga no Gerenciador de Credenciais do Windows não é importada nem apagada pela versão WebAssembly; pode ser removida manualmente em `TimeOps.AzureDevOps.Default`.

## Funcionalidades e integração

As visões de equipe, pessoa, **Features e histórias** e histórico mantêm os mesmos cálculos e regras. A visão de features soma o `Completed Work` das Tasks da sprint por história e feature, com cada nível expansível. Tasks sem vínculo aparecem em grupos próprios. O histórico mostra diferenças entre revisões de `Completed Work`, `Original Estimate` e `Remaining Work` nas Tasks do escopo atual, não apontamentos individuais. Completed é o valor **atual** do work item, mesmo ao selecionar uma referência anterior.

A visão **Burndown da iteração**, disponível no menu, compara o `Remaining Work` diário com uma linha ideal que parte do restante na abertura da sprint e reduz até zero nos dias úteis, descontando as folgas da equipe. O gráfico SVG tem uma tabela expansível com os mesmos valores; ambos permitem rolagem interna em telas pequenas. O ponto “Início” representa a abertura e os demais representam o fim de cada dia. O dia da coleta é parcial; datas futuras não recebem valor real. Alterar a referência de capacidade não muda a curva, que permanece ancorada à coleta.

A área do gráfico mostra um popup com os valores da data mais próxima ao mover o mouse horizontalmente ou tocar, sem precisar mirar na linha ou nas bolinhas. O popup acompanha o cursor a cada quadro do navegador, sem reconstruir o gráfico, e uma guia vertical identifica a data selecionada. Datas futuras continuam sem valor real. O popup destaca a data, o restante real, a linha ideal e a diferença, com cores correspondentes às séries e indicação de início ou coleta parcial. Tab navega pelos pontos e Escape fecha o popup. Seu tamanho em pixels independe da largura do gráfico; em telas pequenas, aparece abaixo da curva para manter todos os valores visíveis.

O carregamento consulta as seis configurações independentes em paralelo. O histórico mantém até oito leituras de Tasks simultâneas, sem aguardar um grupo inteiro terminar. As revisões de horas ficam em cache na memória da aba por até cinco minutos: a revisão atual de cada Task é conferida antes de reutilizar suas atualizações, inclusive ao mudar o período. O burndown aproveita o escopo e as revisões da coleta do painel, pula Tasks sem mudanças no período e guarda seu histórico calculado para reabrir a mesma coleta sem novas consultas. Uma coleta atualizada ou nova conexão invalida esse resultado; páginas incompletas não entram no cache.

O burndown reconstrói revisões somente das Tasks que estão atualmente na sprint e na área da equipe; não recompõe entradas e saídas desse escopo. Nas métricas atuais, campos vazios contam como zero com aviso. No burndown, `Remaining Work` vazio nas Tasks também conta como zero e gera um aviso, sem impedir a exibição da curva; campos vazios em revisões anteriores contam como zero. O campo indisponível no processo, datas ou calendário inválidos, histórico incompleto e dados inconsistentes impedem apresentar uma curva como completa.

As chamadas ao DevOps agora estão sujeitas ao **CORS do navegador**. Falhas de rede orientam verificar esse bloqueio. Antes de disponibilizar para a equipe, valide os endpoints GET e POST, paginação e histórico com uma organização e PAT reais, a partir do endereço final do site. O host estático não consegue alterar a política CORS do Azure DevOps; se algum endpoint for bloqueado, será necessário rever a integração. Não desative a segurança do navegador.

## Publicação estática

```powershell
dotnet publish src/TimeOps.Web/TimeOps.Web.csproj -c Release -o publish
```

Publique somente `publish/wwwroot`. Não é necessário executar .NET no host.

Para GitHub Pages, selecione **Settings → Pages → Source: GitHub Actions** e execute **Actions → Deploy TimeOps to Pages → Run workflow**. O workflow testa, publica o projeto Web, ajusta o `base href` para o endereço configurado no Pages e cria `404.html` e `.nojekyll`. Após o push destas alterações na `main`, a publicação é automática. Pull requests para `main` executam testes e build, sem deploy. A execução manual publica apenas quando selecionada a `main`. O deploy depende do build e usa o ambiente `github-pages`; as permissões de publicação ficam restritas ao job de deploy. Nenhum PAT do Azure DevOps é necessário na action.

Para Azure Static Web Apps, publique a mesma pasta com `base href="/"`. O arquivo `staticwebapp.config.json` incluído configura o retorno a `index.html` para rotas da aplicação. Em outros hosts, configure o mesmo fallback; para subdiretórios, ajuste o `base href` com barra final.

Referências: [Blazor WebAssembly no Pages](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/github-pages?view=aspnetcore-10.0) e [Azure Static Web Apps](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/azure-static-web-apps?view=aspnetcore-10.0).

## Validar

```powershell
dotnet test tests/TimeOps.Tests/TimeOps.Tests.csproj
node tests/interaction/burndown-interaction.test.mjs
python -m unittest discover -s tests/publishing -v
```

Os testes cobrem cálculos, casos de uso, API simulada, histórico, serialização e armazenamento com credenciais sintéticas, restauração, exclusão e falhas do navegador. A validação contra uma organização real exige um PAT fornecido pelo usuário e uma sprint de teste.

## Estrutura

- `TimeOps.Domain`: regras puras, modelos e Result pattern.
- `TimeOps.Application`: casos de uso e contratos.
- `TimeOps.Infrastructure`: REST do Azure DevOps, PAT, armazenamento do navegador, cache e falhas.
- `TimeOps.Web`: interface Blazor WebAssembly e composição das dependências.
