> **Revisão de arquitetura — 30/09/2026:** a solicitação posterior adota usuário → múltiplos treinos → múltiplas fichas → múltiplos exercícios. O cadastro de exercício usa Nome, Equipamento e Instruções longas. Administradores fazem CRUD de todas as entidades e podem criar/alterar perfis de usuários pelo aplicativo; contas comuns gerenciam os próprios dados e não criam usuários. Estas regras substituem as restrições anteriores de administração neste documento. A migração vigente é 001 → 003 → 004, com os scripts originais preservados. Consulte o README e ARQUITETURA.md na raiz para a API, a autorização por proprietário e a operação atual.

# Plano de implementação — aplicativo de treinos

Data: 29/09/2026. Entrega: especificação do MVP e scripts SQL de referência; o aplicativo ainda será implementado.

## 1. Objetivo e escopo

Construir um **PWA responsivo e instalável em React + TypeScript**, com API **ASP.NET Core / .NET 10** e **MariaDB**, para gerenciar exercícios, fichas, execução diária offline e progressão pessoal. Os treinos ficam salvos no dispositivo e são sincronizados conforme a política de Wi-Fi da seção 8.6.

Requisitos obrigatórios:

- Login por e-mail e senha, logout, alteração e recuperação de senha por e-mail.
- Dois perfis: **administrador** e **comum**. Somente o administrador cadastra novos usuários; não existe cadastro público.
- PWA com uso offline após preparação online inicial, armazenamento IndexedDB e sincronização dos treinos.
- Preferência padrão por Wi-Fi, com detecção automática quando suportada e confirmação manual nos navegadores que não identificam o tipo de rede.
- Cadastro, consulta, alteração e exclusão de exercícios próprios.
- Gerenciamento das fichas: exercícios, ordem, séries, repetições ou duração e descanso.
- Registro de treino por data, inclusive retroativo, com carga opcional por série.
- Modo rápido para registrar o treino sem detalhar séries e modo detalhado por série.
- Histórico editável e relatórios de frequência, cargas, repetições e duração.
- Separação rigorosa entre controllers HTTP e lógica de negócio.
- Carga inicial das fichas Full Body A, B e C criadas nesta conversa.
- Isolamento de dados por usuário desde a primeira versão.
- **Toda a aplicação e suas dependências devem executar em containers Podman reunidos em um único pod por ambiente/instalação**, incluindo frontend/proxy, API, MariaDB e tarefas auxiliares de migração, seed e backup.

Fora do MVP: pagamento, rede social, prescrição por IA, integração com relógios, contagem de calorias e aplicativo nativo. **Uso offline e sincronização fazem parte do MVP.** Registro, correção e exclusão de treinos funcionam sem internet. Gestão do catálogo/fichas, cadastro de usuários, login e recuperação de senha exigem conexão; os dados já baixados podem ser consultados offline.

## 2. Stack e compatibilidade

| Camada | Decisão |
|---|---|
| Frontend | React, TypeScript e Vite como PWA; manifest, ícones, service worker e interface pt-BR |
| Navegação e dados | React Router, TanStack Query para consultas online e cliente HTTP centralizado |
| Offline | IndexedDB com repositório local; service worker com Workbox; fila própria de sincronização persistente |
| Sincronização | Protocolo versionado, IDs locais UUID, operações idempotentes, conflitos explícitos e cursor por usuário |
| Formulários | React Hook Form + Zod, ou validação equivalente; backend permanece autoritativo |
| Gráficos | Recharts, com tabela acessível contendo os mesmos dados |
| Backend | ASP.NET Core Web API com controllers, target `net10.0` |
| Persistência | Dapper + MySqlConnector, SQL parametrizado e operações assíncronas |
| Banco | MariaDB 11.8, InnoDB e `utf8mb4`; fixar patch/digest homologado |
| Identidade | ASP.NET Core Identity com stores Dapper; perfis fixos `administrator` e `common` |
| E-mail | `IEmailSender` com adaptador SMTP configurável e fila persistente; desabilitado até informar configurações |
| Sessão | Cookie de autenticação HttpOnly, Secure e SameSite=Lax; proteção CSRF |
| Testes | xUnit para domínio/API; integração com MariaDB real; Playwright para os fluxos essenciais |
| Execução | Podman; todos os containers da aplicação no mesmo pod `treinos-pod`; ciclo de vida com Quadlet/systemd |

**Decisão de persistência:** MySqlConnector declara suporte ao MariaDB e, no histórico da versão 2.5.0, ao .NET 10. Usar uma versão estável corrigida e homologada, não fixar essa versão mínima antiga como recomendação de segurança. Dapper permite manter o target .NET 10 sem depender de um provider EF Core específico. Na consulta realizada, a release estável do Pomelo exibida foi a 9.0.0, compatível com EF Core 9. Não misturar Pomelo 9 e EF Core 10. [1–4]

Na fase inicial, resolver e fixar versões estáveis compatíveis em `global.json`, `Directory.Packages.props`, `packages.lock.json` e lockfile npm. Validar build, autenticação e integração com a imagem MariaDB escolhida antes de implementar as telas. Bibliotecas frontend acima são escolhas de projeto; confirmar suas versões e requisitos de Node na instalação.

## 3. Arquitetura: negócio fora dos controllers

Adotar um monólito modular simples, com quatro projetos de produção. Não há necessidade inicial de microserviços, event bus, repositório genérico ou MediatR.

| Projeto | Responsabilidade | Dependências permitidas |
|---|---|---|
| `Treinos.Domain` | Entidades, regras, estados de sessão, validação de séries e políticas de cálculo | Nenhuma referência a ASP.NET, Dapper ou banco |
| `Treinos.Application` | Casos de uso, transações, autorização sobre recursos, DTOs, contratos de persistência e relatórios | Domain |
| `Treinos.Infrastructure` | Repositórios Dapper, consultas SQL, conexão, transações e stores Identity | Application e Domain |
| `Treinos.Api` | Controllers finos, autenticação HTTP, filtros, ProblemDetails, DI e OpenAPI | Application; Infrastructure apenas na composição das dependências |

Projetos adicionais: `Treinos.Migrator` (console .NET 10), testes de domínio, integração e arquitetura.

Fluxo de uma escrita: controller recebe DTO → caso de uso identifica o usuário → carrega recursos próprios → domínio valida → repositórios persistem em transação → controller traduz o resultado para HTTP.

**Proibido nos controllers:** SQL, acesso a `MySqlConnection`, cálculo de progressão, hash de senha, decisão de permissão sobre IDs, alteração direta de entidades, controle de transações ou acesso direto aos stores Identity.

Exemplo ilustrativo de controller fino, a completar com os DTOs e contratos do projeto:

```csharp
[ApiController]
[Authorize]
[Route("api/exercises")]
public sealed class ExercisesController(IExerciseService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateExerciseRequest request,
                                            CancellationToken cancellationToken)
    {
        var exercise = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/exercises/{exercise.Id}", exercise);
    }
}
```

`IExerciseService` usa `ICurrentUser`, definido em Application e implementado na camada HTTP. O DTO público nunca recebe um `userId` de proprietário. Erros de validação, recurso ausente e conflito são convertidos em ProblemDetails por tratamento centralizado.

Casos de uso principais:

- `BootstrapAdministrator` por comando administrativo; `CreateUserByAdministrator` pela aplicação.
- `Login`, `Logout`, `ChangePassword`, `GetCurrentUser`, `RequestPasswordReset`, `ResetPassword`.
- `ApplySessionSyncOperation`, `GetSyncSnapshot`, `GetSyncChanges` e `DispatchEmailOutbox`.
- `CreateExercise`, `UpdateExercise`, `DeleteExercise`, `ListExercises`.
- `CreatePlan`, `UpdatePlan`, `SaveWorkoutTemplate`, `DeleteWorkoutTemplate`.
- `StartWorkoutSession`, `AddSessionExercise`, `SaveSet`, `RemoveSet`.
- `CompleteWorkoutSession`, `CorrectWorkoutSession`, `DeleteWorkoutSession`.
- `GetWorkoutHistory`, `GetFrequencyReport`, `GetExerciseProgressReport`.

Um `IUnitOfWork` compartilha a mesma conexão/transação entre os repositórios participantes. Consultas de relatório podem usar um `IProgressReportReader` específico. O SQL faz agregações, mas filtros elegíveis e fórmulas são contratos explícitos da Application/Domain, cobertos por testes.

## 4. Estrutura sugerida do repositório

