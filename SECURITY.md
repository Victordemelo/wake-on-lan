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

Áreas de atenção especial incluem autenticação, autorização entre usuários, assinatura de comandos, armazenamento de chaves, gateway, agente privilegiado e execução de ações locais.

O projeto está em fase inicial e ainda não deve ser exposto diretamente à internet sem HTTPS, proxy reverso, configuração segura de segredos e restrição de cadastro.

