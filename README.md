# Remote Wake

Plataforma open source e self-hosted para ligar, desligar, reiniciar, suspender e hibernar computadores remotamente. O projeto combina Wake-on-LAN, uma PWA instalável, uma API autenticada e componentes locais para funcionar dentro e fora de casa.

> O projeto está em desenvolvimento. API, interface, gateway e agente têm testes automatizados e foram validados ponta a ponta em simulação; o teste com computadores e redes reais ainda está pendente. Consulte [o guia remoto](docs/REMOTE_SETUP.md).

## Por que este projeto existe?

Aplicativos tradicionais de Wake-on-LAN geralmente resolvem apenas o envio local de um Magic Packet. O Remote Wake pretende oferecer uma experiência completa:

- painel web responsivo e instalável como PWA;
- múltiplos usuários, cada um com suas próprias máquinas;
- Wake-on-LAN dentro da rede local;
- Wake-on-WAN por IP público ou DDNS;
- integração com VPN do roteador;
- gateway para redes Tailscale e conexões atrás de CGNAT;
- agente Windows/Linux para desligar, reiniciar, suspender e hibernar;
- confirmação de que a máquina realmente ligou;
- histórico, auditoria e verificação em duas etapas;
- instalação self-hosted com Docker.

## Como funciona?

```text
PWA / navegador
      │ HTTPS
      ▼
Remote Wake API
      │
      ├── UDP direto ───────────────► roteador/rede local ─► PC desligado
      │
      └── gateway residencial ─────► LAN/Tailscale ───────► PC desligado

Quando o PC estiver ligado:

Remote Wake API ◄── conexão segura ── Agente Windows/Linux
```

Wake-on-LAN liga ou desperta a máquina. Desligar, reiniciar, suspender e hibernar exigem o agente instalado no computador, pois uma máquina desligada não executa programas. Quando o agente se conecta logo depois de um pedido de Ligar, o histórico registra que a máquina ligou.

## Modos de ativação

| Modo | Funcionamento | Requisitos |
|---|---|---|
| Rede local/VPN | Envia o pacote ao broadcast da LAN | API/gateway dentro da LAN ou VPN que encaminhe o pacote |
| Wake-on-WAN | Envia UDP para um IP público ou DDNS | IP público, redirecionamento de porta e IP/MAC binding |
| Gateway residencial | Um processo ligado na residência envia o pacote dentro da LAN | Gateway sempre ligado e API acessível por HTTPS ou Tailscale |

O Tailscale instalado somente no computador desligado não consegue acordá-lo. A API e o gateway devem permanecer ligados em outro equipamento, como NAS, Raspberry Pi ou mini PC. O EX511 sozinho só substitui o gateway se seu firmware oferecer a função necessária. Veja [como configurar gateway e agente](docs/REMOTE_SETUP.md).

## Tecnologias

- **API:** ASP.NET Core 10 / C#
- **Persistência:** Entity Framework Core (migrações) e PostgreSQL
- **Autenticação:** sessões no servidor com cookie `HttpOnly`, verificação em duas etapas (TOTP) e hash de senha do ASP.NET Core Identity
- **Web/PWA:** React, TypeScript e Vite
- **Produção:** Nginx, Caddy (HTTPS automático) e imagens amd64/arm64
- **Execução local:** Docker Compose
- **Testes:** xUnit v3 com PostgreSQL real, Playwright e GitHub Actions

O .NET foi escolhido porque oferece excelente integração com serviços do Windows, suporte multiplataforma para Linux, boa biblioteca de rede e uma base adequada para compartilhar código entre API, gateway e agente (`src/shared`).

## Início rápido com Docker

### Pré-requisitos

- Docker Desktop no Windows/macOS ou Docker Engine no Linux;
- Docker Compose 2.24 ou superior;
- placa-mãe e placa Ethernet compatíveis com Wake-on-LAN.

### 1. Clone o projeto

```bash
git clone https://github.com/Victordemelo/wake-on-lan.git
cd wake-on-lan
```

### 2. Configure o ambiente

No Linux/macOS:

```bash
cp .env.example .env
```

No PowerShell:

```powershell
Copy-Item .env.example .env
```

Preencha `POSTGRES_PASSWORD` com um valor aleatório próprio; o Compose não inicia sem ele. Para usar gateway e agentes, preencha também `GATEWAY_KEY`, `GATEWAY_OWNER_EMAIL` e `AGENT_MASTER_KEY`. Para gerar uma chave:

```bash
openssl rand -base64 48
```

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

### 3. Inicie os serviços

```bash
docker compose up --build -d
```

Acesse:

- aplicação: <http://localhost:8005>
- API: <http://localhost:8080>
- saúde da API e do banco: <http://localhost:8080/health>
- OpenAPI em desenvolvimento: <http://localhost:8080/openapi/v1.json>

Na primeira execução, o PostgreSQL e as tabelas são criados automaticamente pelas migrações. As atualizações de esquema também são aplicadas na inicialização, inclusive em bancos criados por versões anteriores.

### 4. Crie a primeira conta

Enquanto não houver contas, a API exibe um **código de configuração** no log. Informe-o na tela de cadastro:

```bash
docker compose logs api | grep "Código de configuração"
```

Para automatizar a instalação, defina `SETUP_TOKEN` no `.env`. Depois da primeira conta, o cadastro fica fechado. Para criar outras contas sem abrir o cadastro, use a linha de comando:

```bash
docker compose exec api dotnet RemoteWake.Api.dll create-user pessoa@exemplo.com Nome da pessoa
```

Para encerrar sem apagar o banco:

```bash
docker compose down
```

Para apagar também os dados locais, execute conscientemente:

```bash
docker compose down --volumes
```

Portas e nome do projeto podem ser alterados no `.env` (`WEB_PORT`, `API_PORT`, `COMPOSE_PROJECT_NAME`), por exemplo para manter duas instalações na mesma máquina.

## Demonstração sem hardware

O perfil `demo` sobe um gateway que envia pacotes apenas para o loopback do próprio contêiner e um agente em modo simulação: nenhum PC é ligado ou desligado.

1. Cadastre uma máquina com o método **Gateway residencial / Tailscale** e destino `127.0.0.1`.
2. Use o e-mail da conta em `GATEWAY_OWNER_EMAIL` e preencha `GATEWAY_KEY` e `AGENT_MASTER_KEY`.
3. Abra a chave do agente no card da máquina e copie o ID e a chave para `DEMO_AGENT_MACHINE_ID` e `DEMO_AGENT_KEY` no `.env`.
4. Inicie: `docker compose --profile demo up -d --build`.

O painel mostra gateway e agente online; Ligar, Desligar, Reiniciar, Suspender e Hibernar respondem em simulação.

## Primeiro teste de Wake-on-LAN

Antes do teste, habilite Wake-on-LAN na BIOS/UEFI e nas propriedades da placa de rede do Windows.

1. Entre no Remote Wake.
2. Clique em **Nova máquina**.
3. Informe o MAC da placa Ethernet, por exemplo `AA:BB:CC:DD:EE:FF`.
4. Para rede local, use o broadcast da rede, por exemplo `192.168.1.255`.
5. Mantenha a porta `9`.
6. Salve e clique em **Ligar**.

### Observação importante sobre Docker

Broadcast UDP saindo de um contêiner pode não alcançar a rede física no Docker Desktop (Windows/macOS/WSL2). No Linux, o perfil `gateway-linux` usa a rede do host. No Windows/macOS, execute o worker do gateway diretamente no host, a partir de uma pasta do disco local, ou em outro dispositivo sempre ligado na LAN. Veja [o guia remoto](docs/REMOTE_SETUP.md).

## Colocar em produção

Veja [docs/DEPLOY.md](docs/DEPLOY.md): acesso pelo Tailscale sem abrir portas, ou servidor com domínio e HTTPS automático via Caddy (`compose.prod.yaml`).

## Desenvolvimento sem Docker

### API

Requer o SDK .NET 10 e PostgreSQL:

```bash
dotnet run --project src/api/RemoteWake.Api.csproj
```