| Caminho | Conteúdo |
|---|---|
| `frontend/src/features/auth/` | Login, conta, senha temporária e recuperação de senha |
| `frontend/src/features/admin/` | Cadastro de usuários protegido por permissão |
| `frontend/src/offline/` | IndexedDB, repositórios locais, outbox, conflitos e migrações locais |
| `frontend/src/sync/` | Política de rede, autenticação, envio/recebimento e retries |
| `frontend/src/service-worker.ts` | App shell offline e integração opcional com Background Sync |
| `frontend/public/manifest.webmanifest` | Nome, ícones, escopo, início e modo standalone |
| `frontend/src/features/exercises/` | Catálogo e formulários |
| `frontend/src/features/plans/` | Planos e fichas |
| `frontend/src/features/sessions/` | Registro do treino e histórico |
| `frontend/src/features/reports/` | Gráficos, filtros e tabelas |
| `frontend/src/shared/` | Cliente HTTP, componentes, datas e tratamento de erros |
| `backend/src/Treinos.Domain/` | Entidades e políticas |
| `backend/src/Treinos.Application/` | Casos de uso e interfaces |
| `backend/src/Treinos.Infrastructure/` | Identity, persistência e consultas |
| `backend/src/Treinos.Api/` | HTTP e composição |
| `backend/src/Treinos.Migrator/` | Aplicação controlada de migrações |
| `backend/tests/` | Testes automatizados |
| `database/migrations/001_schema_treinos.sql` | Esquema inicial fornecido |
| `database/seeds/002_carga_inicial_fullbody.sql` | Carga inicial fornecida, com notificação para sincronização |
| `database/migrations/003_pwa_usuarios_email.sql` | Perfis, controle de sincronização e fila de e-mail |
| `deploy/Containerfile.web` | Build React em estágio Node e entrega dos arquivos estáticos pelo proxy |
| `deploy/Containerfile.api` | Build com SDK .NET 10 e imagem final de runtime |
| `deploy/Containerfile.migrator` | Imagem da tarefa de migração .NET 10 |
| `deploy/Containerfile.maintenance` | Cliente MariaDB, seed, backup e restauração |
| `deploy/quadlet/treinos.pod` | Definição do único pod da aplicação |
| `deploy/quadlet/*.container` e `*.volume` | Containers vinculados ao pod e volumes persistentes |
| `deploy/scripts/` | Build, inicialização, parada, migração, seed, backup e atualização via Podman |

## 5. Autenticação, perfis e recuperação de senha

### 5.1 Perfis e cadastro de usuários

Há exatamente dois perfis de aplicação: `administrator` (administrador) e `common` (comum). Ambos podem usar as mesmas funções de treino sobre seus próprios dados. O único privilégio adicional de produto do administrador é cadastrar novos usuários; ele não recebe acesso aos treinos alheios.

| Operação | Administrador | Comum |
|---|---|---|
| Gerenciar próprios exercícios/fichas/treinos | Sim | Sim |
| Usar PWA offline e sincronizar próprios dados | Sim | Sim |
| Consultar próprios relatórios e recuperar senha | Sim | Sim |
| Cadastrar novos usuários | Sim, online | Não |
| Ler ou alterar treinos de outra pessoa | Não | Não |

Remover a tela e a rota de cadastro público. `POST /api/admin/users` exige a política `CanCreateUsers`; validar a permissão na API e no caso de uso. O DTO recebe nome, e-mail e senha inicial, **não** aceita proprietário, perfil, hash ou security stamp. Usuários criados pela interface recebem `common` e `must_change_password=true`.

O administrador informa uma senha inicial forte por campo mascarado. Ela existe somente no transporte HTTPS e na memória necessária ao Identity: não devolver a senha na resposta, registrar em logs ou guardá-la na fila de e-mail. Comunicar essa senha ao usuário por meio apropriado, fora do escopo deste aplicativo. Na primeira entrada, exigir alteração antes de liberar dados de treino ou download offline; permitir apenas `me`, CSRF, logout e alteração de senha enquanto houver essa obrigação. Assim o cadastro funciona mesmo antes de configurar o e-mail.

O primeiro administrador é provisionado por comando `BootstrapAdministrator`, executado em container de manutenção .NET no mesmo pod, usando UserManager/Identity. Ler senha interativamente ou de Podman secret. Não usar senha padrão nem fazer o primeiro visitante virar administrador. O bootstrap é controlado pelo operador, transacional e idempotente; reexecução não troca a senha. Promover uma conta existente exige opção explícita com o ID da conta e invalidação de sessões. A interface não oferece promoção de perfil; outros administradores só são provisionados por esse procedimento operacional.

Persistir o perfil em `app_users.app_role`, limitado por CHECK aos dois valores. Uma factory de principal produz a claim de papel a partir do banco; a política usa essa claim e revalida o estado da conta. Não é necessário um catálogo dinâmico de papéis/RoleManager no MVP. Nunca confiar em perfil vindo do React, IndexedDB ou payload de sincronização.

### 5.2 Identity, cookies e isolamento

Manter ASP.NET Core Identity com `IUserStore`, `IUserPasswordStore`, `IUserEmailStore`, `IUserSecurityStampStore` e `IUserLockoutStore` em Dapper. Usar UserManager para criação/alteração/reset de senha, normalização e lockout. `UpdateAsync` confere e renova `concurrency_stamp`; falha de concorrência não pode ser ignorada. Os stores ficam na Infrastructure. [5]

- Senhas de 12 a 128 caracteres, aceitando frases e sem armazenamento em texto puro.
- Login com mensagem genérica, limite de tentativas por conta/IP e bloqueio temporário configurável.
- Cookie `__Host-treinos`, HttpOnly, Secure, SameSite=Lax, `Path=/`, sem Domain; duração inicial de 8 horas, sem renovação ilimitada.
- Token antiforgery obtido por `GET /api/auth/csrf` e enviado em `X-CSRF-TOKEN` nas escritas, inclusive login, criação de usuário, logout, reset e sincronização. Renovar após mudança da autenticação. [6]
- Nunca guardar senha ou token de autenticação em IndexedDB/localStorage. O outbox guarda intenções de negócio, sem cookies, credenciais ou tokens CSRF.
- Persistir chaves Data Protection fora do container efêmero. Validar security stamp a cada request autenticado no MVP (`ValidationInterval=TimeSpan.Zero`) para revogação efetiva após troca/reset de senha, inclusive no sync.
- Mesma origem HTTPS para frontend e `/api`; desenvolvimento também em containers, com origem segura para o service worker.
- API retorna 401/403 JSON, sem redirecionamento HTML. Todas as consultas/escritas usam o proprietário autenticado e FKs compostas; recurso de outro usuário retorna 404.

### 5.3 Recuperação por e-mail, pronta para configuração posterior

Implementar desde o MVP as telas “Esqueci minha senha” e “Definir nova senha”, os endpoints e `IEmailSender`/fila. O operador configurará o serviço depois, sem precisar alterar o código. Usar a geração/validação de tokens do Identity como base do fluxo. [15] Nenhum envio real é necessário nesta entrega de planejamento.

Fluxo com serviço habilitado:

1. `POST /auth/forgot-password` recebe e-mail e aplica limites de frequência por IP/conta.
2. Responder 202 com mensagem genérica igual para conta existente/inexistente. Não expor o token na resposta, no log ou na interface administrativa.
3. Para conta existente, gerar token de recuperação pelo Identity (`GeneratePasswordResetTokenAsync`), configurar validade de 1 hora para esse provedor e codificá-lo com Base64Url. Usar URL HTTPS a partir de `App.PublicBaseUrl` configurada, nunca do header Host do request.
4. Guardar mensagem na `email_outbox`; proteger link/token com Data Protection antes de persistir. Dispatcher envia via adaptador SMTP com TLS e validação de certificado. Repetições são limitadas; mensagens expiradas não são enviadas.
5. A página recebe ID/token, permite nova senha e confirmação e chama `ResetPasswordAsync`. Validar token e política, renovar security stamp e limpar `must_change_password`. Controle de concorrência garante que apenas um reset concorrente com aquele token seja efetivado.
6. Depois do sucesso, invalidar sessões anteriores e voltar ao login. Reutilização/expiração do token retorna erro genérico. O PWA conserva pendências do mesmo usuário até nova autenticação, sem enviá-las com sessão revogada.

Não cachear respostas ou tokens de recuperação no service worker. O app shell pode ser reutilizado, mas a operação é online. Remover ID/token da barra de endereço após a leitura em memória e usar `Referrer-Policy: no-referrer`; omitir parâmetros de token dos logs do proxy. Abrir o link não altera a senha automaticamente.

