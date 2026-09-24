# Política de segurança

## Relatando uma vulnerabilidade

Não abra uma issue pública contendo detalhes exploráveis. Use a opção **Security > Report a vulnerability** do repositório GitHub quando ela estiver habilitada. Se esse canal ainda não estiver disponível, abra uma issue sem detalhes sensíveis solicitando um contato privado.

Inclua, quando possível:

- versão ou commit afetado;
- impacto observado;
- passos mínimos para reprodução;
- sugestão de correção;
- se a vulnerabilidade já é pública.

## Escopo sensível

Áreas de atenção especial incluem autenticação e sessões, verificação em duas etapas, autorização entre usuários, armazenamento de chaves, gateway, agente privilegiado e execução de ações locais.

## Controles implementados

- Sessões no servidor com token aleatório em cookie `HttpOnly`/`SameSite=Strict` (`Secure` e `__Host-` sob HTTPS); o banco guarda só o hash do token.
- Proteção CSRF por cabeçalho obrigatório nas requisições que alteram estado.
- Verificação em duas etapas (TOTP) com bloqueio de reutilização e códigos de recuperação de uso único.
- Código de configuração para a primeira conta e cadastro fechado por padrão.
- Limites de tentativas por IP real (login e duas etapas) e por conta (comandos de energia).
- Eventos de segurança e histórico com IP de origem, visíveis ao titular da conta.
- Chaves de agente derivadas por HMAC e revogáveis; o agente aceita apenas ações de energia pré-definidas.
- Cabeçalhos CSP, HSTS, `X-Frame-Options`, `X-Content-Type-Options` e `Referrer-Policy`.

Antes de expor uma instalação à internet, siga o checklist de [docs/DEPLOY.md](docs/DEPLOY.md): HTTPS, segredos próprios, cadastro fechado e nenhuma porta da API publicada diretamente.
