# Instalar o worker como serviço

Primeiro valide a URL, o gateway e o agente com o [guia remoto](REMOTE_SETUP.md).
Os arquivos abaixo são um ponto de instalação manual; não são instaladores assinados.
Mantenha a API e o gateway em um equipamento que continuará ligado.

## Windows x64

Em uma máquina com SDK .NET 10, publique um executável independente do runtime:

```powershell
dotnet publish src/worker/RemoteWake.Worker.csproj -c Release -r win-x64 --self-contained true -o publish/windows
Copy-Item deploy/worker.example.json worker.json
```

Edite `worker.json` com a URL da API (inclua `/` final), ID e chave do painel.
O exemplo mantém `REMOTE_WAKE_DRY_RUN=true` e ações reais desabilitadas.
Para um gateway, altere o modo para `gateway`, use a chave do gateway e informe
`REMOTE_WAKE_ALLOWED_BROADCASTS`; o ID da máquina não é usado nesse modo.
Não coloque chaves em commits ou mensagens públicas.

No equipamento de destino, abra PowerShell como administrador e execute:

```powershell
.\scripts\Install-Worker.ps1 -PublishedDirectory .\publish\windows -SettingsPath .\worker.json
Get-Service RemoteWakeAgent
```

O instalador recusa sobrepor serviços/diretórios existentes. Ele copia o binário
e a configuração para `C:\Program Files\RemoteWake\agent` (ou `gateway`),
restringe escrita a Administradores/SYSTEM e inicia o serviço automaticamente.
O gateway usa LocalService; o agente usa SYSTEM para agendar desligamentos.
Proteja ou remova a cópia original da configuração depois de instalar.
Não afrouxe a política de execução da organização para executar este script.

Valide **Reiniciar** e **Desligar** no painel: a resposta precisa dizer Simulação.
Somente depois, edite o `worker.json` instalado, definindo dry-run `false` e
power-actions-enabled `true`, e reinicie o serviço. Isso habilita comandos reais.
Logs ficam no Visualizador de Eventos do Windows (Aplicativo).

Para atualizar, pare o serviço, faça backup da configuração e substitua somente
os arquivos publicados, preservando `worker.json` e suas permissões; reinicie.
Para desinstalar, pare o serviço e use `sc.exe delete RemoteWakeAgent` (ou
`RemoteWakeGateway`). Os arquivos permanecem para recuperação/remoção manual.

## Linux com systemd

Publique para a arquitetura do destino (`linux-x64` ou `linux-arm64`):

```bash
dotnet publish src/worker/RemoteWake.Worker.csproj -c Release -r linux-x64 --self-contained true -o publish/linux
sudo install -d -m 755 /opt/remote-wake
sudo cp -r publish/linux/. /opt/remote-wake/
sudo chown -R root:root /opt/remote-wake
sudo chmod -R go-w /opt/remote-wake
sudo chmod 755 /opt/remote-wake/RemoteWake.Worker
sudo install -d -m 700 /etc/remote-wake
sudo install -m 600 deploy/worker.example.json /etc/remote-wake/worker.json
sudoedit /etc/remote-wake/worker.json
sudo install -m 644 deploy/remote-wake-agent.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now remote-wake-agent
sudo journalctl -u remote-wake-agent -n 50
```

Configure os mesmos campos do Windows e teste inicialmente em dry-run. O serviço
do agente usa root para chamar `shutdown`; os binários e configuração não podem
ser graváveis por usuários comuns. Para gateway Linux, prefira o perfil Docker
documentado no guia remoto, que não precisa de privilégios de desligamento.

Após revogar uma chave no painel, o agente encerra ao receber HTTP 401.
Copie a nova chave para a configuração protegida e reinicie o serviço.
Variáveis de ambiente sobrescrevem valores do arquivo JSON.

## Critérios para considerar a instalação concluída

- API e gateway continuam acessíveis quando o PC controlado está desligado.
- Worker volta após reiniciar o equipamento onde foi instalado.
- Gateway aparece online; pacote chega ao PC pela Ethernet.
- PC liga após desligamento e suspensão, conforme suporte do hardware.
- Desligamento/reinício reais funcionam depois de salvar os trabalhos abertos.
- Teste pelo celular em outra rede via HTTPS/tailnet, sem expor a API de desenvolvimento.

Esses testes dependem do hardware/rede de destino e não são substituídos por
testes em contêiner. Mantenha relógios dos hosts sincronizados: comandos expiram
em 20 segundos, evitando execução atrasada.
