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

Suspender e hibernar agem imediatamente; o agente responde primeiro e executa a ação 5 segundos depois. Os pedidos ficam em memória apenas enquanto a requisição do usuário espera: um pedido nunca é executado depois de expirar (20 s) ou após um reinício da API.

### Confirmação de que a máquina ligou

```text
Ligar bem-sucedido → ... → agente conecta (estava offline) → histórico: "Ligou em N s"
```

A API registra a confirmação quando o agente da máquina passa de offline para online em até 10 minutos depois do pedido.

## Autenticação e sessões

- A sessão é um token aleatório de 256 bits num cookie `HttpOnly`, `SameSite=Strict` (e `Secure` com prefixo `__Host-` quando a requisição chega por HTTPS). O banco guarda apenas o hash SHA-256 do token: um vazamento do banco não permite forjar sessões.
- Cada sessão expira após 7 dias sem uso ou 30 dias após o login e pode ser encerrada individualmente, por "sair dos outros dispositivos" ou pela troca de senha.
- Requisições que alteram estado exigem o cabeçalho `X-Remote-Wake-Request`, que um navegador não envia entre origens sem uma permissão CORS que a API nunca concede.
- Verificação em duas etapas por TOTP (RFC 6238), com tolerância de um passo, bloqueio de reutilização do código e códigos de recuperação de uso único (apenas hashes são guardados).
- Gateway e agentes autenticam-se por chave no cabeçalho `X-Remote-Wake-Key`, nunca pelo cookie; as chaves dos agentes derivam de `AGENT_MASTER_KEY` (HMAC) e são revogadas por versão.
- A API confia no IP e no protocolo informados por proxies somente nas redes de `ForwardedHeaders__TrustedNetworks__*`; o nginx e o Caddy substituem o `X-Forwarded-For` enviado pelo cliente.

## Persistência

- Migrações do EF Core aplicadas na inicialização, sob um advisory lock do PostgreSQL.
- Bancos criados pela versão 0.1 (`EnsureCreated`) são reconhecidos e recebem a primeira migração como linha de base, sem perder dados.
- O histórico de ações pertence ao proprietário e guarda o nome da máquina, sobrevivendo à remoção dela.

## Código compartilhado e testes

- `src/shared` contém o Magic Packet, o contrato `RemoteJob` e o mapeamento das ações para comandos nativos, usados pela API e pelo worker.
- `tests/RemoteWake.Tests` cobre as regras isoladamente (relógio simulado no broker de pedidos) e a API real contra PostgreSQL, incluindo a atualização de bancos antigos a partir do esquema real da versão 0.1.
- `scripts/Test-Integration.ps1` sobe API, gateway e agente em contêineres e exercita o fluxo completo em simulação.

## Decisões iniciais

- Monorepo para manter contratos e documentação próximos.
- .NET LTS para API, gateway e agent.
- React/TypeScript para uma PWA simples de distribuir.
- PostgreSQL para consistência e evolução multiusuário.
- Conexões iniciadas por gateway/agent para evitar portas abertas na residência.
- Allowlist de ações em vez de shell remoto livre.

