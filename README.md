# Remote Wake

Plataforma open source e self-hosted para ligar, desligar e reiniciar computadores remotamente. O projeto combina Wake-on-LAN, uma PWA instalável, uma API autenticada e componentes locais para funcionar dentro e fora de casa.

> O projeto está em desenvolvimento inicial. O gateway e o agente já têm uma primeira implementação, mas exigem configuração local e ainda precisam ser testados na rede e nos computadores reais. Consulte [o guia remoto](docs/REMOTE_SETUP.md).

## Por que este projeto existe?

Aplicativos tradicionais de Wake-on-LAN geralmente resolvem apenas o envio local de um Magic Packet. O Remote Wake pretende oferecer uma experiência completa:

- painel web responsivo e instalável como PWA;
- múltiplos usuários, cada um com suas próprias máquinas;
- Wake-on-LAN dentro da rede local;
- Wake-on-WAN por IP público ou DDNS;
- integração com VPN do roteador;
- gateway para redes Tailscale e conexões atrás de CGNAT;
- agente Windows/Linux para desligar, reiniciar e executar ações autorizadas;
- histórico e auditoria das operações;
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

Wake-on-LAN liga ou desperta a máquina. Desligar, reiniciar ou executar ações exige o agente instalado no computador, pois uma máquina desligada não executa programas.

## Modos de ativação

| Modo | Funcionamento | Requisitos |
|---|---|---|
| Rede local/VPN | Envia o pacote ao broadcast da LAN | API/gateway dentro da LAN ou VPN que encaminhe o pacote |
| Wake-on-WAN | Envia UDP para um IP público ou DDNS | IP público, redirecionamento de porta e IP/MAC binding |
| Gateway residencial | Um processo ligado na residência envia o pacote dentro da LAN | Gateway sempre ligado e API acessível por HTTPS ou Tailscale |

O Tailscale instalado somente no computador desligado não consegue acordá-lo. A API e o gateway devem permanecer ligados em outro equipamento, como NAS, Raspberry Pi ou mini PC. O EX511 sozinho só substitui o gateway se seu firmware oferecer a função necessária. Veja [como configurar gateway e agente](docs/REMOTE_SETUP.md).

## Tecnologias

- **API:** ASP.NET Core 10 / C#
- **Persistência:** Entity Framework Core e PostgreSQL
- **Autenticação:** JWT Bearer e hash de senha do ASP.NET Core Identity
- **Web/PWA:** React, TypeScript e Vite
- **Produção web:** Nginx
- **Execução local:** Docker Compose

O .NET foi escolhido porque oferece excelente integração com serviços do Windows, suporte multiplataforma para Linux, boa biblioteca de rede e uma base adequada para compartilhar código entre API, gateway e agente.

## Início rápido com Docker

### Pré-requisitos

- Docker Desktop no Windows/macOS ou Docker Engine no Linux;
- Docker Compose;
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

Preencha `POSTGRES_PASSWORD` e `JWT_KEY` com valores aleatórios próprios. O Compose não inicia se algum deles estiver vazio. Para gerar uma chave no PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

### 3. Inicie os serviços

```bash
docker compose up --build
```

Acesse:

- aplicação: <http://localhost:8005>
- API: <http://localhost:8080>
- saúde da API: <http://localhost:8080/health>
- OpenAPI em desenvolvimento: <http://localhost:8080/openapi/v1.json>

Na primeira execução, o PostgreSQL e as tabelas são criados automaticamente.
O primeiro usuário pode se cadastrar. Depois disso, novos cadastros ficam fechados por padrão. Para permitir mais usuários, defina `ALLOW_REGISTRATION=true` temporariamente no `.env` e reinicie a API.

Para executar em segundo plano:

```bash
docker compose up --build -d
```

Para encerrar sem apagar o banco:

```bash
docker compose down
```

Para apagar também os dados locais, execute conscientemente:

```bash
docker compose down --volumes
```

## Primeiro teste de Wake-on-LAN

Antes do teste, habilite Wake-on-LAN na BIOS/UEFI e nas propriedades da placa de rede do Windows.

1. Crie uma conta no Remote Wake.
2. Clique em **Nova máquina**.
3. Informe o MAC da placa Ethernet, por exemplo `AA:BB:CC:DD:EE:FF`.
4. Para rede local, use o broadcast da rede, por exemplo `192.168.1.255`.
5. Mantenha a porta `9`.
6. Salve e clique em **Ligar**.

### Observação importante sobre Docker

Broadcast UDP saindo de um contêiner pode não alcançar a rede física no Docker Desktop. No Linux, o perfil `gateway-linux` usa a rede do host. No Windows/macOS, execute o worker do gateway diretamente no host ou em outro dispositivo sempre ligado na LAN. Veja [o guia remoto](docs/REMOTE_SETUP.md).

## Desenvolvimento sem Docker

### API

