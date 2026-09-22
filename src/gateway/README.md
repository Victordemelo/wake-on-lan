# Gateway

Componente para permanecer ligado dentro da rede local, receber pedidos autenticados da API e enviar Magic Packets por broadcast.

O gateway usa `src/worker` em modo `gateway`. Ele abre uma conexão de saída com a API, recebe somente pedidos autenticados e envia Magic Packets para destinos configurados em `REMOTE_WAKE_ALLOWED_BROADCASTS`. Consulte [o guia de instalação](../../docs/REMOTE_SETUP.md).