Configuração prevista, inicialmente desabilitada:

```ini
Email__Enabled=false
Email__Provider=Smtp
Email__Host=
Email__Port=587
Email__TlsMode=StartTls
Email__Username=
Email__PasswordFile=/run/secrets/treinos-smtp-password
Email__FromAddress=
Email__FromName=Treinos
App__PublicBaseUrl=https://SEU-DOMINIO
```

Implementar explicitamente a leitura de `PasswordFile`; não é uma convenção automática do .NET. Permitir SMTP com STARTTLS ou TLS implícito conforme configuração. Validar os campos quando `Enabled=true`.

Enquanto `Enabled=false`, o aplicativo continua funcional; a recuperação retorna o mesmo 503 `EMAIL_NOT_CONFIGURED` para qualquer e-mail, antes de consultar a conta, e a tela informa indisponibilidade temporária. Não fingir envio, guardar mensagens para entrega indefinida ou devolver links como atalho. Quando habilitado, usar a resposta genérica 202; falhas reais do provedor entram em retry controlado e diagnóstico sem dados sensíveis.

Dispatcher como BackgroundService no container da API, com lease exclusivo por mensagem no MariaDB e processamento seguro entre réplicas. `lease_token` deve acompanhar a conclusão do envio para um worker antigo não liberar o trabalho de outro. Entrega pode repetir se houver falha após aceite SMTP; duplicação de e-mail não torna o token reutilizável. Manter TTL, limite de tentativas e expurgo do conteúdo protegido após retenção definida. O SMTP externo é uma integração configurável; o código de envio e a fila pertencem aos containers/pod da aplicação.

## 6. Modelo de dados e arquivos SQL

O arquivo **`001_schema_treinos.sql`** é o contrato original, preservado. Aplicar também a migração **`003_pwa_usuarios_email.sql`** antes de usar esta versão do aplicativo ou o seed atualizado. Ela acrescenta perfis e quatro tabelas para sincronização/e-mail, sem promover contas existentes a administrador. Nenhum SQL cria senhas. Migrações de estrutura são 001 → 003; o arquivo 002 é carga de dados executada separadamente.

| Tabela | Finalidade |
|---|---|
| `app_users` | Conta, hash Identity, normalização, stamps, lockout e fuso |
| `exercises` | Catálogo privado; unidade, tipo de medição e convenção de carga |
| `workout_plans` | Agrupamento de fichas e orientações gerais |
| `workout_templates` | Fichas A/B/C ou outras; dia sugerido e ordenação |
| `workout_template_items` | Prescrição de cada exercício na ficha |
| `workout_item_alternatives` | Alternativas explícitas, como hack no lugar do agachamento |
| `workout_sessions` | Treino realizado, data local, estado e snapshots da ficha |
| `session_exercises` | Exercícios da sessão, com snapshots das prescrições e metadados |
| `session_sets` | Séries registradas, repetições/segundos e carga opcional |
| `seed_imports` | Controle idempotente da carga inicial por usuário |
| `user_sync_state` | Revisão atual por usuário e piso de retenção do feed |
| `sync_changes` | Feed ordenado de alterações, snapshots e tombstones |
| `sync_operation_receipts` | Recibos de operações aplicadas para impedir duplicação |
| `email_outbox` | Fila com payload protegido, validade, tentativas e lease de envio |

Convenções:

- IDs em UUID textual de 36 caracteres; na API, gerar UUIDs em minúsculas. O seed gera com `UUID()`.
- `DECIMAL(7,2)` para carga, nunca `FLOAT`. Na API usar `decimal?`.
- `load_kg = NULL` significa não informado; `0` é um valor informado e não é convertido em ausência.
- Data do treino em `performed_on DATE`, transportada como `YYYY-MM-DD`/`DateOnly`.
- Datas técnicas em UTC (`DATETIME(6)`); conexão do banco em UTC.
- Fuso IANA do usuário, inicialmente `America/Sao_Paulo`, copiado para a sessão. O dia de um treino antigo não muda quando o usuário altera o fuso.
- `row_version` numérico para concorrência otimista; aplicação incrementa também quando um filho da sessão/ficha muda.
- Índices principais: catálogo por usuário/estado/nome, sessões por usuário/estado/data e execução por usuário/exercício/sessão.

**Separação essencial:** a ficha é uma prescrição editável; a sessão é o que foi registrado em determinada data. Iniciar sessão copia nomes, convenções, metas e instruções. Consultar um treino antigo não recalcula esses campos usando o catálogo atual.

## 7. Regras dos exercícios e das fichas

O formulário de exercício inclui nome, grupo muscular, equipamento, instruções, medição (`reps` ou `duration`), tipo de carga e convenção.

| Campo | Opções / regra |
|---|---|
| Tipo de carga | `external`, `bodyweight`, `assisted` |
| Convenção | `total`, `per_hand`, `machine_display`, `added_weight` |
| Unidade de carga | Quilogramas no MVP |
| Prescrição por repetição | Faixa mínima/máxima positiva, sem faixa de segundos |
| Prescrição por duração | Faixa de segundos positiva, sem repetições |
| Afundo unilateral | `per_side`; descanso `after_both_sides` |

Validar nome obrigatório, limites dos campos, séries positivas, mínimo ≤ máximo, medição coerente com o exercício e referência a exercícios ativos do usuário. Permitir nomes iguais quando equipamentos/variações diferirem; usar IDs como identidade.

Depois que um exercício tem sessões, não alterar silenciosamente sua medição, equipamento físico, tipo ou convenção de carga: orientar a duplicá-lo como nova variação. Nome e instruções podem mudar; snapshots antigos permanecem.

**Excluir exercício:** implementar exclusão lógica com `deleted_at_utc`. Ele sai do catálogo ativo; histórico e relatórios permanecem. A API informa fichas afetadas. Iniciar uma ficha com exercício arquivado exige substituí-lo ou removê-lo, retornando conflito claro. Nunca apagar execuções em cascata.

Excluir plano/ficha também arquiva sua definição, preservando sessões. Remover um item da ficha pode removê-lo fisicamente: sessões não dependem desse item, pois usam snapshots. As alternativas desse item são o único relacionamento com exclusão em cascata no esquema.

Dias da semana são sugestões. Permitir qualquer ficha em qualquer dia e mais de uma sessão na mesma data.

## 8. Registro de treino

### 8.1 Iniciar e retomar

1. Escolher ficha e data; oferecer hoje no fuso do usuário.
2. Marcar opcionalmente “Semana de adaptação”. Quando marcado, copiar 2 séries para itens que originalmente têm 3; manter a ficha original inalterada.
3. Escolher alternativas antes de iniciar, por exemplo agachamento livre ou hack.
4. Criar UUID da sessão e snapshots em uma transação IndexedDB, com estado `in_progress`, revisão local e operação na outbox. Validar no servidor ao sincronizar.
5. Retomar do IndexedDB ao recarregar, mesmo offline. Receber atualizações do servidor pelo protocolo de sync; não criar nova sessão a cada entrada na tela.

Permitir sessão avulsa sem ficha, adicionando exercícios do catálogo. Datas futuras são inválidas para um treino realizado; a validação usa o fuso informado na sessão, não a data UTC do servidor.

### 8.2 Modo rápido

O usuário informa a data, marca os exercícios realizados e conclui, sem informar séries, repetições ou carga. Deve haver uma ação explícita “Marcar todos como realizados”, nunca inferência silenciosa.

Exigir pelo menos um exercício realizado. Salvar `completion_status=completed` e nenhuma série fictícia. O treino conta na frequência, mas não gera métricas de carga, séries ou volume sem dados medidos. Metas da ficha nunca viram desempenho automaticamente.

### 8.3 Modo detalhado

- Cada série tem número, lado, repetições **ou** duração, carga opcional, RIR opcional e sinalização de aquecimento.
- Uma série marcada concluída exige a medida positiva correspondente ao exercício. Carga continua opcional.
- Prancha usa segundos. Não exigir repetições para ela.
- No afundo, 3 séries por perna significam três pares de registros: `(1,left)`, `(1,right)` até `(3,right)`. A interface apresenta três linhas com campos por lado; não duplicar o número de séries prescrito.
- Mostrar a última carga conhecida como sugestão, sem salvá-la automaticamente como carga de hoje.
- Exibir a convenção junto ao campo: “kg por halter”, “kg totais” ou “kg da máquina”.
- Carga negativa, segundos/repetições nulos em série concluída e lado incompatível são rejeitados no backend.
- Não exigir atingir a faixa planejada: uma série real de 7 repetições em uma meta de 8-12 é um registro válido.
- Salvar por confirmação de série ou saída do campo, primeiro em transação IndexedDB com a outbox; distinguir “Salvo neste dispositivo” de “Sincronizado”. Em falha/quota local, mostrar “Não salvo” e preservar o formulário.

