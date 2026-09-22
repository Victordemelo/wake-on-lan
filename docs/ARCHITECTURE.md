# Arquitetura

## Objetivos

O Remote Wake separa o plano de controle do acesso à rede local. Essa separação permite hospedar a interface em qualquer lugar sem presumir que broadcasts UDP atravessem roteadores, VPNs ou redes Tailscale.

## Componentes

### Web

PWA responsável por autenticação, cadastro de máquinas e envio de ações. Comunica-se somente com a API por HTTPS.

### API

Plano de controle. Armazena usuários, máquinas, configuração de ativação e auditoria. No MVP, também consegue enviar Magic Packets diretamente. Futuramente encaminhará pedidos ao gateway apropriado.

### Gateway

Processo leve dentro da rede residencial. Mantém uma conexão autenticada de saída com a API, recebe pedidos autorizados e envia o broadcast UDP na LAN. Poderá ser executado em Docker, Linux, Windows, NAS ou nó Tailscale.

### Agent

Serviço instalado na máquina controlada. Quando o sistema está ligado, informa presença e executa ações previamente permitidas, como desligar, reiniciar, suspender ou reiniciar um serviço.

## Fluxos

### Wake local ou Wake-on-WAN no MVP

```text
Usuário → PWA → POST /machines/{id}/wake → API → UDP → destino configurado
```

### Wake via gateway planejado

```text
Usuário → PWA → API → canal autenticado → Gateway → UDP broadcast → NIC
```

### Comando em máquina ligada planejado

```text
Usuário → PWA → API → canal autenticado → Agent → ação permitida
                                      └──── resultado/auditoria ────┘
```

## Decisões iniciais

- Monorepo para manter contratos e documentação próximos.
- .NET LTS para API, gateway e agent.
- React/TypeScript para uma PWA simples de distribuir.
- PostgreSQL para consistência e evolução multiusuário.
- Conexões iniciadas por gateway/agent para evitar portas abertas na residência.
- Allowlist de ações em vez de shell remoto livre.

