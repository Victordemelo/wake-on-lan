# Colocar em produção

O Compose padrão (`compose.yaml`) é para desenvolvimento: liga as portas apenas
em `127.0.0.1` e usa HTTP. Para usar o Remote Wake fora de casa, escolha uma das
topologias abaixo. Em ambas, **o gateway precisa ficar ligado dentro da sua
rede local**, em um equipamento diferente do PC que será desligado.

## Opção A — Tudo em casa, acesso pelo Tailscale

Recomendada para uso pessoal: não abre portas no roteador e funciona atrás de
CGNAT. A API, a interface e o gateway ficam em um equipamento sempre ligado da
residência (mini PC, NAS, Raspberry Pi ou um PC que não será desligado).

1. Instale o [Tailscale](https://tailscale.com/download) nesse equipamento e no
   celular, na mesma tailnet.
2. No `.env`, defina segredos próprios, `ALLOW_REGISTRATION=false` e
   `ASPNETCORE_ENVIRONMENT=Production`.
3. Suba os serviços: `docker compose up -d --build`.
4. Publique a interface na tailnet com HTTPS:

   ```bash
   tailscale serve --bg 8005
   ```

   O endereço fica `https://<nome-do-equipamento>.<sua-tailnet>.ts.net`.
   Use a porta definida em `WEB_PORT`, se tiver alterado.
5. Inicie o gateway no mesmo equipamento apontando para
   `REMOTE_WAKE_API_URL=http://127.0.0.1:8080/` (veja
   [o guia remoto](REMOTE_SETUP.md) e [a instalação como serviço](SERVICE_INSTALL.md)).

Neste modo todas as requisições chegam à API pelo próprio equipamento, então o
limite de tentativas de login é compartilhado pelos seus dispositivos da
tailnet. Isso é aceitável porque apenas eles alcançam a instalação.

## Opção B — Servidor com domínio público

A interface e a API ficam em um servidor com IP público (VPS) e HTTPS
automático via [Caddy](https://caddyserver.com/). O gateway continua em casa e
faz apenas conexões de saída para o servidor, inclusive atrás de CGNAT.

1. Aponte um registro DNS (`A`/`AAAA`) do seu domínio para o servidor e libere
   as portas 80 e 443 no firewall.
2. No `.env` do servidor, defina segredos próprios, `ALLOW_REGISTRATION=false`
   e `DOMAIN=remotewake.seudominio.com`.
3. Suba em modo produção:

   ```bash
   docker compose -f compose.yaml -f compose.prod.yaml up -d --build
   ```

   O override `compose.prod.yaml` coloca a API em `Production`, remove as portas
   da API e da interface e publica somente o Caddy (80/443). O certificado é
   emitido automaticamente pelo Let's Encrypt. Requer Docker Compose 2.24+.
4. Crie sua conta logo em seguida, antes de divulgar o endereço.
5. Em casa, configure o gateway com `REMOTE_WAKE_API_URL=https://remotewake.seudominio.com/`.

O Caddy envia a API diretamente (sem passar pelo nginx), descarta
`X-Forwarded-For` enviado por clientes e acrescenta HSTS e os demais cabeçalhos
de segurança. A API só aceita o IP/protocolo informado por proxies nas redes
listadas em `ForwardedHeaders__TrustedNetworks__*` (`compose.yaml`). Se colocar
outro proxy (por exemplo, Cloudflare) na frente do Caddy, configure
`trusted_proxies` no `deploy/Caddyfile`.

Para testar o modo produção localmente sem domínio, use `DOMAIN=localhost` e
portas alternativas; o Caddy emite um certificado da própria CA interna
(o navegador exibirá um alerta):

```bash
DOMAIN=localhost HTTP_PORT=8480 HTTPS_PORT=8443 docker compose -p remote-wake-prod -f compose.yaml -f compose.prod.yaml up -d --build
```

## Gateway no Windows

Publique o worker como executável independente (veja
[SERVICE_INSTALL.md](SERVICE_INSTALL.md)) e **execute-o a partir de uma pasta do
disco do Windows** (por exemplo `C:\RemoteWake`). Iniciado de dentro do WSL
(`\\wsl.localhost\...`), o executável de arquivo único não chega a se conectar.

## Checklist antes de expor

- Segredos longos e diferentes em `POSTGRES_PASSWORD`, `JWT_KEY`, `GATEWAY_KEY`
  e `AGENT_MASTER_KEY`; `.env` com permissão restrita (`chmod 600`) e fora do Git.
- `ALLOW_REGISTRATION=false`.
- Nenhuma porta da API publicada diretamente na internet.
- `curl -I https://seu-dominio/` mostra `Strict-Transport-Security` e
  `Content-Security-Policy`; `https://seu-dominio/health` responde `Healthy`
  (a verificação inclui o banco).
- Backup periódico do banco:

  ```bash
  docker compose exec database pg_dump -U remotewake remotewake > backup.sql
  ```

- Atualização: `git pull` e o mesmo `docker compose ... up -d --build`. As
  alterações de esquema são aplicadas automaticamente na inicialização da API.