### 8.4 Conclusão e correção

Antes de concluir, resolver exercícios ainda `planned`: realizados ou pulados. No modo detalhado, cada exercício realizado precisa de uma série concluída; para informar só presença, o usuário escolhe o modo rápido. É permitido concluir um treino parcial, desde que exista exercício realizado.

`completed_at_utc` é a hora em que o registro foi concluído no sistema. Não usá-la para agrupar treino retroativo nem para inventar duração. `started_at_utc` e `ended_at_utc` são opcionais e representam horários reais apenas quando informados/medidos.

Permitir corrigir sessão concluída e excluir registros equivocados, inclusive offline. Alteração/exclusão atualiza a visão local e, após sync, a visão do servidor. Excluir gera tombstone, sem apagar a intenção pendente; no servidor marca `deleted_at_utc`. Sessões excluídas, canceladas ou em andamento não contam nos relatórios padrão.

### 8.5 PWA e preparação para uso offline

Entregar manifest com nome, ícones 192/512 e maskable, `start_url`, `scope`, `display=standalone`, cores e HTTPS. Usar service worker com Workbox para precache versionado do app shell, fontes/ícones locais e navegação offline. Evitar dependência de CDN em telas essenciais. Compatibilidade de instalação varia; o modo em aba também deve funcionar.

Primeiro uso exige conexão, login válido e conclusão da troca de senha inicial. Oferecer “Preparar para usar offline”: baixar catálogo, fichas e histórico recente em um snapshot consistente, inicialmente 90 dias. Mostrar quando o preparo terminou e o alcance do histórico disponível.

Persistir em IndexedDB: metadados não secretos da conta, catálogo/fichas baixados, sessões/séries com snapshots, outbox, conflitos, cursor e preferências de rede. Particionar tudo por ID de usuário. TanStack Query e memória não são a fonte durável de dados offline.

| Função | Offline |
|---|---|
| Consultar exercícios/fichas já preparados | Sim |
| Iniciar, registrar, concluir, corrigir ou excluir treino | Sim, incluindo carga opcional |
| Relatórios do histórico local e pendências | Sim, identificados como locais/parciais |
| Criar/alterar catálogo e fichas | Online no MVP |
| Login inicial, cadastro de usuário e operações de senha | Online |

Salvar a alteração local e sua intenção de sincronização na **mesma transação IndexedDB**. Solicitar armazenamento persistente quando suportado, tratar recusa/quota e não limpar pendências automaticamente. O navegador pode remover dados locais e a pessoa pode limpar o armazenamento; o aplicativo deve informar o estado de sincronização e não prometer backup antes do envio. [14]

Atualizações do service worker/IndexedDB preservam a outbox; não forçar reload no meio de um treino. Versionar o schema local e o envelope de sync, manter leitura de versões pendentes durante a atualização e oferecer a ativação da nova versão após salvar o formulário.

### 8.6 Sincronização preferencial por Wi-Fi

**Limite de plataforma:** um PWA não identifica Wi-Fi com confiabilidade em todos os navegadores, nem garante execução com o app fechado. `NetworkInformation.type` e Background Sync têm disponibilidade limitada. A experiência garantida inclui sincronização com o app aberto e uma alternativa manual. [12–13]

Política padrão `wifi_only`:

- Se `navigator.connection?.type === 'wifi'`, houver API acessível e autenticação válida, tentar automaticamente ao abrir/retomar o app e nos eventos de conexão.
- Se o tipo for `cellular`, não sincronizar automaticamente. O usuário pode autorizar uma execução em outra rede de forma explícita.
- Se o tipo não existir, for `unknown` ou outra rede, manter pendente e oferecer “Estou no Wi-Fi — sincronizar agora”. Essa confirmação vale para a execução atual, não vira uma identificação técnica permanente.
- Não deduzir Wi-Fi de `effectiveType`, velocidade, SSID ou `navigator.onLine`. O primeiro não indica a tecnologia física e `onLine` sozinho não prova acesso ao backend.
- Antes de enviar, consultar um endpoint leve sem cache para verificar alcance da API. Nova perda de conexão mantém as operações para retry.
- Background Sync é melhoria opcional: respeita a mesma política e só envia se conseguir avaliá-la. Se o navegador encerrar o worker ou não suportar a API, retomar no próximo uso do app. Não depender de timers permanentes no service worker.

A política cobre upload e atualização automática dos dados de treino. A preparação inicial e operações online explicitamente solicitadas pelo usuário podem usar a conexão atual. Se for indispensável garantir disparo em Wi-Fi com app fechado em todo celular, será necessário avaliar integração nativa; isso não é uma garantia possível do PWA deste plano.

### 8.7 Protocolo de sincronização e idempotência

Usar **uma única outbox de negócio** para a sessão inteira (exercícios e séries), em vez de enfileirar requests HTTP crus ou manter duas filas com Workbox. O cliente gera IDs UUID estáveis. Também quando estiver online, o registro de treino percorre armazenamento local → outbox → servidor.

Envelope por operação: `operationId`, `deviceId`, `schemaVersion`, `entityId` da sessão, `kind=create|replace|delete`, `baseVersion`, snapshot do treino e metadados locais. O proprietário é sempre obtido da autenticação; comparar a conta ativa à partição local antes do envio. `deviceId` não é credencial.

Estados locais: `pending`, `sending`, `synced`, `conflict`, `failed` e `auth_required`. Exibir contagem de pendências, última sincronização, motivo de bloqueio e ação de tentar novamente.

1. Coordenar aba/worker com lease transacional em IndexedDB para evitar dois emissores. Serializar operações do mesmo treino; lotes pequenos podem conter sessões distintas.
2. Capturar o estado a enviar. Antes da primeira tentativa, é permitido coalescer edições; ao alterar um payload, gerar novo `operationId`. Depois de enviada uma tentativa, congelar ID/hash/payload para retries.
3. Fazer `GET /auth/me` e obter CSRF atual. Enviar a intenção usando cookies; nunca reaproveitar headers de autenticação antigos salvos localmente.
4. O servidor bloqueia `user_sync_state` do proprietário, consulta recibo e valida a operação. Mesmo ID e mesmo hash retorna a resposta gravada; mesmo ID com payload diferente retorna 409 `IDEMPOTENCY_KEY_REUSED`.
5. Para nova sessão, exigir `baseVersion=0`, ID livre e referências pertencentes à conta. Nas criações por sync, preencher `client_request_id` com `operationId` e `request_hash` com o hash canônico correspondente. Colisão de ID sem recibo válido é conflito.
6. Para alteração/exclusão, exigir versão base igual à atual. Validar o agregado completo: séries, lados, cargas nulas, datas e conclusão, usando os mesmos casos de uso da API.
7. Na **mesma transação MariaDB**, persistir sessão/filhos, incrementar versão do agregado e revisão do usuário, gravar `sync_changes` e `sync_operation_receipts`. Commit precede o ACK.
8. No cliente, gravar ACK/versão e remover a operação pendente na mesma transação local. Se o usuário editou durante o envio, preservar a revisão local mais nova e gerar a próxima operação com a versão retornada como base; não apagá-la ao aplicar o ACK.

O servidor pode ter aplicado uma operação cuja resposta se perdeu. Reenviar com o mesmo ID recupera o recibo e não duplica treino/série. Não aplicar TTL curto aos recibos no MVP.

Se um treino criado localmente for excluído antes de qualquer tentativa de envio, cancelar a criação e seus dados locais na mesma transação, sem enviar um delete de recurso inexistente. Se a criação já tiver sido tentada e o resultado for incerto, primeiro recuperar seu recibo por retry e depois enviar a exclusão com a versão confirmada; não abandonar a operação que pode ter sido aplicada no servidor.

Sessões criadas offline usam a prescrição que estava disponível no dispositivo. Durante sync, aceitar referências próprias arquivadas para preservar um treino já realizado; isso é uma exceção ao bloqueio de iniciar novas fichas online com itens arquivados. Validar propriedade e estrutura dos snapshots recebidos; nunca aplicar snapshots de outra conta ou confiar nos números enviados para relatórios sem validar os registros reais.

### 8.8 Conflitos, recebimento e exclusões

