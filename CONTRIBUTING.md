# Como contribuir

Obrigado por considerar uma contribuição ao Remote Wake.

## Fluxo recomendado

1. Procure uma issue existente ou crie uma descrevendo a proposta.
2. Crie uma branch a partir de `main`: `feat/nome-curto` ou `fix/nome-curto`.
3. Mantenha cada pull request focado em uma única mudança.
4. Adicione ou atualize testes e documentação.
5. Confirme que `docker compose build` e os testes passam.
6. Abra o pull request explicando motivação, implementação e forma de testar.

## Commits

Preferimos mensagens objetivas no padrão Conventional Commits:

```text
feat: adiciona cadastro de gateways
fix: normaliza endereços MAC com hífen
docs: explica configuração do EX511
```

## Qualidade e segurança

- Nunca inclua senhas, tokens, chaves ou endereços pessoais.
- Valide todos os dados recebidos pela API.
- Autorize cada recurso pelo usuário proprietário.
- Evite execução arbitrária de shell.
- Para vulnerabilidades, siga o processo privado descrito em `SECURITY.md`.

