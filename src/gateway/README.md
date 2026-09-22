# Gateway

Componente planejado para permanecer dentro da rede local, receber pedidos autenticados da API e enviar Magic Packets por broadcast. Será o caminho recomendado para Tailscale, CGNAT e Docker Desktop.

O gateway deverá iniciar a conexão com a API, ter identidade própria, suportar revogação e nunca aceitar pacotes arbitrários sem autorização.

