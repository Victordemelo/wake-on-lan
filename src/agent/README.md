# Agent

Serviço para Windows e Linux que informa presença e executa apenas desligamento, reinicialização, suspensão e hibernação.

O agente usa `src/worker` em modo `agent`. Ele recebe somente `shutdown`, `restart`, `suspend` e `hibernate`, usando uma chave vinculada ao ID da máquina. Quando ele se conecta logo depois de um Ligar, a API registra que a máquina ligou. O PC deve estar ligado e conectado à API. Consulte [o guia de instalação](../../docs/REMOTE_SETUP.md). Shell remoto arbitrário não é aceito.