As configurações podem ser fornecidas por `appsettings.Development.json`, variáveis de ambiente ou user secrets. Nunca faça commit de credenciais reais. Para criar uma migração depois de alterar o modelo:

```bash
dotnet ef migrations add NomeDaMudanca --project src/api/RemoteWake.Api.csproj --output-dir Data/Migrations
```

### Interface web

Requer Node.js 22 ou superior:

```bash
cd src/web
npm ci
npm run dev
```

O Vite encaminha `/api` para `http://localhost:8080` durante o desenvolvimento.

## Testes

```bash
dotnet test --solution RemoteWake.slnx
```

Os testes de unidade rodam sempre. Os testes de API e de migração sobem a aplicação real contra um PostgreSQL descartável; defina a conexão antes (cada teste cria e apaga o próprio banco):

```bash
export REMOTE_WAKE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres"
```

Na interface: `npm run lint` e `npm run build` em `src/web`.

Com Docker em execução, `./scripts/Test-Integration.ps1` (PowerShell 7 ou Windows PowerShell) cria banco, API, gateway e agente descartáveis e testa sessão, isolamento entre usuários, edição, histórico, revogação e reinício. O gateway envia apenas para loopback e o agente simula as ações; nenhum PC é desligado.

O GitHub Actions executa tudo isso a cada push e pull request (`.github/workflows/ci.yml`). Tags `v*.*.*` publicam as imagens no GitHub Container Registry (`release.yml`).

## Estrutura do repositório

```text
.
├── src/
│   ├── api/        # API, autenticação, persistência, migrações e Magic Packet
│   ├── web/        # PWA em React e TypeScript
│   ├── worker/     # executável do gateway e do agente
│   ├── shared/     # Magic Packet e contrato entre API e worker
│   ├── gateway/    # orientação do gateway residencial
│   └── agent/      # orientação do agente Windows/Linux
├── tests/          # testes de unidade, de API e de migração
├── deploy/         # Caddyfile, unidade systemd e exemplo de configuração
├── docs/           # arquitetura, deploy e guias
├── scripts/        # instalação do worker no Windows e teste de integração
├── compose.yaml
├── compose.prod.yaml
├── .env.example
└── README.md
```

## API atual

A interface usa um cookie de sessão `HttpOnly`. Toda requisição que altera dados precisa do cabeçalho `X-Remote-Wake-Request: 1` (proteção contra CSRF).

| Método | Rota | Autenticação | Descrição |
|---|---|---|---|
| `GET` | `/api/auth/registration` | Não | Informa se o cadastro está aberto e se falta a primeira conta |
| `POST` | `/api/auth/register` | Não | Cria a conta (a primeira exige o código de configuração) |
| `POST` | `/api/auth/login` | Não | Entra; com duas etapas ativas, pede o código |
| `POST` | `/api/auth/logout` | Sessão | Encerra a sessão atual |
| `GET` | `/api/auth/me` | Sessão | Dados da conta conectada |
| `POST` | `/api/account/password` | Sessão | Troca a senha e desconecta os outros dispositivos |
| `GET` | `/api/account/sessions` | Sessão | Dispositivos conectados |
| `DELETE` | `/api/account/sessions/{id}` | Sessão | Encerra uma sessão |
| `POST` | `/api/account/sessions/revoke-others` | Sessão | Desconecta os outros dispositivos |
| `GET` | `/api/account/events` | Sessão | Últimos eventos de segurança |
| `POST` | `/api/account/two-factor/setup` | Sessão | Gera a chave e o QR do aplicativo autenticador |
| `POST` | `/api/account/two-factor/enable` | Sessão | Confirma o código e devolve os códigos de recuperação |
| `POST` | `/api/account/two-factor/disable` | Sessão | Desativa (exige senha e código) |
| `POST` | `/api/account/two-factor/recovery-codes` | Sessão | Gera novos códigos de recuperação |
| `GET` | `/api/machines` | Sessão | Lista as máquinas do usuário |
| `POST` | `/api/machines` | Sessão | Cadastra uma máquina |
| `PUT` | `/api/machines/{id}` | Sessão | Atualiza uma máquina |
| `DELETE` | `/api/machines/{id}` | Sessão | Remove uma máquina (o histórico é mantido) |
| `POST` | `/api/machines/{id}/wake` | Sessão | Envia o Magic Packet |
| `POST` | `/api/machines/{id}/actions` | Sessão | Pede `shutdown`, `restart`, `suspend` ou `hibernate` ao agente |
| `POST` | `/api/machines/{id}/agent-key` | Sessão | Obtém a chave do agente da própria máquina |
| `DELETE` | `/api/machines/{id}/agent-key` | Sessão | Revoga a chave atual do agente |
| `GET` | `/api/activity` | Sessão | Últimas 100 ações e confirmações do proprietário |
| `GET` | `/api/gateway/poll` | Chave do gateway | Long polling do gateway residencial |
| `POST` | `/api/gateway/jobs/{id}/complete` | Chave do gateway | Resultado de um envio do gateway |
| `GET` | `/api/agent/{machineId}/poll` | Chave do agente | Long polling do agente |
| `POST` | `/api/agent/{machineId}/jobs/{id}/complete` | Chave do agente | Resultado de uma ação do agente |
| `GET` | `/health` | Não | Verifica a API e a conexão com o banco |

