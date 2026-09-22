# Remote Wake

Plataforma open source e self-hosted para ligar e, futuramente, administrar computadores remotamente. O projeto combina Wake-on-LAN, uma PWA instalável, uma API autenticada e componentes locais para funcionar dentro e fora de casa.

> O projeto está em desenvolvimento inicial. O MVP atual permite criar uma conta, cadastrar máquinas e enviar um Magic Packet. Desligamento, reinicialização, gateway Tailscale e agente do sistema estão no roadmap.

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
      └── gateway futuro ───────────► LAN/Tailscale ───────► PC desligado

Quando o PC estiver ligado:

Remote Wake API ◄── conexão segura ── Agente Windows/Linux
```

Wake-on-LAN liga ou desperta a máquina. Desligar, reiniciar ou executar ações exige o agente instalado no computador, pois uma máquina desligada não executa programas.

## Modos de ativação

| Modo | Funcionamento | Requisitos |
|---|---|---|
| Rede local/VPN | Envia o pacote ao broadcast da LAN | API/gateway dentro da LAN ou VPN que encaminhe o pacote |
| Wake-on-WAN | Envia UDP para um IP público ou DDNS | IP público, redirecionamento de porta e IP/MAC binding |
| Gateway Tailscale | Um nó Tailscale ligado envia o pacote dentro da LAN | Gateway sempre ligado na residência; implementação no roadmap |

O Tailscale instalado somente no computador desligado não consegue acordá-lo. É necessário outro nó ativo na residência, como roteador compatível, NAS, Raspberry Pi ou mini PC.

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

Broadcast UDP saindo de um contêiner pode não alcançar a rede física no Docker Desktop. No Linux, uma opção futura será executar o gateway com rede do host. No Windows/macOS e no modo Tailscale, o componente `gateway` será a forma recomendada de transmitir o Magic Packet pela LAN. O envio direto disponível no MVP é útil para Wake-on-WAN e ambientes onde o contêiner alcança o destino configurado.

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
│   ├── gateway/    # transmissor dentro da LAN (roadmap)
│   └── agent/      # serviço Windows/Linux (roadmap)
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
| `GET` | `/health` | Não | Verifica API e banco |

## Segurança

O Remote Wake controla máquinas e deve ser tratado como software sensível.

- Não exponha a instalação de desenvolvimento diretamente à internet.
- Use HTTPS por meio de proxy reverso em produção.
- Troque todas as senhas e a chave JWT do `.env`.
- Restrinja cadastro público antes de hospedar para terceiros.
- Não use uma conta administrativa do sistema para tarefas desnecessárias.
- O futuro agente aceitará ações cadastradas, e não shell arbitrário por padrão.
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
- [ ] Gateway autenticado para LAN/Tailscale
- [ ] Agente Windows executado como Windows Service
- [ ] Agente Linux executado via systemd
- [ ] Status online/offline em tempo real
- [ ] Desligar, reiniciar, suspender e hibernar
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
