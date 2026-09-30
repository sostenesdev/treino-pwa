# Treinos

PWA de treinos com React/TypeScript, API .NET 10, Identity com stores Dapper e MariaDB 11.8. Catálogo e fichas ficam disponíveis offline após o preparo. Registros e correções são gravados em IndexedDB antes do envio, com fila por conta, recibos idempotentes, versões e resolução explícita de conflitos.

## Executar e validar

Para subir com o `compose.yaml` da raiz, use o provedor `podman-compose`:

```sh
export PODMAN_COMPOSE_PROVIDER=podman-compose
deploy/scripts/compose-prepare.sh
podman compose up -d --build
podman compose --profile maintenance run -T --rm --no-deps bootstrap-admin
```

Para personalizar porta e conta inicial, copie `.env.example` para `.env` e ajuste os valores antes de subir.

Acesse **http://localhost:8080**. `front`, `back`, `db` e o migrator pertencem ao pod `pod_treinos-compose`; somente a porta do frontend é publicada. Banco e chaves usam volumes persistentes. O script gera senhas aleatórias em `.secrets/`, preserva arquivos existentes e não imprime os valores. O administrador local usa `admin@example.invalid`; sua senha está em `.secrets/admin-password`. Configure `BOOTSTRAP_ADMIN_EMAIL` e `BOOTSTRAP_ADMIN_NAME` antes do comando de bootstrap se desejar outros valores.

O Compose padrão usa HTTP e ambiente `Development`, com cookies compatíveis com HTTP. HTTPS pode ser ativado posteriormente com o Caddy/Let’s Encrypt, sem alterar as imagens:

```sh
export TREINOS_DOMAIN=treinos.seu-dominio.com
export LETSENCRYPT_EMAIL=operador@seu-dominio.com
podman compose down
podman compose -f compose.yaml -f compose.https.yaml up -d
```

A configuração opcional publica 80/443, solicita e renova certificados automaticamente, guarda-os em `caddy-data` e ativa cookies seguros no backend. O domínio deve apontar para o servidor, e as portas 80/443 devem estar disponíveis externamente. Use `podman compose -f compose.yaml -f compose.https.yaml` nos comandos seguintes enquanto essa configuração estiver ativa. É necessário recriar o pod para mudar suas portas; `down` preserva os volumes. O arquivo opcional utiliza `!override`, suportado pelo `podman-compose` 1.6 usado na validação.

Em `localhost`, navegadores permitem service worker mesmo com HTTP. Para acessar o PWA offline por um endereço de rede ou domínio, ative HTTPS. Veja também [implantação](deploy/README.md).

```sh
podman compose ps
podman compose logs -f
podman compose down
```

`down` preserva os volumes; `down -v` apaga os dados. Para mudar a porta do host, defina `TREINOS_WEB_PORT`; a porta interna permanece 80. `TREINOS_SECRETS_DIR` permite usar outro diretório de segredos. A recuperação por e-mail fica desabilitada nessa configuração local.

O ambiente completo precisa de Podman; Node, .NET e MariaDB executam em containers do mesmo pod. Para produção em Linux, veja [implantação e operação](deploy/README.md). No macOS, os testes usam a máquina Podman; as unidades Quadlet destinam-se ao systemd do servidor Linux.

```sh
deploy/scripts/build.sh
deploy/scripts/test.sh
```

O segundo comando cria um pod descartável, gera credenciais de teste, aplica as migrações, provisiona o administrador, executa a carga duas vezes, verifica permissões, roda xUnit/Vitest/Playwright e restaura o backup em outro schema. O SMTP de captura permanece no mesmo pod. Ao terminar, remove somente os recursos criados por essa execução.

Se desejar trabalhar com os SDKs locais:

```sh
dotnet restore backend/Treinos.slnx --locked-mode
dotnet build backend/Treinos.slnx --no-restore -m:1
npm ci --prefix frontend
npm test --prefix frontend
npm run build --prefix frontend
```

Os testes de integração exigem `TREINOS_TEST_CONNECTION`; sem ela, são explicitamente ignorados. Use `test.sh` para a validação completa em MariaDB real. HTTPS é necessário para testar cookie seguro e service worker.

## SQL e primeiro acesso

Os scripts de `Webapp_Treinos_Plano_e_SQL/` foram preservados em `database/migrations/` e `database/seeds/`. O migrator registra checksum e serializa execução. O seed é uma operação separada para uma conta já criada: A com 8 exercícios, B com 7 e C com 8, totalizando 23 definições. Não cria usuários nem treinos realizados.

Somente o operador provisiona administradores. Administradores cadastram contas comuns, cuja primeira senha deve ser trocada antes de acessar os treinos. A recuperação utiliza tokens Identity, fila criptografada e SMTP configurável; fica desabilitada enquanto o operador não configurar o serviço.

## API e persistência

Controllers adaptam HTTP; regras ficam em Domain/Application, e SQL/Identity/SMTP em Infrastructure. As rotas `/api/sessions` e suas rotas aninhadas usam um comando completo:

```json
{ "operationId": "UUID", "deviceId": "UUID", "baseVersion": 1, "snapshot": { "id": "UUID", "exercises": [] } }
```

O exemplo é estrutural: envie uma sessão válida com todos os snapshots e campos obrigatórios. As alterações de item/série enviam o agregado resultante; remoções enviam o agregado anterior e o serviço remove o ID da rota. Reenvie exatamente o mesmo comando até receber o ACK. `DELETE /sessions/{id}` também exige `expectedVersion` na query, igual a `baseVersion`. Histórico online aceita `from`, `to` e `page`, com 50 registros por página e período máximo de 366 dias.

O protocolo offline usa `/api/sync/operations` ou `/push`, `/bootstrap` e `/changes`. Conflitos incluem a versão e o snapshot atuais. Cursor expirado ou seed provocam novo bootstrap preservando pendências. O cache dos relatórios, IndexedDB e a fila são separados por ID de conta.

## Uso offline e limites

Entre online e escolha **Preparar offline**. Depois é possível abrir novamente o aplicativo, registrar, concluir, corrigir ou excluir treinos sem rede. Sair bloqueia o acesso local e preserva pendências para a próxima autenticação da mesma conta. O dispositivo guarda dados pessoais localmente; a tela de conta permite removê-los após confirmação.

A sincronização automática ocorre quando o navegador identifica `wifi`, ao abrir/retomar o aplicativo. Sem essa informação, use **Estou no Wi-Fi — sincronizar**. Não há garantia de envio com o aplicativo fechado. Relatórios locais são identificados como parciais e incluem pendências; carga não informada permanece nula e não vira zero.

Consulte [validação e pendências de homologação](VALIDACAO_IMPLEMENTACAO.md). Os testes em Chromium não substituem testes em dispositivos reais com Safari/iOS e Android, certificados públicos e SMTP de produção.
