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
- [x] revogação individual de chaves dos agentes (gateway ainda usa chave manual única);
- [ ] teste real na rede residencial.

## Fase 3 — Agente Windows/Linux

- [x] chave de agente por máquina;
- [x] presença online/offline por contato recente;
- [x] desligar e reiniciar com confirmação e timeout;
- [x] worker compatível com Windows Service e systemd;
- [x] script de instalação Windows e configuração systemd documentada;
- [ ] teste real dos serviços em Windows/Linux;
- [ ] suspender, hibernar e ações locais cadastradas;
- [x] histórico persistente de comandos visível no painel (removido junto com a máquina).

## Fase 4 — Produção

- cookies HttpOnly e refresh tokens rotativos;
- 2FA e recuperação de conta;
- evolução completa de migrações (upgrade aditivo v1 implementado);
- ampliar rate limiting (autenticação limitada a 20 requisições/minuto por IP; atrás do proxy o limite é compartilhado);
- observabilidade;
- releases assinadas;
- atualização segura de agentes;
- imagens multi-arquitetura.

