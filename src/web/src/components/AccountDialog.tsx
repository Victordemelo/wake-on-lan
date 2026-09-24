import { FormEvent, ReactNode, useEffect, useState } from 'react'
import { Copy, Download, History, KeyRound, MonitorSmartphone, ShieldAlert, ShieldCheck, X } from 'lucide-react'
import { api, SecurityOverview, SessionInfo, TwoFactorSetup, User } from '../api'
import { describeAgent, formatDate } from '../format'

type Tab = 'password' | 'two-factor' | 'sessions' | 'events'
type Status = { ok: boolean; text: string } | null

const failure = (error: unknown, fallback: string): Status =>
  ({ ok: false, text: error instanceof Error ? error.message : fallback })

const tabs: { id: Tab; label: string; icon: ReactNode }[] = [
  { id: 'password', label: 'Senha', icon: <KeyRound size={15} /> },
  { id: 'two-factor', label: 'Duas etapas', icon: <ShieldCheck size={15} /> },
  { id: 'sessions', label: 'Sessões', icon: <MonitorSmartphone size={15} /> },
  { id: 'events', label: 'Atividade', icon: <History size={15} /> },
]

export default function AccountDialog({ user, onClose, onUserChange, onSignedOut }: {
  user: User
  onClose: () => void
  onUserChange: (user: User) => void
  onSignedOut: () => void
}) {
  const [tab, setTab] = useState<Tab>('password')

  return (
    <div className="dialog-backdrop" onMouseDown={onClose}>
      <div className="dialog account-dialog" role="dialog" aria-modal="true" aria-labelledby="account-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog-title">
          <div>
            <span className="dialog-icon"><ShieldCheck size={20} /></span>
            <div><h2 id="account-title">Minha conta</h2><p>{user.name} · {user.email}</p></div>
          </div>
          <button className="icon-button" onClick={onClose} aria-label="Fechar"><X size={20} /></button>
        </div>
        <div className="tabs" role="tablist" aria-label="Configurações da conta">
          {tabs.map((item) => (
            <button key={item.id} role="tab" aria-selected={tab === item.id} onClick={() => setTab(item.id)}>
              {item.icon} {item.label}
            </button>
          ))}
        </div>
        <div className="dialog-body account-body" role="tabpanel">
          {tab === 'password' && <PasswordTab />}
          {tab === 'two-factor' && <TwoFactorTab user={user} onUserChange={onUserChange} />}
          {tab === 'sessions' && <SessionsTab onSignedOut={onSignedOut} />}
          {tab === 'events' && <EventsTab />}
        </div>
      </div>
    </div>
  )
}

function StatusMessage({ status }: { status: Status }) {
  if (!status) return null
  return <div className={status.ok ? 'success' : 'error'} role={status.ok ? 'status' : 'alert'}>{status.text}</div>
}