## Serviços

Veja [instalação Windows/systemd](docs/SERVICE_INSTALL.md). No Windows, o serviço é reiniciado automaticamente após falhas.

## Segurança

O Remote Wake controla máquinas e deve ser tratado como software sensível.

- Sessões ficam no servidor: o navegador guarda apenas um cookie `HttpOnly` e `SameSite=Strict` (com `Secure` e prefixo `__Host-` sob HTTPS). Sair, trocar a senha ou desconectar dispositivos encerra as sessões imediatamente.
- Verificação em duas etapas (TOTP) com códigos de recuperação de uso único.
- A primeira conta exige o código de configuração exibido no log da API; o cadastro público fica fechado por padrão.
- Limites de tentativas por IP (login) e por conta (comandos de energia); atrás do Caddy ou do nginx a API recebe o IP real do cliente.
- Histórico com IP de origem e eventos de segurança visíveis no painel (logins, falhas, trocas de senha, duas etapas).
- Recuperação de conta sem e-mail pela linha de comando: `docker compose exec api dotnet RemoteWake.Api.dll reset-password voce@exemplo.com`.
- Cabeçalhos CSP, HSTS (em produção) e demais proteções no nginx e no Caddy.
- O agente aceita somente `shutdown`, `restart`, `suspend` e `hibernate`; shell arbitrário não é permitido.
- Magic Packets não possuem autenticação; a segurança deve estar no acesso ao sistema e à rede.

Antes de expor a instalação, siga o checklist de [docs/DEPLOY.md](docs/DEPLOY.md) e consulte [SECURITY.md](SECURITY.md).

## Roadmap

- [x] Estrutura inicial e Docker Compose
- [x] Cadastro, login e CRUD de máquinas
- [x] Geração e envio do Magic Packet
- [x] Histórico das tentativas e auditoria visível no painel
- [x] Testes automatizados da API, do pacote WOL e das migrações, com CI
- [x] Edição de máquinas pela PWA
- [x] Gateway com conexão de saída autenticada para LAN/Tailscale (chave manual)
- [x] Worker compatível com Windows Service e systemd
- [x] Status online/offline e confirmação de que a máquina ligou
- [x] Desligar, reiniciar, suspender e hibernar com confirmação
- [x] Sessões seguras, 2FA, código de configuração e recuperação de conta
- [x] Deploy com HTTPS e imagens publicadas (amd64/arm64)
- [ ] Teste em computadores e redes reais
- [ ] Pareamento de gateways por código e múltiplos gateways
- [ ] Ações e scripts locais previamente autorizados
- [ ] Internacionalização português/inglês

Veja detalhes em [docs/ROADMAP.md](docs/ROADMAP.md).

## Contribuindo

Issues, sugestões e pull requests são bem-vindos. Leia [CONTRIBUTING.md](CONTRIBUTING.md) antes de contribuir e não publique vulnerabilidades em issues públicas.

## Licença

Distribuído sob a licença MIT. Consulte [LICENSE](LICENSE).