Se a versão divergir, retornar conflito com versão/snapshot atual. Preservar a cópia local e oferecer comparar, adotar a versão do servidor ou reaplicar a edição após revisão, com novo ID de operação e versão base atual. Não usar “último horário vence”. Em delete contra edit, exigir escolha explícita, sem ressuscitar registros por merge automático.

O feed usa revisão **monotônica por usuário**, alocada sob trava da linha `user_sync_state` e confirmada com a alteração. Todas as escritas sincronizáveis, inclusive REST online de catálogo/fichas, participam desse protocolo. Não usar apenas timestamp nem um AUTO_INCREMENT global sujeito a commits fora de ordem.

`GET /sync/changes?after=...&limit=...` retorna mudanças ordenadas, `nextCursor`, `hasMore` e um limite de revisão observado. Aplicar dados e cursor juntos no IndexedDB. Upserts contêm snapshots canônicos; exclusões contêm tombstones. Receber um item que tem edição local pendente não pode substituí-lo silenciosamente.

`GET /sync/bootstrap` lê dados e revisão em snapshot transacional consistente e devolve escopo/limites do histórico. Para conjuntos grandes, implementar snapshot paginado materializado com token fixo; não paginar um snapshot inicial vivo enquanto a base muda. Clientes mantêm a janela local informada, sem interpretar ausência fora dessa janela como exclusão.

Se o cursor for anterior à retenção do feed, retornar 410 `SYNC_CURSOR_EXPIRED`. O seed emite `reset_required`. Nos dois casos, baixar novo snapshot preservando outbox, conflitos e sessões locais não enviadas; depois reconciliar por ID/versão. Limpar dados já sincronizados antigos é permitido apenas conforme a política de cache, nunca as pendências.

Após novo sync, invalidar relatórios online e atualizar a visão local sem duplicar o mesmo UUID. Relatórios offline mostram o intervalo disponível, dados pendentes e horário da última atualização.

### 8.9 Autenticação offline e tratamento de falhas

Offline significa acesso local a dados previamente preparados; não é novo login nem validação de senha sem servidor. Expiração de cookie não impede salvar um treino local no perfil já aberto, mas a sincronização exige autenticação online válida.

- 401: pausar como `auth_required`, conservar dados e pedir login da mesma conta.
- 403 de CSRF: renovar uma vez; se persistir, bloquear e informar, sem loop de tentativas.
- Falha de rede/5xx/429: retry com backoff, jitter e respeito a `Retry-After`; manter ID/payload.
- Erro de validação: marcar a operação para correção, sem repetição infinita nem descartar treino.
- Logout: encerrar a sessão quando online e bloquear a partição local. Pendências permanecem associadas ao proprietário até seu próximo login; “Apagar dados deste dispositivo” exige confirmação se houver pendências. Não permitir que outra conta envie ou visualize dados dessa partição.
- Antes de cada lote/worker, comparar conta autenticada com o dono da outbox. Troca de conta pausa a fila anterior.

## 9. Relatórios de progressão

Filtros comuns: período inclusivo por data local, ficha e exercício. Limitar consultas interativas inicialmente a 366 dias; paginação no histórico. Os dados pertencem sempre ao usuário autenticado.

| Relatório | Definição |
|---|---|
| Frequência | Quantidade de sessões concluídas por semana/mês e dias distintos com treino |
| Cargas por exercício | Maior carga registrada em série de trabalho por sessão, mostrando repetições/lado junto ao ponto |
| Repetições | Repetições por série e total registrado; comparação opcional na mesma carga |
| Duração | Maior duração de série e soma de segundos registrados, especialmente para prancha |
| Séries registradas | Contagem de séries de trabalho concluídas; não estimar séries no modo rápido |
| Volume registrado | Soma de `load_kg * repetitions` apenas em séries elegíveis e com carga conhecida |

Regras para evitar gráficos enganosos:

1. Excluir aquecimento, séries não concluídas e sessões não concluídas/excluídas.
2. Agrupar comparação por exercício e convenção compatível. Não juntar agachamento livre com hack, equipamentos diferentes ou kg por halter com kg totais.
3. Carga ausente não vira zero. Usar `NULL`/lacuna no gráfico e “Carga não informada” na tabela.
4. Volume em `kg × repetições` descreve a **convenção registrada**, não trabalho físico total. Não multiplicar automaticamente por dois por haver dois halteres. Para lados separados, somar os registros de cada lado; não duplicá-los novamente.
5. Não somar volume entre convenções diferentes em um indicador global. Exibir por exercício/variação. Excluir exercícios assistidos e por duração desse cálculo no MVP.
6. Exibir cobertura: séries com carga / séries de trabalho medidas elegíveis. Se houver ausência, chamar o resultado de “volume parcial”. Se nenhuma carga foi informada, retornar `null`, não 0.
7. Para exercícios assistidos, menos assistência pode ser evolução; mostrar o contexto e não tratar maior número como recorde positivo.
8. Variação percentual só com dois pontos comparáveis e base maior que zero. Caso contrário, `null`/“Sem base de comparação”.
9. Datas sem treino podem ter zero na frequência, mas não zero no gráfico de carga. Um peso maior com menos repetições não deve ganhar automaticamente o rótulo “Você evoluiu”.
10. Não incluir estimativa de 1RM ou de percentual de gordura no MVP.

Exemplos de aceitação do cálculo:

- `10 reps × 20 kg` e `8 reps × carga nula`: volume parcial 200; cobertura 1/2; carga máxima 20.
- Duas séries com carga nula: volume e carga máxima nulos, frequência do treino preservada.
- Prancha de 30 s e 45 s: total 75 s e máximo 45 s, sem volume em kg × reps.
- Afundo com 10 reps à esquerda e 10 à direita, carga 8 kg por halter: volume registrado 160 nessa convenção; sem multiplicação adicional pelos dois halteres.
- Excluir ou corrigir um treino muda os resultados da consulta seguinte.

Implementar fórmulas em políticas testáveis e projeções SQL equivalentes. A projeção offline TypeScript deve seguir o mesmo contrato e fixtures numéricas do backend, sem inventar fórmulas. Mesclar sessões locais e sincronizadas por UUID, contabilizando uma única versão por sessão. O servidor continua autoritativo; a visão offline é marcada como local/parcial e pode incluir pendências.

## 10. Contratos HTTP

Prefixo `/api`, JSON camelCase, IDs UUID, erros ProblemDetails. Exemplos abaixo são contratos a implementar, não endpoints já disponíveis.

| Método e rota | Finalidade |
|---|---|
| `GET /auth/csrf` | Obter token antiforgery |
| `POST /admin/users` | Administrador cadastra usuário comum; negar acesso a comum/anônimo |
| `POST /auth/forgot-password` | Solicitar recuperação por e-mail |
| `POST /auth/reset-password` | Definir nova senha com token válido |
| `POST /auth/login` | Abrir sessão autenticada |
| `POST /auth/logout` | Encerrar sessão |
| `GET /auth/me` | Conta atual, ID, fuso, perfil e obrigação de trocar senha |
| `POST /auth/change-password` | Alterar senha |
| `GET /exercises?q=&page=&pageSize=` | Catálogo ativo paginado |
| `POST /exercises` | Criar exercício |
| `GET /exercises/{id}` | Consultar exercício próprio |
| `PUT /exercises/{id}` | Editar com `expectedVersion` |
| `DELETE /exercises/{id}?expectedVersion=` | Arquivar e informar fichas afetadas |
| `GET, POST /plans` | Listar/criar planos |
| `GET, PUT, DELETE /plans/{id}` | Consultar/editar/arquivar plano |
| `POST /plans/{id}/templates` | Criar ficha |
| `GET, PUT, DELETE /templates/{id}` | Consultar/editar/arquivar ficha; PUT salva itens e alternativas |
| `POST /sessions` | Compatibilidade online: iniciar sessão; reusar pipeline de escrita/versionamento do sync |
| `GET /sessions?from=&to=&page=` | Histórico |
| `GET /sessions/{id}` | Sessão com snapshots e séries |
| `PUT /sessions/{id}` | Data, notas, modo e correções |
| `POST /sessions/{id}/exercises` | Adicionar exercício; ID do item gerado pelo cliente para retry |
| `PUT /sessions/{id}/exercises/{itemId}` | Estado realizado/pulado e observações |
| `DELETE /sessions/{id}/exercises/{itemId}` | Remover lançamento equivocado e suas séries em transação |
| `PUT /sessions/{id}/exercises/{itemId}/sets/{setId}` | Criar/alterar série de ID estável |
| `DELETE /sessions/{id}/exercises/{itemId}/sets/{setId}` | Excluir série registrada por engano |
| `POST /sessions/{id}/complete` | Concluir treino |
| `DELETE /sessions/{id}?expectedVersion=` | Excluir logicamente a sessão |
| `GET /sync/bootstrap` | Snapshot consistente para preparar/recuperar a base local |
| `POST /sync/push` | Lote de intenções de sessão, com resultado individual por operação |
| `GET /sync/changes?after=&limit=` | Feed de atualizações e tombstones |
| `GET /health/connectivity` | Verificação leve de conectividade, sem dados pessoais e sem cache |
| `GET /reports/frequency?from=&to=` | Frequência |
| `GET /reports/exercises/{exerciseId}?from=&to=` | Progressão detalhada |