function PasswordTab() {
  const [status, setStatus] = useState<Status>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = event.currentTarget
    const data = new FormData(form)
    const next = String(data.get('next'))
    if (next !== String(data.get('confirm'))) {
      setStatus({ ok: false, text: 'A confirmação não confere com a nova senha.' })
      return
    }
    setBusy(true)
    setStatus(null)
    try {
      const { revokedSessions } = await api.changePassword(String(data.get('current')), next)
      form.reset()
      setStatus({
        ok: true,
        text: revokedSessions === 0 ? 'Senha alterada.'
          : `Senha alterada. ${revokedSessions === 1 ? 'Outro dispositivo foi desconectado' : `${revokedSessions} outros dispositivos foram desconectados`}.`,
      })
    } catch (error) {
      setStatus(failure(error, 'Não foi possível alterar a senha.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <p className="account-hint">Ao trocar a senha, os outros dispositivos conectados à sua conta são desconectados.</p>
      <label>Senha atual<input name="current" type="password" autoComplete="current-password" required /></label>
      <label>Nova senha<input name="next" type="password" autoComplete="new-password" minLength={8} required placeholder="Mínimo de 8 caracteres" /></label>
      <label>Confirme a nova senha<input name="confirm" type="password" autoComplete="new-password" minLength={8} required /></label>
      <StatusMessage status={status} />
      <div className="account-actions"><button className="button primary" disabled={busy}>{busy ? 'Salvando...' : 'Alterar senha'}</button></div>
    </form>
  )
}

function TwoFactorTab({ user, onUserChange }: { user: User; onUserChange: (user: User) => void }) {
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null)
  const [qrCode, setQrCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)
  const [status, setStatus] = useState<Status>(null)
  const [busy, setBusy] = useState(false)

  const run = async (action: () => Promise<void>, fallback: string) => {
    setBusy(true)
    setStatus(null)
    try {
      await action()
    } catch (error) {
      setStatus(failure(error, fallback))
    } finally {
      setBusy(false)
    }
  }

  const start = () => run(async () => {
    const result = await api.startTwoFactor()
    setSetup(result)
    // Loaded on demand: only people configuring two-step verification need it.
    const QRCode = await import('qrcode')
    setQrCode(await QRCode.toDataURL(result.uri, { margin: 1, width: 220, errorCorrectionLevel: 'M' }))
  }, 'Não foi possível iniciar a configuração.')

  const enable = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    return run(async () => {
      const result = await api.enableTwoFactor(String(data.get('password')), String(data.get('code')))
      setSetup(null)
      setQrCode('')
      setRecoveryCodes(result.recoveryCodes)
      onUserChange({ ...user, twoFactorEnabled: true })
    }, 'Não foi possível ativar.')
  }

  const regenerate = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = event.currentTarget
    const password = String(new FormData(form).get('password'))
    return run(async () => {
      const result = await api.regenerateRecoveryCodes(password)
      form.reset()
      setRecoveryCodes(result.recoveryCodes)
    }, 'Não foi possível gerar novos códigos.')
  }

  const disable = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = event.currentTarget
    const data = new FormData(form)
    return run(async () => {
      await api.disableTwoFactor(String(data.get('password')), String(data.get('code')))
      form.reset()
      onUserChange({ ...user, twoFactorEnabled: false })
      setStatus({ ok: true, text: 'Verificação em duas etapas desativada.' })
    }, 'Não foi possível desativar.')
  }

  if (recoveryCodes) return <RecoveryCodes codes={recoveryCodes} onDone={() => setRecoveryCodes(null)} />

  if (setup) {
    return (
      <form onSubmit={enable}>
        <p className="account-hint">1. Escaneie o código com um aplicativo autenticador, como Google Authenticator, Microsoft Authenticator, Aegis ou 1Password.</p>
        <div className="qr-box">{qrCode ? <img src={qrCode} alt="Código QR para o aplicativo autenticador" width={220} height={220} /> : <span>Gerando código...</span>}</div>
        <p className="account-hint">Sem câmera? Digite esta chave no aplicativo:</p>
        <code className="secret-key">{setup.secret}</code>
        <label>2. Código de 6 dígitos mostrado no aplicativo
          <input name="code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}" maxLength={7} required placeholder="123456" />
        </label>
        <label>3. Sua senha, para confirmar<input name="password" type="password" autoComplete="current-password" required /></label>
        <StatusMessage status={status} />
        <div className="account-actions">
          <button type="button" className="button secondary" onClick={() => { setSetup(null); setQrCode('') }}>Cancelar</button>
          <button className="button primary" disabled={busy}>{busy ? 'Verificando...' : 'Ativar'}</button>
        </div>
      </form>
    )
  }

  if (user.twoFactorEnabled) {
    return (
      <>
        <div className="account-state on">
          <ShieldCheck size={22} />
          <div><strong>Ativa</strong><p>Além da senha, o login pede o código do aplicativo autenticador.</p></div>
        </div>
        <StatusMessage status={status} />
        <form onSubmit={regenerate}>
          <h3>Novos códigos de recuperação</h3>
          <p className="account-hint">Use se perdeu os códigos anteriores. Os antigos deixam de valer.</p>
          <label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>
          <div className="account-actions"><button className="button secondary" disabled={busy}>Gerar novos códigos</button></div>
        </form>
        <form onSubmit={disable}>
          <h3>Desativar</h3>
          <label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>
          <label>Código do aplicativo ou de recuperação<input name="code" autoComplete="one-time-code" required /></label>
          <div className="account-actions"><button className="button danger" disabled={busy}>Desativar duas etapas</button></div>
        </form>
      </>
    )
  }

  return (
    <>
      <div className="account-state off">
        <ShieldAlert size={22} />
        <div>
          <strong>Desativada</strong>
          <p>Com duas etapas, quem descobrir sua senha ainda precisa do seu celular para entrar. Recomendado para uma conta que liga e desliga computadores.</p>
        </div>
      </div>
      <StatusMessage status={status} />
      <div className="account-actions"><button className="button primary" onClick={() => void start()} disabled={busy}>{busy ? 'Preparando...' : 'Configurar'}</button></div>
    </>
  )
}

