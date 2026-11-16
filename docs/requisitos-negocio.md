# Requisitos de negócio

- Acesso compartilhado por login GitHub, com administrador global, gestor por equipe e integrante. Funções de trabalho são configuráveis e independentes dessas permissões.
- Cada equipe configura calendário, fuso, integrantes, folgas e capacidade diária distribuída por função. O cadastro padrão é copiado para cada sprint nova; alterações do padrão não sobrescrevem sprints existentes.
- Cada ciclo pertence a uma equipe e contém quantidade configurável de sprints, com datas editáveis e sem sobreposição na equipe. Sprints com planejamento, apontamentos ou histórico não podem ser removidas.
- Cada produto relaciona um repositório e um Project existentes na organização. Opções de Status são associadas às categorias de acompanhamento.
- Criar uma tarefa central selecionando um ou mais produtos gera implementações independentes, com uma issue em cada repositório e inclusão no respectivo Project. O par tarefa/produto é único.
- Cada implementação pode estar no backlog ou em uma sprint e possui estimativa e restante próprios. Responsável e status são atualizados no GitHub; planejamento e esforço são atualizados no TimeOps.
- Apontamentos registram pessoa que trabalhou, data, implementação, sprint e horas. A data deve pertencer à sprint e não pode ser futura no fuso da equipe. Horas são positivas; a soma diária por pessoa não excede 24. Correções e anulações mantêm auditoria e controle de concorrência.
- Horas realizadas por pessoa são a soma de seus apontamentos ativos. Horas não são transferidas quando o responsável da issue ou a sprint atual da implementação muda. Estimativa e restante seguem o planejamento atual, sem decremento automático por apontamento ou conclusão.
- Capacidade esperada soma capacidades das funções por dia elegível até a referência, descontando folgas. Não presume jornada de oito horas nem multiplica capacidade por produto. Cobertura é realizado/esperado quando a expectativa é positiva.
- Preservar painéis de equipe/pessoa, agrupamento por tarefa, timeline, histórico, burndown e exportação Excel. Burndown reconstrói restante e mudanças de escopo locais; dados incompletos não podem parecer uma curva completa.
- Publicação parcial permanece visível por produto. Criações incertas exigem reconciliação antes de repetir; uma nova criação não confirmada exige conferência de gestor.
- A primeira versão atende uma organização no GitHub.com com Projects atual. Não importa automaticamente dados anteriores, não publica planejamento/horas em campos do GitHub e não acompanha automaticamente issues criadas fora do TimeOps.