Toda escrita em agregados existentes recebe `expectedVersion` no corpo; DELETE usa query porque não depende de corpo. Retornar a nova versão. Nas rotas aninhadas, conferir usuário, sessão, item e série; não confiar apenas no último ID.

Exemplo de série concluída sem carga:

```json
{
  "expectedVersion": 4,
  "setNumber": 1,
  "side": "both",
  "repetitions": 10,
  "durationSeconds": null,
  "loadKg": null,
  "rir": 2,
  "isWarmup": false,
  "isCompleted": true
}
```

Usar 201 para criação, 200/204 para sucesso, 400 para validação, 401 para ausência de autenticação, 404 para recurso inexistente/inacessível, 409 para conflito e 429 para limite de tentativas. Todos os endpoints de negócio exigem autenticação; login, CSRF, recuperação/reset por token e health de conectividade são exceções explicitamente configuradas. `POST /sync/push` autentica o lote e retorna status por operação; falha de uma sessão não confirma nem descarta as demais. Limitar lote e payload, inicialmente a 20 operações e 1 MiB, e documentar paginação de respostas.

## 11. Telas e experiência na academia

1. **Entrar / recuperar senha:** login, “Esqueci minha senha”, nova senha e troca obrigatória no primeiro acesso. Sem cadastro público.
2. **Início:** próximo treino sugerido, sessão em andamento, botão “Registrar treino” e frequência recente.
3. **Exercícios:** busca, grupo muscular, criar, editar e excluir com confirmação contextual.
4. **Planos e fichas:** A/B/C, reordenação acessível por botões, metas, descanso e alternativas.
5. **Treino do dia:** campos grandes, carga opcional explícita, última carga como referência e gravação visível.
6. **Histórico:** calendário/lista, abrir sessão, corrigir data/série e excluir registro.
7. **Progressão:** período, exercício, convenção, gráficos, tabela e aviso de dados parciais.
8. **Conta:** nome, fuso, alteração/recuperação de senha, logout e dados locais.
9. **Usuários (somente administrador):** cadastrar conta comum com senha inicial, sem acessar treinos alheios.
10. **Offline e sincronização:** preparar dispositivo, alcance dos dados locais, preferência Wi-Fi, pendências, última sincronização, conflitos e ação manual com confirmação da rede.

Priorizar celular, navegação com uma mão, contraste adequado e alvos de toque de pelo menos 44 px. Campos numéricos aceitam vírgula na interface pt-BR e enviam números JSON normalizados. Não converter data `YYYY-MM-DD` por `new Date()` com deslocamento de fuso; tratá-la como data civil.

## 12. Carga inicial dos treinos

O arquivo **`002_carga_inicial_fullbody.sql`** faz parte deste plano. Ele é um script concreto, sem placeholders de exercícios: contém todos os INSERTs e a transação de importação.

Conteúdo esperado: **1 plano, 3 fichas, 23 posições de exercícios e 23 exercícios no catálogo**, incluindo o hack como alternativa. A elevação lateral é reutilizada nas fichas A e C. “Tríceps na polia com corda” e “Tríceps na polia” permanecem variações separadas, para não misturar cargas de acessórios possivelmente diferentes.

### Full Body A — segunda-feira

| Ordem | Exercício | Séries | Meta | Descanso |
|---:|---|---:|---|---|
| 1 | Agachamento livre **ou hack** | 3 | 6–10 reps | 120–180 s |
| 2 | Supino reto com halteres | 3 | 8–12 reps | 120–180 s |
| 3 | Remada na máquina com peito apoiado | 3 | 8–12 reps | 120 s |
| 4 | Mesa flexora | 3 | 10–15 reps | 90 s |
| 5 | Elevação lateral | 2 | 12–20 reps | 60–90 s |
| 6 | Tríceps na polia com corda | 2 | 10–15 reps | 60–90 s |
| 7 | Panturrilha em pé | 3 | 10–15 reps | 60–90 s |
| 8 | Prancha abdominal | 2 | 30–45 segundos | 60 s |

### Full Body B — quarta-feira

| Ordem | Exercício | Séries | Meta | Descanso |
|---:|---|---:|---|---|
| 1 | Levantamento terra romeno | 3 | 8–10 reps | 120–180 s |
| 2 | Supino inclinado com halteres | 3 | 8–12 reps | 120–180 s |
| 3 | Puxada pela frente, pegada confortável | 3 | 8–12 reps | 120 s |
| 4 | Afundo reverso com halteres | 3 por perna | 8–12 reps por perna | 120 s após ambas |
| 5 | Desenvolvimento de ombros sentado | 2 | 8–12 reps | 120 s |
| 6 | Rosca direta | 2 | 10–15 reps | 60–90 s |
| 7 | Panturrilha sentada | 3 | 12–20 reps | 60–90 s |

### Full Body C — sexta-feira

| Ordem | Exercício | Séries | Meta | Descanso |
|---:|---|---:|---|---|
| 1 | Leg press | 3 | 10–15 reps | 120–180 s |
| 2 | Supino na máquina | 3 | 8–12 reps | 120 s |
| 3 | Remada baixa na polia | 3 | 8–12 reps | 120 s |
| 4 | Cadeira flexora | 3 | 10–15 reps | 90 s |
| 5 | Elevação lateral | 2 | 12–20 reps | 60–90 s |
| 6 | Rosca martelo | 2 | 10–15 reps | 60–90 s |
| 7 | Tríceps na polia | 2 | 10–15 reps | 60–90 s |
| 8 | Abdominal na polia | 2 | 10–15 reps | 60–90 s |

O plano inicial mantém 8 semanas, aquecimento de 5–8 minutos, cardio de 15–20 minutos, RIR de 1–3 e a orientação de duas séries na semana de adaptação para itens originalmente com três. O seed não cria treinos já realizados, não preenche cargas e não cria administradores ou senhas.

Escolhas de equipamento que a ficha não especificava foram explicitadas no catálogo: halteres na elevação lateral/desenvolvimento, barra na rosca direta/terra romeno e máquina nas panturrilhas. Podem ser ajustadas antes do primeiro registro; depois, cadastrar outra variação quando necessário.

### Execução do esquema e do seed

1. Inicializar o MariaDB dentro do pod `treinos-pod`, com banco vazio, por exemplo `treinos`, e usuário de migração autorizado.
2. Aplicar migrações de estrutura `001_schema_treinos.sql` e `003_pwa_usuarios_email.sql`, nessa ordem, uma única vez cada. O arquivo 002 é seed e não entra nessa ordem de migração.
3. Provisionar o administrador pelo comando BootstrapAdministrator/Identity no pod. Ele pode cadastrar o usuário comum pela aplicação. Entrar com a conta que receberá os treinos, trocar a senha inicial se necessário e obter o UUID em `GET /api/auth/me`.
4. Abrir o cliente MariaDB **dentro do container do banco**, sem instalar esse cliente no host. `-p` solicita a senha sem incluí-la na linha de comando. Montar `database/` como somente leitura em `/opt/treinos/database` no container para esta operação.

```bash
podman exec -it treinos-db mariadb --default-character-set=utf8mb4 -h 127.0.0.1 -u treinos_migrator -p treinos
```

No cliente, primeiro, apenas no banco vazio:

```sql
SOURCE /opt/treinos/database/migrations/001_schema_treinos.sql;
SOURCE /opt/treinos/database/migrations/003_pwa_usuarios_email.sql;
```

Em banco já existente, aplicar apenas a nova migração 003, sem repetir a 001. Depois que a conta tiver sido provisionada, em uma conexão com o mesmo banco:

```sql
SET @seed_user_id = 'COLE-AQUI-O-UUID-REAL-DA-SUA-CONTA';
SOURCE /opt/treinos/database/seeds/002_carga_inicial_fullbody.sql;
```

