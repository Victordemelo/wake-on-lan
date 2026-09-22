# Ligar, desligar e reiniciar fora de casa

Esta versão usa dois processos locais. O **gateway** fica ligado na residência e envia o Magic Packet pela LAN. O **agente** roda no próprio PC e aceita apenas `shutdown` e `restart`. Ambos fazem conexões HTTP de saída para a API; não abra portas de entrada no PC.

```text
Celular/PWA → API acessível por HTTPS ou Tailscale
                 ├─ gateway na residência → UDP broadcast → PC desligado
                 └─ agente no PC ligado → desligar/reiniciar
```

O Tailscale é um meio de alcançar a API. Instalá-lo somente no PC que será desligado não basta para acordá-lo: **a API e o gateway precisam continuar ligados em outro dispositivo**. Eles podem rodar no mesmo mini PC, NAS ou Raspberry Pi da residência. O gateway também pode alcançar uma API pública por HTTPS, inclusive quando a casa está atrás de CGNAT. Uma VPN do roteador apenas fornece transporte; a PWA ainda precisa de um processo capaz de enviar UDP dentro da LAN.

## 1. API

No `.env` da instalação da API, configure `GATEWAY_KEY`, `GATEWAY_OWNER_EMAIL` e `AGENT_MASTER_KEY`. Use valores aleatórios longos e diferentes para as duas chaves. `GATEWAY_OWNER_EMAIL` deve ser o e-mail da conta proprietária das máquinas que usarão o gateway. Reinicie a API após alterar o `.env`.

Exemplo para gerar uma chave no PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

O gateway só atende máquinas desse proprietário. Uma instalação suporta um gateway residencial nesta versão. A chave do agente é diferente para cada máquina e pode ser exibida apenas pela conta proprietária no painel. Alterar `AGENT_MASTER_KEY` invalida todas as chaves dos agentes; apagar uma máquina invalida a chave dela.

Antes de usar pela internet, coloque a API atrás de HTTPS ou disponibilize a API somente na sua tailnet. O Compose fornecido é para desenvolvimento local e liga as portas `8005` e `8080` apenas a `127.0.0.1`; configure um proxy seguro para acesso externo.

## 2. Gateway na residência

Cadastre a máquina escolhendo **Gateway residencial / Tailscale**. Em **Destino**, informe o endereço de broadcast da LAN, por exemplo `192.168.1.255`. Informe o MAC da placa Ethernet do PC.

O gateway precisa de:

- `REMOTE_WAKE_MODE=gateway`
- `REMOTE_WAKE_API_URL=https://sua-api.example/` ou URL da API na tailnet
- `REMOTE_WAKE_KEY` igual ao `GATEWAY_KEY` da API
- `REMOTE_WAKE_ALLOWED_BROADCASTS=192.168.1.255` (separe múltiplos destinos por vírgula)

No Linux, com o gateway na mesma máquina do Compose da API, configure essas variáveis no `.env` e inicie:

```bash
docker compose --profile gateway-linux up --build -d
```

O contêiner do gateway usa `network_mode: host` no Linux para alcançar o broadcast físico. No Docker Desktop para Windows/macOS, essa rede pode não alcançar o broadcast da LAN; execute o worker diretamente no host ou em um Linux/NAS/Raspberry Pi da residência:

```bash
dotnet run --project src/worker/RemoteWake.Worker.csproj
```

Defina as quatro variáveis acima no ambiente do processo antes de iniciar. O gateway deve ficar ligado continuamente. O painel mostra **Gateway online** após o primeiro contato, que pode levar alguns segundos. Se estiver offline, o botão Ligar retorna um erro claro.

Para testar manualmente em um Windows que permaneça ligado na residência, abra o PowerShell na pasta do projeto:

```powershell
$env:REMOTE_WAKE_MODE='gateway'
$env:REMOTE_WAKE_API_URL='http://localhost:8080/' # troque pela URL segura da API se estiver em outro host
$env:REMOTE_WAKE_KEY='<GATEWAY_KEY do .env da API>'
$env:REMOTE_WAKE_ALLOWED_BROADCASTS='192.168.1.255' # troque pelo broadcast da sua LAN
dotnet run --project src/worker/RemoteWake.Worker.csproj
```

Isso exige o SDK .NET 10. Quando o teste funcionar, mantenha o worker ativo como serviço no equipamento que não será desligado.

## 3. Agente no PC controlado

No painel, abra a opção de chave da máquina e copie o ID e a chave do agente. No PC controlado, configure:

- `REMOTE_WAKE_MODE=agent`
- `REMOTE_WAKE_API_URL=https://sua-api.example/` ou URL da API na tailnet
- `REMOTE_WAKE_MACHINE_ID=<ID mostrado no painel>`
- `REMOTE_WAKE_KEY=<chave mostrada no painel>`
- `REMOTE_WAKE_POWER_ACTIONS_ENABLED=true`

Publique e execute o worker com o SDK .NET 10:

```bash
dotnet publish src/worker/RemoteWake.Worker.csproj -c Release -o ./publish/agent
```

Para um primeiro teste seguro, use `REMOTE_WAKE_DRY_RUN=true`: o agente confirma a ação sem desligar o PC. Depois, remova essa variável e habilite `REMOTE_WAKE_POWER_ACTIONS_ENABLED=true`.

No PowerShell do PC controlado, um teste em primeiro plano fica assim:

```powershell
$env:REMOTE_WAKE_MODE='agent'
$env:REMOTE_WAKE_API_URL='http://localhost:8080/' # troque pela URL segura da API
$env:REMOTE_WAKE_MACHINE_ID='<ID da máquina mostrado no painel>'
$env:REMOTE_WAKE_KEY='<chave mostrada no painel>'
$env:REMOTE_WAKE_DRY_RUN='true'
dotnet run --project src/worker/RemoteWake.Worker.csproj
```

Clique em **Reiniciar** no painel: a resposta deve indicar **Simulação** e o PC deve continuar ligado.

No Windows, instale o executável publicado como Windows Service com uma conta autorizada a desligar a máquina. As variáveis do agente precisam estar disponíveis ao processo do serviço. No Linux, execute o worker via systemd com um `EnvironmentFile` protegido (`chmod 600`) e uma conta autorizada a executar `shutdown`. O processo usa os comandos nativos do sistema: Windows `shutdown.exe /s|/r /t 30`; Linux `shutdown -h|-r +1`.

O painel libera **Desligar** e **Reiniciar** quando o agente aparece online. Há uma confirmação antes de cada ação. O agente não executa shell arbitrário nem scripts enviados pela API.

## Limites desta versão

- Os pedidos em andamento ficam na memória da API. Um reinício da API cancela esses pedidos; não há fila persistente.
- O resultado de Ligar confirma o envio do pacote, não que o PC efetivamente iniciou. Teste BIOS/UEFI, placa Ethernet e energia em suspensão/desligamento.
- O status online considera o contato do worker nos últimos 35 segundos, não uma inspeção direta do sistema operacional.
- Ações de energia são registradas no log da API, mas o histórico completo ainda não aparece no painel.
- O pareamento do gateway usa chave configurada manualmente. Códigos temporários, múltiplos gateways e rotação individual de chaves ficam para a próxima fase.
