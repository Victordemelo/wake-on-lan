# Roadmap

## Fase 1 — Wake-on-LAN local

- autenticação básica;
- cadastro de máquinas;
- biblioteca de Magic Packet;
- interface PWA;
- histórico de tentativas;
- documentação do teste no Windows.

## Fase 2 — Gateway remoto

- identidade e pareamento de gateway;
- conexão de saída via WebSocket/SignalR;
- associação entre máquina e gateway;
- retransmissão de broadcast na LAN;
- instruções para Tailscale e Docker;
- rotação e revogação de chaves.

## Fase 3 — Agente Windows/Linux

- instalador e pareamento;
- presença online/offline;
- desligar, reiniciar, suspender e hibernar;
- comandos definidos localmente por ID;
- confirmação, timeout e auditoria;
- Windows Service e unidade systemd.

## Fase 4 — Produção

- cookies HttpOnly e refresh tokens rotativos;
- 2FA e recuperação de conta;
- migrações versionadas do banco;
- rate limiting e proteção contra abuso;
- observabilidade;
- releases assinadas;
- atualização segura de agentes;
- imagens multi-arquitetura.