Substituir o marcador pelo UUID real. O arquivo valida conta existente e aborta antes de inserir caso ela não exista. Não usar `--force` e não criar usuário com senha fixa no seed.

`DELIMITER` é uma instrução do cliente `mariadb`; não enviar o arquivo bruto com esse comando para Dapper/MySqlCommand. O bloco `BEGIN NOT ATOMIC` é específico do MariaDB. [8]

O seed é transacional, usa a linha `user_sync_state` como trava por proprietário e registra `fullbody-3dias-v1` em `seed_imports`. A primeira carga incrementa a revisão e emite `reset_required` em `sync_changes`, para o PWA atualizar as fichas sem perder pendências. Reexecutar para o mesmo usuário não duplica dados, não sobrescreve alterações e não restaura itens excluídos. Cada usuário pode receber uma cópia própria. Colisão inesperada de códigos sem o marcador de importação causa rollback, em vez de substituir dados silenciosamente.

Para modificar a carga inicial no futuro, criar uma nova versão e uma estratégia explícita de atualização; não remover o marcador para reaplicar por cima das personalizações.

## 13. Implantação obrigatória com Podman e um único pod

### 13.1 Contrato de execução

**Todos os serviços necessários à aplicação devem ser containerizados com Podman e pertencer ao mesmo pod da instância, chamado `treinos-pod`.** Conectar containers independentes a uma rede não satisfaz esse requisito. Desenvolvimento e testes usam seus próprios pods isolados, cada qual com os serviços daquele ambiente.

O host fornece sistema operacional, Podman e systemd/Quadlet. O PWA é baixado desses serviços e executa também no navegador do usuário; seu funcionamento offline não depende de um container rodando no celular. Node/npm, SDK/runtime .NET, proxy, MariaDB e ferramentas de manutenção pertencem às imagens. Não exigir esses componentes instalados diretamente no host. Builds são executados em estágios de Containerfiles pelo Podman; todos os containers funcionais de execução e manutenção entram no pod da instância.

### 13.2 Composição e rede

| Container | Função | Porta no pod | Ciclo de vida |
|---|---|---|---|
| `treinos-web` | Proxy HTTPS e arquivos estáticos do React | 8443; 8080 opcional para redirecionamento | Contínuo |
| `treinos-api` | API ASP.NET Core .NET 10 | 8081 | Contínuo |
| `treinos-db` | MariaDB 11.8 | 3306 | Contínuo |
| `treinos-migrator` | Aplicar migrações versionadas | Nenhuma | Executa e termina antes de liberar a API |
| `treinos-maintenance` | Seed, backup e restauração | Nenhuma | Transitório, associado ao mesmo pod |

Em produção, Node é usado no build e o proxy serve o React compilado, manifest, ícones e service worker. Usar assets com hash e cache longo; servir manifest/service worker com revalidação, sem cache imutável. Excluir `/api/auth`, `/api/admin`, `/api/sync` e respostas pessoais do cache HTTP genérico do service worker. Qualquer nova dependência necessária também deve ser entregue em container desse pod.

Os containers compartilham a rede do pod e devem usar portas distintas. Configurar proxy → API em `http://127.0.0.1:8081` e API/migrator/manutenção → banco em `127.0.0.1:3306`. O browser usa `/api` na origem do frontend. Publicar apenas as portas web **no pod**, sem `-p` nos containers individuais. [10]

API e banco podem escutar somente no loopback compartilhado; não publicar 8081 ou 3306 no host. Exemplo rootless: HTTPS externo em 8443. Portas padrão 80/443 dependem da configuração autorizada do host e devem ser documentadas, sem alterações automáticas de política do sistema.

Os membros do pod não têm isolamento de rede entre si. Manter nele somente os componentes dessa aplicação e configurar forwarded headers para o proxy interno confiável.

### 13.3 Persistência e segredos

- `treinos-db-data`: dados MariaDB em `/var/lib/mysql`.
- `treinos-dp-keys`: chaves Data Protection usadas pela API.
- `treinos-tls`: certificados/material TLS do proxy, com acesso restrito.
- `treinos-backups`: saída dos backups, com retenção e cópia de segurança definida.

Montar volumes explicitamente apenas nos containers que precisam deles; o pod não compartilha arquivos automaticamente. SQLs e configurações são montados como somente leitura. Em hosts com SELinux, configurar rótulos compatíveis com o uso exclusivo ou compartilhado de cada diretório.

Usar Podman secrets ou arquivos de segredo protegidos. Implementar a leitura desses arquivos na aplicação quando necessário; o .NET não interpreta automaticamente qualquer variável com sufixo `_FILE`. Separar credenciais de API, migração e backup. Não incluir segredos no Git, nas imagens ou nos logs. Parar, atualizar ou remover o pod não deve apagar volumes persistentes por padrão.

### 13.4 Quadlet e inicialização

Usar Quadlet/systemd como implantação canônica: um arquivo `treinos.pod`, unidades `.container` com `Pod=treinos.pod` e unidades `.volume`. Homologar as opções na versão Podman do servidor. Preferir rootless com usuário dedicado quando compatível e documentar inicialização no boot/lingering. [11]

Sequência do script de implantação:

1. Construir/baixar imagens com versões ou digests fixados; criar secrets, volumes e pod.
2. Iniciar MariaDB e aguardar readiness com timeout e tentativas limitadas.
3. Rodar `treinos-migrator` no mesmo pod e exigir código de saída zero.
4. Iniciar API, aguardar readiness e liberar frontend/proxy.
5. Executar BootstrapAdministrator no pod; após provisionar a conta, executar o seed explicitamente com o UUID do proprietário. A criação de novas contas inicializa também `user_sync_state`.

A ordem das unidades systemd não substitui readiness. O migrator é tarefa finita, sem reinício infinito. Cada atualização executa a migração da nova versão antes de liberar a API; falha interrompe a implantação. Processos contínuos têm política de reinício e limites apropriados.

Exemplo mínimo ilustrativo, a completar com as unidades dos containers na implementação:

```ini
# deploy/quadlet/treinos.pod
[Pod]
PodName=treinos-pod
PublishPort=8443:8443
ExitPolicy=continue

[Install]
WantedBy=default.target
```

Cada `.container` do ambiente declara `Pod=treinos.pod`. Tarefas manuais usam `podman run --pod treinos-pod ...`, com os volumes e secrets necessários. A entrega atual é um plano; as unidades completas e scripts operacionais serão implementados na etapa de infraestrutura.

### 13.5 Migrações e operação

- O migrator mantém `schema_migrations` com versão, checksum e data, além das dez tabelas originais e das quatro tabelas acrescentadas pela migração 003. Serializar migrações com lock de banco na mesma conexão.
- DDL MariaDB pode fazer commits implícitos; interromper e diagnosticar erros parciais antes de continuar. Não prometer rollback integral de DDL.
- API usa permissões de dados; migração usa permissões de estrutura; manutenção recebe as permissões necessárias.
- Seed, backup e restauração usam cliente MariaDB em container e conexão interna ao banco. Um timer systemd pode disparar o backup, mas o trabalho acontece em `treinos-maintenance` no pod.
- Não reaplicar a carga inicial automaticamente ao reiniciar containers.
- Oferecer liveness da API, readiness de banco/migrações e health checks dos serviços.
- Logs estruturados seguem para stdout/stderr, consultáveis via Podman/journald, sem dados de autenticação ou tokens em URLs.
- O dispatcher de e-mails executa como BackgroundService na API, portanto dentro do mesmo pod. Credencial SMTP vem de Podman secret. O servidor SMTP externo será configurado pelo operador; não exigir instalação de SMTP no host.
- Para testes de e-mail, usar SMTP fake/container de captura dentro do pod de teste; não enviar a destinatários reais. Backup inclui fila de e-mail e as chaves necessárias para descriptografá-la.
- Validar backup/restauração e preservação de dados, certificados e chaves em reinícios/atualizações.

Entregar comandos para build, subir/parar o pod, logs, migração, seed, backup/restauração e atualização. Um Compose isolado não atende ao requisito; o contrato é Podman + Quadlet com todos os containers da aplicação no mesmo pod.

## 14. Etapas de implementação e critérios de aceite

