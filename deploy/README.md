# Implantação e operação

## Ambiente

Use Linux com Podman rootless, systemd do usuário e Quadlet. No macOS, `deploy/scripts/test.sh` usa a máquina Podman, sem depender de systemd no host. Todos os containers funcionais de cada ambiente pertencem ao mesmo pod. Apenas o proxy publica a porta 8443; API e MariaDB escutam na rede interna do pod.

Os Containerfiles recebem imagens por digest de `deploy/images.conf`. As versões NuGet ficam em `backend/Directory.Packages.props`, com lockfiles; o frontend utiliza `npm ci` e `package-lock.json`. Ao atualizar imagens, ajuste também o digest do banco em `quadlet/treinos-db.container` e execute novamente a homologação.

## Preparar o servidor

Disponibilize o checkout em `~/treinos` ou substitua `%h/treinos` nos volumes das unidades Quadlet pelo caminho real. Crie os diretórios `~/.config/containers/systemd/` e `~/.config/treinos/`. Copie os arquivos de `deploy/quadlet/` para o primeiro.

Crie cinco Podman secrets a partir de arquivos protegidos, fora do checkout:

```sh
podman secret create treinos-db-root-password /caminho/root-password
podman secret create treinos-db-password /caminho/api-password
podman secret create treinos-db-migrator-password /caminho/migrator-password
podman secret create treinos-db-backup-password /caminho/backup-password
podman secret create treinos-admin-password /caminho/admin-password
```

Use senhas distintas. A senha inicial do administrador precisa de 12 a 128 caracteres. `db-init.sh` configura, no primeiro início de um volume vazio, os usuários `treinos_api` (dados), `treinos_migrator` (estrutura e dados) e `treinos_backup` (leitura e dump). Um volume existente não executa novamente o init; um operador precisa provisionar essas contas antes de adotar as novas unidades.

Crie `~/.config/treinos/api.env`:

```ini
ConnectionStrings__Treinos=Server=127.0.0.1;Port=3306;Database=treinos;User ID=treinos_api;
Database__PasswordFile=/run/secrets/db-password
DataProtection__KeysPath=/var/lib/treinos/keys
Email__Enabled=false
App__PublicBaseUrl=https://seu-dominio:8443
```

O arquivo não contém senhas. Mantenha certificados válidos para o domínio em um diretório protegido, contendo `fullchain.pem` e `privkey.pem`.

## Subir

```sh
deploy/scripts/build.sh
systemctl --user daemon-reload
systemctl --user start treinos-migrator.service
```

O migrator inicia o pod/banco, aguarda conexão por até 60 segundos, aplica as migrações com lock e checksum e termina. DDL MariaDB não oferece rollback integral: uma falha parcial exige diagnóstico antes de repetir. A API depende do sucesso desta unidade.

Copie os certificados para o volume antes de iniciar web:

```sh
podman run --rm --pod treinos-pod -v /caminho/certificados:/input:ro -v treinos-tls:/certs --entrypoint sh localhost/treinos-maintenance:local -ec 'cp /input/fullchain.pem /input/privkey.pem /certs/; chmod 600 /certs/privkey.pem'
deploy/scripts/start.sh
```

Confirme `/api/health/ready` por HTTPS. Readiness verifica banco e as migrações 001/003; `/api/health/live` e `/api/health/connectivity` são sondas simples. API responses usam `no-store`; service worker guarda somente o shell e seus arquivos estáticos.

Provisione o administrador:

```sh
export BOOTSTRAP_ADMIN_EMAIL='operador@seu-dominio'
export BOOTSTRAP_ADMIN_NAME='Operador'
deploy/scripts/bootstrap-admin.sh
```

Reexecutar não troca a senha. Para promover uma conta existente, informe explicitamente `BOOTSTRAP_PROMOTE_USER_ID`. O procedimento invalida suas sessões anteriores.

Depois de criar uma conta comum, defina seu UUID e importe a ficha quando desejado:

```sh
export TREINOS_USER_ID='UUID da conta'
deploy/scripts/seed.sh
```

O seed ocorre dentro do pod, é idempotente por usuário e emite a mudança no feed. Não é reaplicado durante reinícios.

## E-mail

O app funciona com `Email__Enabled=false`; recuperação retorna `EMAIL_NOT_CONFIGURED`. Para ativar SMTP, adicione ao arquivo de ambiente:

```ini
Email__Enabled=true
Email__Host=smtp.seu-provedor
Email__Port=587
Email__TlsMode=StartTls
Email__FromAddress=treinos@seu-dominio
Email__FromName=Treinos
Email__Username=usuario-smtp
Email__PasswordFile=/run/secrets/smtp-password
```

Crie `treinos-smtp-password` e adicione `Secret=treinos-smtp-password,type=mount,target=/run/secrets/smtp-password` à unidade API. Use `SslOnConnect` quando o provedor exigir TLS direto. Atualize a URL pública HTTPS; o link não usa o Host enviado pelo cliente. Tokens Identity expiram em uma hora. A fila armazena payload protegido pelas chaves Data Protection, usa lease e até cinco tentativas, e limpa o link após envio/expiração. Nunca habilite SMTP de captura em produção.

## Backup, restauração e atualização

```sh
deploy/scripts/backup.sh
export TREINOS_RESTORE_NAME='treinos-AAAAMMDDTHHMMSSZ'
export TREINOS_CONFIRM_RESTORE=RESTORE
deploy/scripts/restore.sh
```

A retenção remove eventos do feed após 90 dias e mensagens encerradas após 30 dias; recibos de operações não são apagados.

O backup cria um diretório no volume `treinos-backups` com `database.sql`, `keys.tar` e `tls.tar`. Ele inclui recibos, fila de e-mail e chaves necessárias à recuperação. Copie os backups para armazenamento externo protegido conforme a política do operador.

Restore substitui dados: para API/web, valida os arquivos, importa o dump com a conta de migração, recupera chaves/certificados e reinicia os serviços após o migrator. Se falhar, mantenha os serviços parados e consulte a causa antes de tentar novamente. Homologue primeiro em ambiente separado.

```sh
deploy/scripts/update.sh
deploy/scripts/logs.sh treinos-api
deploy/scripts/stop.sh
podman ps --pod
```

Atualização faz backup, build, parada da API/web, migração e reinício. Os volumes de dados, chaves e TLS permanecem. A configuração do banco e seus secrets também devem ser mantidos pelo operador.

## Testes

```sh
deploy/scripts/test.sh
```

O comando gera um pod isolado e credenciais descartáveis. Valida permissões de dados sem DDL, repete o seed, roda xUnit contra MariaDB, Vitest e Playwright com HTTPS local. Mailpit captura os e-mails dentro do mesmo pod. O navegador usa uma exceção de certificado exclusivamente para esse ambiente; a configuração de produção usa certificados válidos. O teste de backup importa o dump em outro schema e compara tabelas, chaves e certificados. Nenhum serviço existente é removido.

Safari/iOS, Android real, retomada após suspensão, quotas de armazenamento, certificados públicos e systemd/Quadlet no servidor de destino ainda precisam de homologação operacional.