function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [copied, setCopied] = useState(false)
  const text = `Remote Wake - códigos de recuperação\nCada código funciona uma única vez.\n\n${codes.join('\n')}\n`

  const download = () => {
    const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'remote-wake-codigos-de-recuperacao.txt'
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <>
      <div className="account-state on">
        <ShieldCheck size={22} />
        <div><strong>Guarde estes códigos de recuperação</strong><p>Eles permitem entrar se você perder o celular. Cada um funciona uma vez e eles não serão exibidos de novo.</p></div>
      </div>
      <div className="recovery-grid">{codes.map((code) => <code key={code}>{code}</code>)}</div>
      <div className="account-actions">
        <button type="button" className="button secondary" onClick={() => void navigator.clipboard.writeText(text).then(() => setCopied(true))}>
          <Copy size={15} /> {copied ? 'Copiados' : 'Copiar'}
        </button>
        <button type="button" className="button secondary" onClick={download}><Download size={15} /> Baixar .txt</button>
        <button type="button" className="button primary" onClick={onDone}>Já guardei</button>
      </div>
    </>
  )
}

function SessionsTab({ onSignedOut }: { onSignedOut: () => void }) {
  const [sessions, setSessions] = useState<SessionInfo[] | null>(null)
  const [status, setStatus] = useState<Status>(null)
  const [version, setVersion] = useState(0)

  useEffect(() => {
    let active = true
    api.sessions().then(
      (items) => { if (active) setSessions(items) },
      (error) => { if (active) setStatus(failure(error, 'Não foi possível carregar as sessões.')) },
    )
    return () => { active = false }
  }, [version])

  const revoke = async (session: SessionInfo) => {
    try {
      await api.revokeSession(session.id)
      if (session.current) onSignedOut()
      else setVersion((value) => value + 1)
    } catch (error) {
      setStatus(failure(error, 'Não foi possível encerrar a sessão.'))
    }
  }

  const revokeOthers = async () => {
    try {
      const { revokedSessions } = await api.revokeOtherSessions()
      setStatus({ ok: true, text: revokedSessions === 1 ? '1 dispositivo foi desconectado.' : `${revokedSessions} dispositivos foram desconectados.` })
      setVersion((value) => value + 1)
    } catch (error) {
      setStatus(failure(error, 'Não foi possível desconectar os outros dispositivos.'))
    }
  }

  return (
    <>
      <p className="account-hint">Dispositivos com sessão ativa. Uma sessão expira após 7 dias sem uso ou 30 dias após o login.</p>
      <StatusMessage status={status} />
      {sessions === null ? <p className="account-hint">Carregando...</p> : (
        <div className="session-list">
          {sessions.map((session) => (
            <article key={session.id} className="session-row">
              <div>
                <strong>{describeAgent(session.userAgent)} {session.current && <span className="badge">ESTE DISPOSITIVO</span>}</strong>
                <small>{session.ipAddress ?? 'IP desconhecido'} · último acesso {formatDate(session.lastSeenAt)} · desde {formatDate(session.createdAt)}</small>
              </div>
              <button className="button secondary" onClick={() => void revoke(session)}>{session.current ? 'Sair' : 'Encerrar'}</button>
            </article>
          ))}
        </div>
      )}
      {sessions && sessions.length > 1 && (
        <div className="account-actions"><button className="button danger" onClick={() => void revokeOthers()}>Sair dos outros dispositivos</button></div>
      )}
    </>
  )
}

