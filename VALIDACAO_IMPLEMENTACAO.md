# Validação da implementação

Última execução completa: **30/09/2026**, em Podman sobre macOS ARM64, com containers Linux no mesmo pod descartável e MariaDB 11.8 real.

## Resultado executado

| Validação | Resultado |
|---|---|
| Build API/migrator/PWA/testes, imagens fixadas por digest | Passou |
| Migrações 001/003/004, journal/checksum/lock | Passou |
| Bootstrap via Identity e carga SQL original | Passou |
| Seed repetido, A=8/B=7/C=8 e 23 exercícios | Passou |
| API sem permissão de DDL; migração e backup com contas próprias | Passou |
| xUnit, incluindo integração MariaDB | 22 testes passaram |
| Vitest, outbox/contas/conflitos/lease/fórmulas | 13 testes passaram |
| Playwright Chromium, HTTPS e SMTP de captura | 4 testes passaram |
| Dump importado em schema separado, comparação de tabelas | Passou |
| Backup/restauração de chaves e certificados | Passou |

O comando reproduzível é `deploy/scripts/test.sh`. Nenhum teste usa SQLite como substituto do MariaDB.

Os testes de navegador conferem CSRF, cadastro restrito ao administrador, rejeição de perfil injetado, troca obrigatória da senha inicial, proibição de cadastro por conta comum, entrega no Mailpit, reset e rejeição do token reutilizado, revogação do cookie, preparo do PWA, recarregamento e nova aba offline, conclusão rápida, persistência e sincronização sem duplicação, e conclusão detalhada sem preencher carga.

Os testes de integração conferem stores Identity, normalização e hash, concorrência de atualização/reset, isolamento de referências, recibo recuperado após exclusão, retenção do feed sem apagar recibos, comandos online com o mesmo pipeline de versões e histórico paginado fora dos 90 dias do bootstrap.

A validação da nova arquitetura cobre criação de usuários pelo painel, dois treinos na mesma conta, ficha com múltiplos exercícios, seleção do proprietário por cabeçalho e rota, isolamento dos dados do administrador, proibição de cadastro e promoção por contas comuns e bloqueio da tela administrativa. A integração MariaDB também cobre duas fichas em cada treino, instruções com mais de 70.000 caracteres, preservação de snapshots e metadados de duração/carga, feeds por proprietário, edição e exclusão lógica de usuários, controle de versão, revogação de sessões e proteção do último administrador.

As jornadas de navegador executam em dois grupos, com reinício da API entre eles, para respeitar o limite real de dez requisições de autenticação por minuto sem reduzir a proteção da aplicação.

## Implementado nesta continuação

- Identity com stores Dapper e serviços de conta/recuperação fora dos controllers.
- Hierarquia usuário → treinos → fichas → exercícios, cadastro simplificado de exercícios e migração 004.
- Administração de usuários comuns e administradores e CRUD dos dados de qualquer proprietário.
- CRUD de fichas e exercícios, snapshots de prescrição, alternativas e adaptação.
- Registro rápido/detalhado, lados, duração, carga opcional, aquecimento e correções.
- IndexedDB por conta, fila congelada após tentativa, lease, backoff, recebimento de mudanças, conflito explícito e exclusões.
- Reabertura offline com conta local preservada; logout bloqueia esse acesso.
- Rotas de sessão e itens usando comandos completos e recibos idempotentes.
- Histórico online com período/página; relatórios locais/online e cache separado por conta.
- Cursor consistente, versão em tombstones e retenção de 90 dias; recibos permanecem.
- PWA com ícones, shell precacheado, exclusão de `/api` do fallback e aviso de atualização.
- Quadlet com dependência do migrator, secrets, contas de banco distintas, backup/restauração e atualização.
- Dependências NuGet centralizadas e lockfiles; imagens centralizadas por digest.

## Homologação operacional pendente

Ainda requer execução no ambiente/dispositivo de destino:

- Safari/iOS e Android reais, instalação standalone, suspensão e retomada prolongada.
- Tipo de rede Wi-Fi/celular indisponível, quotas de armazenamento e eviction pelo navegador.
- systemd/Quadlet em Linux, reinício do servidor e recriação dos serviços com os volumes de produção.
- Certificados públicos, domínio final e SMTP do operador.
- Exercícios de atualização/rollback e restauração completa em outro servidor, com configuração/secrets externos.

Os quatro testes de Chromium e a restauração dentro do pod validam a jornada automatizada; não constituem homologação desses dispositivos nem implantação em produção. A execução com o aplicativo fechado e a identificação de Wi-Fi dependem do navegador; o envio manual permanece disponível.

## Compose em HTTP

O `compose.yaml` foi validado com Podman 6.1.2 e podman-compose 1.6.0: serviços no mesmo pod e namespace de rede, banco saudável, migrações concluídas e readiness pelo frontend. Um pod separado validou bootstrap do administrador, CSRF, login, cookie e leitura do bootstrap por HTTP. O pod `pod_treinos-compose` está disponível em `http://localhost:8080`.

O merge do `compose.https.yaml` foi conferido com domínio/e-mail de exemplo; nenhuma emissão de certificado Let’s Encrypt foi solicitada. A configuração HTTP utiliza Development; a configuração opcional HTTPS ativa Production e cookies seguros.

## Atualização da arquitetura no Compose local

A migração 004 foi aplicada ao volume existente em 30/09/2026. O dump anterior está em `/private/tmp/treinos-compose-pre-arquitetura.sql`, com permissão 0600. Frontend e backend foram atualizados; o banco permaneceu no mesmo volume. Readiness, CSRF, login do administrador com a senha existente, listagem de usuários e leitura dos treinos pela nova rota foram confirmados em `http://localhost:8080`. A verificação encontrou uma conta ativa e um treino cadastrado, preservados da instalação anterior.
