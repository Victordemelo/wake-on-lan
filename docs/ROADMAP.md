# Roadmap

## Fase 1 — Wake-on-LAN local

- autenticação básica;
- cadastro de máquinas;
- biblioteca de Magic Packet;
- interface PWA;
- histórico de tentativas;
- documentação do teste no Windows.

## Fase 2 — Gateway remoto

- [x] chave manual vinculada ao proprietário;
- [x] conexão de saída por long polling;
- [x] retransmissão de broadcast na LAN com destinos permitidos;
- [x] instruções para Tailscale e Docker Linux;
- [ ] pareamento por código e múltiplos gateways;
- [ ] rotação e revogação individual de chaves;
- [ ] teste real na rede residencial.

## Fase 3 — Agente Windows/Linux

- [x] chave de agente por máquina;
- [x] presença online/offline por contato recente;
- [x] desligar e reiniciar com confirmação e timeout;
- [x] worker compatível com Windows Service e systemd;
- [ ] instaladores e teste real em Windows/Linux;
- [ ] suspender, hibernar e ações locais cadastradas;
- [ ] auditoria persistente e visível no painel.

## Fase 4 — Produção

- cookies HttpOnly e refresh tokens rotativos;
- 2FA e recuperação de conta;
- migrações versionadas do banco;
- rate limiting e proteção contra abuso;
- observabilidade;
- releases assinadas;
- atualização segura de agentes;
- imagens multi-arquitetura.