Requer o SDK .NET 10 e PostgreSQL:

```bash
dotnet run --project src/api/RemoteWake.Api.csproj
```

As configurações podem ser fornecidas por `appsettings.Development.json`, variáveis de ambiente ou user secrets. Nunca faça commit de credenciais reais.

### Interface web

Requer Node.js 22 ou superior:

```bash
cd src/web
npm install
npm run dev
```

O Vite encaminha `/api` para `http://localhost:8080` durante o desenvolvimento.

## Estrutura do repositório

```text
.
├── src/
│   ├── api/        # API, autenticação, persistência e Magic Packet
│   ├── web/        # PWA em React e TypeScript
│   ├── gateway/    # orientação do gateway residencial
│   ├── agent/      # orientação do agente Windows/Linux
│   └── worker/     # executável compartilhado dos dois modos
├── docs/           # arquitetura, segurança e guias
├── compose.yaml
├── .env.example
└── README.md
```

## API atual

| Método | Rota | Autenticação | Descrição |
|---|---|---|---|
| `POST` | `/api/auth/register` | Não | Cria usuário e retorna JWT |
| `POST` | `/api/auth/login` | Não | Autentica o usuário |
| `GET` | `/api/machines` | Sim | Lista as máquinas do usuário |
| `POST` | `/api/machines` | Sim | Cadastra uma máquina |
| `PUT` | `/api/machines/{id}` | Sim | Atualiza uma máquina |
| `DELETE` | `/api/machines/{id}` | Sim | Remove uma máquina |
| `POST` | `/api/machines/{id}/wake` | Sim | Envia o Magic Packet |
| `POST` | `/api/machines/{id}/agent-key` | Sim | Obtém a chave do agente da própria máquina |
| `DELETE` | `/api/machines/{id}/agent-key` | Sim | Revoga a chave atual do agente |
| `GET` | `/api/activity` | Sim | Últimas 100 tentativas do proprietário |
| `POST` | `/api/machines/{id}/actions` | Sim | Pede desligamento ou reinicialização ao agente |
| `GET` | `/health` | Não | Verifica se a API responde (não verifica o banco) |

## Serviços e testes

Veja [instalação Windows/systemd](docs/SERVICE_INSTALL.md). O painel permite editar máquinas, consultar tentativas e revogar a chave de um agente.

Com Docker em execução, rode `./scripts/Test-Integration.ps1` no PowerShell. A suíte cria banco, API, gateway e agente descartáveis; testa isolamento entre usuários, edição, histórico, revogação e upgrade do esquema. O gateway envia apenas para loopback, e o agente simula desligamento/reinício. Nenhum PC é desligado. Os recursos de teste são removidos ao final.

## Segurança

O Remote Wake controla máquinas e deve ser tratado como software sensível.

- Não exponha a instalação de desenvolvimento diretamente à internet.
- Use HTTPS por meio de proxy reverso em produção.
- Troque todas as senhas e a chave JWT do `.env`.
- Restrinja cadastro público antes de hospedar para terceiros.
- Não use uma conta administrativa do sistema para tarefas desnecessárias.
- O agente aceita somente as ações `shutdown` e `restart`; shell arbitrário não é permitido.
- Magic Packets não possuem autenticação; a segurança deve estar no acesso ao sistema e à rede.

Tokens são mantidos em `localStorage` no MVP. Antes de uma versão de produção pública, a autenticação será migrada para cookies `HttpOnly`, com refresh token, proteção CSRF, limitação de tentativas e confirmação de e-mail. Consulte [SECURITY.md](SECURITY.md).

## Roadmap

- [x] Estrutura inicial e Docker Compose
- [x] Cadastro e login
- [x] CRUD básico de máquinas
- [x] Geração e envio do Magic Packet
- [x] Histórico interno das tentativas de wake
- [ ] Testes automatizados da API e do pacote WOL
- [ ] Edição de máquinas pela PWA
- [x] Gateway com conexão de saída autenticada para LAN/Tailscale (chave manual)
- [x] Worker compatível com Windows Service e systemd (instalação manual)
- [x] Status online/offline por contato recente
- [x] Desligar e reiniciar com confirmação
- [ ] Suspender e hibernar
- [ ] Ações e scripts locais previamente autorizados
- [ ] Cookies seguros, refresh tokens, 2FA e recuperação de conta
- [ ] Auditoria visível no painel
- [ ] Internacionalização português/inglês
- [ ] Imagens publicadas em registro de contêineres

Veja detalhes em [docs/ROADMAP.md](docs/ROADMAP.md).

## Contribuindo

Issues, sugestões e pull requests são bem-vindos. Leia [CONTRIBUTING.md](CONTRIBUTING.md) antes de contribuir e não publique vulnerabilidades em issues públicas.

## Licença

Distribuído sob a licença MIT. Consulte [LICENSE](LICENSE).