| Etapa | Entrega | Critério de aceite |
|---|---|---|
| 1. Fundação | .NET 10, React/TS, Podman, Quadlet e MariaDB | Build em containers; um pod por ambiente; conexão homologada |
| 2. Identidade | Migrações 001/003, bootstrap admin, perfis, cookies e CSRF | Comum não cadastra usuários; admin cadastra; isolamento preservado |
| 3. Recuperação | Telas, tokens Identity, SMTP configurável e outbox | Testes de reset com SMTP de captura; aplicação funciona com e-mail desabilitado |
| 4. Catálogo e seed | CRUD/fichas e seed atualizado | A=8, B=7, C=8; 23 exercícios; seed idempotente e feed notificado |
| 5. PWA e offline | Manifest, service worker, IndexedDB e outbox | Preparar online e depois abrir/registrar treino em modo avião |
| 6. Sincronização | Wi-Fi/fallback, recibos, cursor, conflitos e retomada | Não duplicar após retry; não perder edições; reautenticar conta correta |
| 7. Relatórios | Projeções online e locais | Mesmas fixtures; cargas nulas e pendências tratadas corretamente |
| 8. Homologação | Dispositivos reais, backup/restauração e implantação | Rodar casos offline, redes não identificáveis, troca de conta e atualizações |

Fechar o contrato de sync e persistência local antes de acabamento visual. Cadastro por administrador e recuperação por e-mail fazem parte desta versão, ainda que o operador só configure SMTP posteriormente.

## 15. Testes essenciais

- **Arquitetura:** Domain não referencia infraestrutura/ASP.NET; controllers não referenciam Dapper, conexão ou repositórios concretos.
- **Identity/perfis:** senha nunca em texto puro; normalização, lockout, concorrência, logout, CSRF e revogação após alteração/reset. Comum/anônimo não cria conta nem injeta perfil; admin não acessa treinos de terceiros; bootstrap não reseta senha ao repetir.
- **E-mail:** serviço desabilitado sem travar o app; respostas sem enumeração; SMTP de captura; token válido, inválido, expirado, reutilizado e dois resets concorrentes; fila protegida, lease expirado, retry e URL pública correta.
- **Autorização:** tentar ler, alterar, excluir, vincular e relatar recursos de outro usuário, incluindo IDs em rotas aninhadas.
- **Persistência:** integração no MariaDB escolhido, não apenas SQLite/in-memory; conferir FKs, DECIMAL, CHECKs, transações e concorrência.
- **Podman:** conferir com `podman ps --pod` e inspeção que todos os containers pertencem ao pod; apenas portas web são publicadas; API/banco comunicam-se por loopback; reiniciar/recriar o pod preserva dados e chaves; falha do migrator impede liberar a API. Seed e backup executam em containers do pod. Validar restauração em ambiente de teste isolado.
- **Datas:** registro próximo de meia-noite UTC respeita o dia de São Paulo; treino retroativo aparece em `performed_on`.
- **Carga opcional:** concluir sem carga; distinguir null de zero; nunca copiar última carga para realizado sem confirmação.
- **Snapshots:** editar nome/meta e arquivar exercício preserva sessão anterior; iniciar ficha com item arquivado exige correção.
- **Unilateral/duração:** validar prancha em segundos, afundo por lado e descanso após ambas.
- **Relatórios:** usar os exemplos numéricos da seção 9, sessões rápidas, aquecimento, cancelamento e exclusão.
- **Concorrência:** retry de criação não duplica; duas abas editando produzem 409; gravação é atômica.
- **Seed:** conta ausente aborta; primeira carga completa; segunda execução sem alterações; dois usuários isolados; execução concorrente para a mesma conta não duplica.
- **Offline:** primeiro acesso sem preparo informa necessidade de conexão; após preparar, modo avião permite abrir, salvar, concluir, corrigir, excluir e recarregar sem perda. Validar quota, falha de transação e migração IndexedDB com pendências.
- **Wi-Fi:** testar tipo `wifi`, `cellular`, ausente e `unknown`; sem API de Background Sync, com app fechado e ao retomá-lo. Não inferir tecnologia de `effectiveType`; fallback manual funciona em Safari/iOS e nos demais alvos homologados.
- **Sync:** resposta perdida após commit não duplica; duas abas/worker não alteram payload de retry; edição durante envio é preservada; conflito entre dispositivos não sobrescreve silenciosamente; exclusão não ressuscita registro.
- **Feed:** escritas concorrentes não deixam lacunas perdidas; bootstrap/cursor coerentes; 410/reset_required preservam a outbox; alteração de catálogo e seed chegam ao cliente.
- **Contas:** cookie expirado pausa sync; A sai e B entra sem receber dados de A; A volta e sincroniza; senha redefinida exige reautenticação sem perder treinos pendentes.
- **E2E:** bootstrap admin → criar comum → trocar senha inicial → importar fichas → preparar PWA → treinar em modo avião → recarregar → reconectar/sincronizar → consultar progressão → recuperar senha com SMTP de teste.

## 16. Instruções para o agente implementador

1. Tratar este plano como escopo do MVP e os SQLs como contrato inicial, reconciliando qualquer mudança de schema com seed e testes.
2. Implementar regras na Application/Domain e persistência na Infrastructure; controllers apenas adaptam HTTP.
3. Priorizar a jornada vertical: bootstrap/admin cria usuário, login, preparo offline, treino sem carga em modo avião, sincronização e histórico.
4. Não preencher registros realizados a partir das metas, não gerar usuários com senha padrão e não expor dados entre contas.
5. Não substituir MariaDB ou .NET 10 por conveniência de biblioteca. Não adicionar provider EF incompatível.
6. Documentar comandos de desenvolvimento, migração, seed, testes e implantação no README do projeto.
7. Entregar o MVP somente após executar os testes essenciais em MariaDB real.
8. Implementar execução integral em Podman, com todos os containers funcionais da aplicação em um único pod por ambiente. Não exigir Node, .NET, proxy ou MariaDB no host. Entregar Containerfiles, Quadlets, volumes, secrets e scripts da seção 13.
9. Implementar PWA offline e recuperação de senha nesta versão, não como trabalho futuro. Remover cadastro público e não conceder ao administrador acesso aos treinos de outras contas.
10. Informar honestamente a limitação de Wi-Fi/background por navegador e entregar os fallbacks; não simular garantia universal de sincronização com o app fechado.

## 17. Validação desta entrega e limites

Os arquivos desta entrega são o plano e os SQLs, não um aplicativo pronto. A sequência, as metas, os descansos e os casos especiais foram conferidos contra o PDF Full Body A/B/C. Foi feita verificação estática de consistência entre catálogo, fichas, schema e seed.

**O SQL não foi executado em MariaDB neste ambiente**, pois não há servidor/cliente MariaDB, Docker ou Podman disponíveis. A execução real e os testes de idempotência, rollback, constraints e concorrência são uma etapa obrigatória de homologação, antes de usar em produção. O schema de referência e a migração 003 também precisam ser integrados aos stores Identity e ao protocolo de sincronização. Esta revisão não foi executada em navegadores, Podman ou SMTP; a validação de PWA, filas e reset é requisito de homologação.

## 18. Referências técnicas

Fontes consultadas em 29/09/2026; verificar novamente versões na implementação.

1. [.NET — política oficial de suporte](https://dotnet.microsoft.com/en-us/platform/support/policy).
2. [MySqlConnector — compatibilidade com MariaDB](https://mysqlconnector.net/).
3. [MySqlConnector — histórico, incluindo suporte ao .NET 10](https://mysqlconnector.net/overview/version-history/) e [uso com Dapper](https://mysqlconnector.net/tutorials/dapper/).
4. [Pomelo — releases e compatibilidade de EF Core](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases).
5. [Microsoft — stores personalizados do ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-custom-storage-providers?view=aspnetcore-10.0).
6. [Microsoft — proteção antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).
7. [React — criar uma aplicação com ferramentas de build](https://react.dev/learn/build-a-react-app-from-scratch).
8. [MariaDB — blocos compostos fora de stored programs](https://mariadb.com/kb/en/using-compound-statements-outside-of-stored-programs/).
9. Fonte dos exercícios: `Plano_Full_Body_3_Dias_Sostenes.pdf`, gerado nesta conversa em 29/09/2026.

10. [Podman — criação de pod e publicação de portas](https://docs.podman.io/en/latest/markdown/podman-pod-create.1.html).
11. [Podman — Quadlet e unidades systemd](https://docs.podman.io/en/latest/markdown/podman-systemd.unit.5.html).

12. [MDN — NetworkInformation.type e disponibilidade](https://developer.mozilla.org/en-US/docs/Web/API/NetworkInformation/type).
13. [MDN — Background Synchronization API](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API).
14. [MDN — quotas e remoção de armazenamento local](https://developer.mozilla.org/en-US/docs/Web/API/Storage_API/Storage_quotas_and_eviction_criteria).
15. [Microsoft — recuperação de senha com Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-10.0).