const eventLabels: Record<string, { label: string; alert?: boolean }> = {
  registered: { label: 'Conta criada' },
  login_succeeded: { label: 'Entrada na conta' },
  login_failed: { label: 'Tentativa com senha incorreta', alert: true },
  two_factor_failed: { label: 'Código de verificação incorreto', alert: true },
  recovery_code_used: { label: 'Código de recuperação usado', alert: true },
  logout: { label: 'Saída da conta' },
  session_revoked: { label: 'Sessão encerrada' },
  other_sessions_revoked: { label: 'Outros dispositivos desconectados' },
  password_changed: { label: 'Senha alterada' },
  password_reset: { label: 'Senha redefinida no servidor', alert: true },
  two_factor_enabled: { label: 'Duas etapas ativada' },
  two_factor_disabled: { label: 'Duas etapas desativada', alert: true },
  recovery_codes_regenerated: { label: 'Novos códigos de recuperação' },
}

function EventsTab() {
  const [overview, setOverview] = useState<SecurityOverview | null>(null)
  const [status, setStatus] = useState<Status>(null)

  useEffect(() => {
    let active = true
    api.securityOverview().then(
      (result) => { if (active) setOverview(result) },
      (error) => { if (active) setStatus(failure(error, 'Não foi possível carregar a atividade.')) },
    )
    return () => { active = false }
  }, [])

  return (
    <>
      <p className="account-hint">Eventos de segurança da sua conta. Se não reconhecer algum, troque a senha e ative as duas etapas.</p>
      <StatusMessage status={status} />
      {overview === null ? <p className="account-hint">Carregando...</p> : (
        <>
          {overview.failedCodes > 0 && (
            <div className="error" role="alert">
              {overview.failedCodes === 1 ? '1 código de verificação incorreto' : `${overview.failedCodes} códigos de verificação incorretos`} nos últimos 30 dias
              {overview.lastFailedCode && ` (último em ${formatDate(overview.lastFailedCode)})`}. Quem erra o código já acertou sua senha: troque-a.
            </div>
          )}
          <p className="account-hint">
            {overview.failedPasswords === 0 ? 'Nenhuma tentativa com senha incorreta nos últimos 30 dias.'
              : `${overview.failedPasswords === 1 ? '1 tentativa' : `${overview.failedPasswords} tentativas`} com senha incorreta nos últimos 30 dias`
                + (overview.lastFailedPassword ? ` (última em ${formatDate(overview.lastFailedPassword)}).` : '.')}
          </p>
          {overview.events.length === 0 ? <p className="account-hint">Nenhum evento registrado.</p> : (
            <div className="activity-list event-list">
              {overview.events.map((item) => {
                const meta = eventLabels[item.type] ?? { label: item.type }
                return (
                  <article key={item.id} className="activity-row">
                    <div>
                      <strong className={meta.alert ? 'activity-failure' : undefined}>{meta.label}</strong>
                      <p>{describeAgent(item.userAgent)}{item.ipAddress ? ` · ${item.ipAddress}` : ''}</p>
                    </div>
                    <time dateTime={item.createdAt}>{formatDate(item.createdAt)}</time>
                  </article>
                )
              })}
            </div>
          )}
        </>
      )}
    </>
  )
}
