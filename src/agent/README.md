# Agent

Serviço planejado para Windows e Linux. Ele informará o estado da máquina e executará ações explicitamente autorizadas, como desligar, reiniciar, suspender e rodar tarefas locais cadastradas.

Shell remoto arbitrário ficará desabilitado por padrão. A implementação será separada do gateway porque o agente não consegue acordar a própria máquina enquanto ela está desligada.

