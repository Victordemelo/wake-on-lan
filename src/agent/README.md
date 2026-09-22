# Agent

Serviço para Windows e Linux que informa presença e executa apenas desligamento e reinicialização nesta versão.

O agente usa `src/worker` em modo `agent`. Ele recebe somente `shutdown` e `restart`, usando uma chave vinculada ao ID da máquina. O PC deve estar ligado e conectado à API. Consulte [o guia de instalação](../../docs/REMOTE_SETUP.md). Shell remoto arbitrário não é aceito.

