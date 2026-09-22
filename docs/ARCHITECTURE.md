# Arquitetura

## Objetivos

O Remote Wake separa o plano de controle do acesso à rede local. Essa separação permite hospedar a interface em qualquer lugar sem presumir que broadcasts UDP atravessem roteadores, VPNs ou redes Tailscale.

## Componentes

### Web

PWA responsável por autenticação, cadastro de máquinas e envio de ações. Comunica-se somente com a API por HTTPS.

### API

Plano de controle. Armazena usuários, máquinas, configuração de ativação e tentativas de wake. Envia Magic Packets diretamente nos modos local e Wake-on-WAN ou despacha o pedido ao gateway residencial.

### Gateway

Processo leve dentro da rede residencial. Faz long polling autenticado de saída para a API, recebe pedidos autorizados e envia o broadcast UDP na LAN. A primeira versão usa uma chave manual e permite apenas destinos explicitamente configurados.

### Agent

Serviço instalado na máquina controlada. Quando o sistema está ligado, faz long polling autenticado de saída e executa somente desligamento ou reinicialização.

## Fluxos

### Wake local ou Wake-on-WAN no MVP

```text
Usuário → PWA → POST /machines/{id}/wake → API → UDP → destino configurado
```

### Wake via gateway

```text
Usuário → PWA → API → canal autenticado → Gateway → UDP broadcast → NIC
```

### Comando em máquina ligada

```text
Usuário → PWA → API → long polling autenticado → Agent → ação permitida
                                      └──── resultado + log ────┘
```

## Decisões iniciais

- Monorepo para manter contratos e documentação próximos.
- .NET LTS para API, gateway e agent.
- React/TypeScript para uma PWA simples de distribuir.
- PostgreSQL para consistência e evolução multiusuário.
- Conexões iniciadas por gateway/agent para evitar portas abertas na residência.
- Allowlist de ações em vez de shell remoto livre.

