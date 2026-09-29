# Execução com Podman

Todos os containers funcionais entram em `treinos-pod`. Instale as unidades Quadlet do diretório `quadlet/` em `~/.config/containers/systemd/` do usuário dedicado e ajuste o caminho de `database/` nas unidades para o checkout real. Crie os segredos `treinos-db-root-password`, `treinos-db-password` e `treinos-admin-password` com `podman secret create` a partir de arquivos protegidos. Não guarde os valores no checkout.

Crie `~/.config/treinos/api.env` com:

```ini
ConnectionStrings__Treinos=Server=127.0.0.1;Port=3306;Database=treinos;User ID=treinos_api;
Database__PasswordFile=/run/secrets/db-password
DataProtection__KeysPath=/var/lib/treinos/keys
Email__Enabled=false
```

Monte certificados TLS válidos em `treinos-tls` como `fullchain.pem` e `privkey.pem`. O proxy escuta 8443. Execute `deploy/scripts/build.sh`, recarregue o systemd do usuário, inicie pod e banco, espere o banco responder, rode `deploy/scripts/migrate.sh`, inicie API e web, e use `deploy/scripts/bootstrap-admin.sh`. O seed é separado: defina `TREINOS_USER_ID` e `TREINOS_DB_PASSWORD`, depois rode `deploy/scripts/seed.sh`. Os SQL de origem estão copiados em `database/`.

A unidade de migração é de execução única; a sequência de readiness deve ser aplicada pelo operador antes de ativar API/web. Para uso interativo, `deploy/scripts/logs.sh`, `stop.sh`, `start.sh` e `backup.sh` estão disponíveis. A restauração exige parar a API e importar um backup do volume `treinos-backups` com o cliente MariaDB dentro do pod. Preserve também as chaves Data Protection e os certificados.
