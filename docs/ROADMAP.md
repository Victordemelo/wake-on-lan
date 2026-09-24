# Roadmap

## Fase 1 — Wake-on-LAN local

- [x] autenticação básica;
- [x] cadastro e edição de máquinas;
- [x] biblioteca de Magic Packet compartilhada entre API e worker;
- [x] interface PWA com ícones para Android e iOS e cache offline da interface;
- [x] histórico de tentativas;
- [x] documentação do teste no Windows.

## Fase 2 — Gateway remoto

- [x] chave manual vinculada ao proprietário;
- [x] conexão de saída por long polling;
- [x] retransmissão de broadcast na LAN com destinos permitidos;
- [x] instruções para Tailscale, Docker Linux e executável Windows;
- [x] demonstração sem hardware (perfil `demo` do Compose);
- [x] revogação individual de chaves dos agentes (gateway ainda usa chave manual única);
- [ ] pareamento por código e múltiplos gateways (veja o desenho abaixo);
- [ ] teste real na rede residencial.

## Fase 3 — Agente Windows/Linux

- [x] chave de agente por máquina;
- [x] presença online/offline por contato recente;
- [x] desligar e reiniciar com confirmação e timeout;
- [x] suspender e hibernar (executados depois de responder à API);
- [x] confirmação de que a máquina ligou (agente conectado até 10 minutos após o Ligar);
- [x] worker compatível com Windows Service (com recuperação automática) e systemd;
- [x] histórico persistente de comandos visível no painel, mantido após remover a máquina;
- [ ] teste real dos serviços em Windows/Linux;
- [ ] ações locais cadastradas previamente.

## Fase 4 — Produção

- [x] sessões no servidor com cookie `HttpOnly`/`SameSite=Strict`, proteção CSRF e revogação;
- [x] verificação em duas etapas (TOTP), códigos de recuperação e recuperação pela linha de comando;
- [x] código de configuração para a primeira conta;
- [x] migrações do EF Core com linha de base para instalações antigas;
- [x] rate limiting por IP real (login) e por conta (comandos);
- [x] eventos de segurança e histórico com IP de origem;
- [x] HTTPS automático com Caddy, cabeçalhos de segurança e health check com banco;
- [x] testes automatizados e CI no GitHub Actions;
- [x] imagens multi-arquitetura (amd64/arm64) publicadas por tag;
- [ ] observabilidade (métricas e alertas);
- [ ] releases assinadas do worker;
- [ ] atualização segura de agentes;
- [ ] papel de administrador na interface;
- [ ] internacionalização português/inglês.

## Próximo passo: pareamento de gateways

Hoje uma instalação aceita um gateway, configurado por `GATEWAY_KEY` e restrito à conta de `GATEWAY_OWNER_EMAIL`. Desenho proposto para vários gateways por usuário:

1. A conta cria um gateway no painel e recebe um código de pareamento curto, válido por 10 minutos.
2. O worker inicia com `REMOTE_WAKE_PAIRING_CODE`, troca o código por uma chave própria (`POST /api/gateway/pair`) e a grava em um arquivo protegido (no Windows, em `%ProgramData%` com ACL para LocalService).
3. A API guarda apenas o hash da chave; cada gateway tem fila, status online e revogação próprios.
4. Cada máquina do método gateway escolhe qual gateway a atende; máquinas sem gateway definido continuam usando a chave de `GATEWAY_KEY`, preservando instalações existentes.
