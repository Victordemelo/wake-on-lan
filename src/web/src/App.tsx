import { FormEvent, useCallback, useEffect, useState } from 'react'
import {
  Activity, ArrowRight, Check, ChevronRight, Eye, EyeOff, LayoutDashboard,
  LogOut, Monitor, Network, Plus, Power, Radio, ScrollText, Settings,
  ShieldAlert, ShieldCheck, Trash2, UserRound, Wifi, WifiOff, X, RotateCcw, KeyRound, Pencil, Moon, Snowflake, TriangleAlert,
} from 'lucide-react'
import { api, ApiError, ActivityItem, GatewayStatus, Machine, MachineInput, PowerAction, RegistrationStatus, User, WakeMethod } from './api'
import AccountDialog from './components/AccountDialog'
import ProjectShowcase from './components/ui/ProjectShowcase'
import { formatDate } from './format'

const powerActions: { action: PowerAction; label: string; verb: string; icon: typeof Power; warning: string }[] = [
  { action: 'shutdown', label: 'Desligar', verb: 'desligar', icon: Power, warning: 'Salve o trabalho aberto nessa máquina antes de continuar.' },
  { action: 'restart', label: 'Reiniciar', verb: 'reiniciar', icon: RotateCcw, warning: 'Salve o trabalho aberto nessa máquina antes de continuar.' },
  { action: 'suspend', label: 'Suspender', verb: 'suspender', icon: Moon, warning: 'Para acordá-la depois, use Ligar; o Wake-on-LAN precisa estar ativo também na suspensão.' },
  { action: 'hibernate', label: 'Hibernar', verb: 'hibernar', icon: Snowflake, warning: 'A hibernação precisa estar habilitada no sistema da máquina.' },
]

const activityLabels: Record<string, string> = {
  wake: 'Ligar', online: 'Ligou', shutdown: 'Desligar', restart: 'Reiniciar', suspend: 'Suspender', hibernate: 'Hibernar',
}

const emptyMachine: MachineInput = {
  name: '', macAddress: '', hostname: '', broadcastAddress: '255.255.255.255',
  wolPort: 9, wakeMethod: 'LocalBroadcast',
}

function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <a className={`brand ${compact ? 'brand-compact' : ''}`} href="/" aria-label="Remote Wake">
      <span className="brand-symbol"><img src="/brand/remote-wake-mark.svg" alt="" /></span>
      {!compact && <span>Remote <strong>Wake</strong></span>}
    </a>
  )
}

type Toast = { text: string; error: boolean }

function App() {
  // undefined while checking the session cookie, null when signed out.
  const [user, setUser] = useState<User | null | undefined>(undefined)
  const [bootFailed, setBootFailed] = useState(false)
  const [showAccount, setShowAccount] = useState(false)
  const [machines, setMachines] = useState<Machine[]>([])
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<Machine | null>(null)
  const [activities, setActivities] = useState<ActivityItem[]>([])
  const [gatewayStatus, setGatewayStatus] = useState<GatewayStatus | null>(null)
  const [toast, setToast] = useState<Toast | null>(null)
  const [loadingMachines, setLoadingMachines] = useState(true)
  const [serverOnline, setServerOnline] = useState(true)
  const [agentSetup, setAgentSetup] = useState<{ machine: Machine; key: string } | null>(null)

  const notify = (text: string) => setToast({ text, error: false })
  const warn = (text: string) => setToast({ text, error: true })

  // Drops everything that belongs to the account, so the next person on this
  // device never sees it, even for a moment.
  const signedOut = useCallback(() => {
    setShowAccount(false)
    setEditing(null)
    setShowForm(false)
    setAgentSetup(null)
    setMachines([])
    setActivities([])
    setGatewayStatus(null)
    setToast(null)
    setLoadingMachines(true)
    setUser(null)
  }, [])

  const loadMachines = useCallback(async (quiet = false) => {
    try {
      const [items, history, status] = await Promise.all([api.machines(), api.activity(), api.status()])
      setMachines(items)
      setActivities(history)
      setGatewayStatus(status)
      setServerOnline(true)
      setLoadingMachines(false)
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        signedOut()
        return
      }
      setServerOnline(false)
      setLoadingMachines(false)
      if (!quiet) setToast({ text: 'Não foi possível atualizar o painel. Tente novamente.', error: true })
    }
  }, [signedOut])

  const checkSession = useCallback(() => {
    setBootFailed(false)
    api.session().then(({ user: current }) => setUser(current), () => setBootFailed(true))
  }, [])

  useEffect(() => {
    api.session().then(({ user: current }) => setUser(current), () => setBootFailed(true))
  }, [])

  // Offline at start: try again as soon as the device reconnects.
  useEffect(() => {
    if (!bootFailed) return
    window.addEventListener('online', checkSession)
    return () => window.removeEventListener('online', checkSession)
  }, [bootFailed, checkSession])

  const userId = user?.id
  useEffect(() => {
    if (!userId) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- state only changes after the requests resolve
    void loadMachines()
    const timer = window.setInterval(() => void loadMachines(true), 15000)
    return () => window.clearInterval(timer)
  }, [userId, loadMachines])

  if (user === undefined) {
    return bootFailed ? <OfflineScreen onRetry={checkSession} /> : <div className="boot-screen" aria-busy="true" aria-label="Carregando" />
  }
  if (user === null) return <AuthScreen onAuthenticated={setUser} />

  const gateway = gatewayStatus === null ? { title: '...', text: 'Verificando o gateway.' }
    : !gatewayStatus.gatewayConfigured ? { title: 'Não configurado', text: 'Defina GATEWAY_KEY e GATEWAY_OWNER_EMAIL com o seu e-mail na API.' }
      : gatewayStatus.gatewayOnline ? { title: 'Online', text: 'Serviço da residência conectado.' }
        : { title: 'Offline', text: 'Inicie o gateway no equipamento da residência.' }

  const wake = async (machine: Machine) => {
    notify(`Enviando Magic Packet para ${machine.name}...`)
    try {
      const result = await api.wake(machine.id)
      notify(result.message)
      await loadMachines()
    } catch (error) {
      warn(error instanceof Error ? error.message : 'Falha ao enviar o comando.')
    }
  }

  const logout = () => {
    void api.logout().catch(() => undefined).finally(signedOut)
  }

  const powerAction = async (machine: Machine, action: PowerAction) => {
    const { verb, warning } = powerActions.find((item) => item.action === action)!
    if (!confirm(`Deseja ${verb} ${machine.name}? ${warning}`)) return
    try {
      const result = await api.action(machine.id, action)
      notify(result.message)
      await loadMachines()
    } catch (error) {
      warn(error instanceof Error ? error.message : `Não foi possível ${verb} a máquina.`)
    }
  }

  const openAgentSetup = async (machine: Machine) => {
    try {
      const result = await api.agentKey(machine.id)
      setAgentSetup({ machine, key: result.key })
    } catch (error) {
      warn(error instanceof Error ? error.message : 'Não foi possível gerar a chave do agente.')
    }
  }

  return (
    <div className="app-frame">
      <aside className="sidebar">
        <Brand />
        <nav className="side-nav" aria-label="Navegação principal">
          <a className="nav-item active" href="#machines"><LayoutDashboard size={18} /> Visão geral</a>
          <a className="nav-item" href="#machines"><Monitor size={18} /> Máquinas <span>{machines.length}</span></a>
          <a className="nav-item" href="https://github.com/Victordemelo/wake-on-lan/blob/main/docs/REMOTE_SETUP.md" target="_blank" rel="noreferrer"><Radio size={18} /> Gateway <small>Guia</small></a>
          <a className="nav-item" href="#activity"><ScrollText size={18} /> Atividades</a>
          <button className="nav-item" onClick={() => setShowAccount(true)}><UserRound size={18} /> Minha conta</button>
        </nav>
        <div className="sidebar-bottom">
          <a className="nav-item" href="https://github.com/Victordemelo/wake-on-lan/blob/main/docs/REMOTE_SETUP.md" target="_blank" rel="noreferrer"><Settings size={18} /> Instalação</a>
          <div className={`local-status${serverOnline ? '' : ' offline'}`} role="status"><span /><div><strong>Servidor</strong><small>{serverOnline ? 'Operacional' : 'Sem conexão'}</small></div></div>
          <button className="nav-item logout" onClick={logout}><LogOut size={18} /> Sair</button>
        </div>
      </aside>

      <main className="workspace">
        <header className="mobile-topbar">
          <Brand />
          <div>
            <button className="icon-button" onClick={() => setShowAccount(true)} aria-label="Minha conta"><UserRound size={19} /></button>
            <button className="icon-button" onClick={logout} aria-label="Sair"><LogOut size={19} /></button>
          </div>
        </header>

        <section className="content" id="machines">
          <div className="page-heading">
            <div>
              <div className="breadcrumb">PAINEL <ChevronRight size={13} /> VISÃO GERAL</div>
              <h1>Olá, {user.name.split(' ')[0]}. Pronto para acordar suas máquinas?</h1>
              <p>Gerencie seus dispositivos e envie comandos de qualquer lugar.</p>
            </div>
            <button className="button primary" onClick={() => setShowForm(true)}><Plus size={18} /> Nova máquina</button>
          </div>

          <section className="stats-grid" aria-label="Resumo">
            <article className="stat-card">
              <span className="stat-icon mint"><Monitor size={21} /></span>
              <div><small>MÁQUINAS</small><strong>{machines.length}</strong><p>{machines.length === 1 ? 'dispositivo cadastrado' : 'dispositivos cadastrados'}</p></div>
            </article>
            <article className="stat-card">
              <span className="stat-icon cyan"><Network size={21} /></span>
              <div><small>GATEWAY</small><strong>{gateway.title}</strong><p>{gateway.text}</p></div>
            </article>
            <article className="stat-card">
              <span className="stat-icon violet">{user.twoFactorEnabled ? <ShieldCheck size={21} /> : <ShieldAlert size={21} />}</span>
              <div>
                <small>SEGURANÇA</small>
                <strong>{user.twoFactorEnabled ? 'Duas etapas ativa' : 'Somente senha'}</strong>
                <p>{user.twoFactorEnabled ? 'O login pede o código do aplicativo autenticador.'
                  : <>Ative a verificação em duas etapas em <button className="link-button" onClick={() => setShowAccount(true)}>Minha conta</button>.</>}</p>
              </div>
            </article>
          </section>

          {toast && (
            <div className={`toast${toast.error ? ' error' : ''}`} role={toast.error ? 'alert' : 'status'}>
              {toast.error ? <TriangleAlert size={17} /> : <Check size={17} />}<span>{toast.text}</span>
              <button onClick={() => setToast(null)} aria-label="Fechar"><X size={16} /></button>
            </div>
          )}

          <div className="section-heading">
            <div><h2>Suas máquinas</h2><p>Dispositivos configurados para Wake-on-LAN.</p></div>
            <span className="section-count">{machines.length} {machines.length === 1 ? 'máquina' : 'máquinas'}</span>
          </div>

          {loadingMachines ? (
            <div className="loading-grid"><span /><span /><span /></div>
          ) : machines.length === 0 ? (
            <EmptyState onAdd={() => setShowForm(true)} />
          ) : (
            <section className="machine-grid">
              {machines.map((machine) => (
                <MachineCard key={machine.id} machine={machine} onEdit={() => setEditing(machine)} onWake={() => void wake(machine)} onAction={(action) => void powerAction(machine, action)} onAgentSetup={() => void openAgentSetup(machine)} onRemove={async () => {
                  if (confirm(`Remover ${machine.name}?`)) {
                    try {
                    await api.removeMachine(machine.id)
                    await loadMachines()
                    } catch (error) { warn(error instanceof Error ? error.message : 'Falha ao remover.') }
                  }
                }} />
              ))}
              <button className="add-machine-card" onClick={() => setShowForm(true)}><span><Plus size={23} /></span><strong>Adicionar outra máquina</strong><small>Configure um novo dispositivo</small></button>
            </section>
          )}
          <section className="activity-section" id="activity">
            <div className="section-heading"><div><h2>Atividades recentes</h2><p>Últimas 100 ações de energia e confirmações de que a máquina ligou.</p></div></div>
            {activities.length === 0 ? <p className="activity-empty">Nenhuma ação registrada.</p> : <div className="activity-list">{activities.map((item) => <article key={item.id} className="activity-row">
              <span className={item.succeeded ? 'activity-success' : 'activity-failure'}>{item.succeeded ? 'Confirmado' : 'Sem sucesso'}</span>
              <div><strong>{item.machineName} · {activityLabels[item.action] ?? item.action}</strong><p>{item.message}</p></div>
              <time dateTime={item.requestedAt}>{formatDate(item.requestedAt)}</time>
            </article>)}</div>}
          </section>
        </section>
      </main>

      {(showForm || editing) && <MachineDialog initial={editing} onClose={() => { setShowForm(false); setEditing(null) }} onSaved={async () => {
        setShowForm(false)
        setEditing(null)
        notify('Máquina salva com sucesso.')
        await loadMachines()
      }} />}
      {showAccount && <AccountDialog user={user} onClose={() => setShowAccount(false)} onUserChange={setUser} onSignedOut={signedOut} />}
      {agentSetup && <div className="dialog-backdrop" onMouseDown={() => setAgentSetup(null)}>
        <div className="dialog agent-setup" role="dialog" aria-modal="true" aria-label="Configurar agente" onMouseDown={(event) => event.stopPropagation()}>
          <div className="dialog-title"><div><span className="dialog-icon"><KeyRound size={20} /></span><div><h2>Agente de {agentSetup.machine.name}</h2><p>Guarde esta chave somente no computador controlado.</p></div></div><button className="icon-button" onClick={() => setAgentSetup(null)} aria-label="Fechar"><X size={20} /></button></div>
          <div className="dialog-body"><p>ID da máquina</p><code>{agentSetup.machine.id}</code><p>Chave do agente</p><code className="secret-key">{agentSetup.key}</code><p>Configure <code>REMOTE_WAKE_MACHINE_ID</code> e <code>REMOTE_WAKE_KEY</code> no serviço local. Veja o <a href="https://github.com/Victordemelo/wake-on-lan/blob/main/docs/REMOTE_SETUP.md" target="_blank" rel="noreferrer">guia de instalação</a>.</p></div>
          <div className="dialog-actions"><button className="button secondary" onClick={async () => {
            if (!confirm('Revogar a chave atual? O agente precisará ser configurado novamente.')) return
            try { await api.revokeAgent(agentSetup.machine.id); setAgentSetup(null); notify('Chave revogada. Abra a configuração para obter a nova chave.'); await loadMachines() }
            catch (error) { warn(error instanceof Error ? error.message : 'Falha ao revogar.') }
          }}>Revogar chave</button><button className="button secondary" onClick={() => setAgentSetup(null)}>Fechar</button></div>
        </div>
      </div>}
    </div>
  )
}

function MachineCard({ machine, onWake, onAction, onAgentSetup, onEdit, onRemove }: { machine: Machine; onWake: () => void; onAction: (action: PowerAction) => void; onAgentSetup: () => void; onEdit: () => void; onRemove: () => void }) {
  return (
    <article className="machine-card">
      <div className="machine-top">
        <span className="device-icon"><Monitor size={23} /></span>
        <span className="configured"><span /> {machine.agentOnline ? 'Agente online' : 'Agente offline'}</span>
      </div>
      <h3>{machine.name}</h3>
      <p className="machine-host">{machine.hostname || 'Computador sem hostname'}</p>
      <dl>
        <div><dt>Endereço MAC</dt><dd>{formatMac(machine.macAddress)}</dd></div>
        <div><dt>Destino</dt><dd>{machine.broadcastAddress}:{machine.wolPort}</dd></div>
        <div><dt>Método</dt><dd>{methodLabel(machine.wakeMethod)}</dd></div>
        {machine.wakeMethod === 'TailscaleGateway' && <div><dt>Gateway</dt><dd>{machine.gatewayOnline ? 'Online' : 'Offline'}</dd></div>}
      </dl>
      {machine.lastWakeRequestedAt && <p className="last-action"><Activity size={14} /> Último envio {formatDate(machine.lastWakeRequestedAt)}</p>}
      <div className="card-actions">
        <button className="button power-button" onClick={onWake}><Power size={18} /> Ligar máquina</button>
        <button className="icon-button" onClick={onAgentSetup} aria-label={`Configurar agente de ${machine.name}`}><KeyRound size={16} /></button>
        <button className="icon-button" onClick={onEdit} aria-label={`Editar ${machine.name}`}><Pencil size={16} /></button>
        <button className="icon-button danger" onClick={onRemove} aria-label={`Remover ${machine.name}`}><Trash2 size={18} /></button>
      </div>
      <div className="power-grid" role="group" aria-label={`Ações de energia de ${machine.name}`}>
        {powerActions.map(({ action, label, icon: Icon }) => (
          <button key={action} className="button secondary" onClick={() => onAction(action)} disabled={!machine.agentOnline}
            title={machine.agentOnline ? undefined : 'Disponível quando o agente está online'}><Icon size={15} /> {label}</button>
        ))}
      </div>
    </article>
  )
}

function EmptyState({ onAdd }: { onAdd: () => void }) {
  return (
    <section className="empty-state">
      <div className="empty-visual">
        <span className="orbit orbit-one" /><span className="orbit orbit-two" />
        <span className="empty-icon"><Power size={36} /></span>
      </div>
      <p className="eyebrow">PRIMEIRO DISPOSITIVO</p>
      <h2>Seu painel está pronto.</h2>
      <p>Cadastre uma máquina para enviar seu primeiro Magic Packet.</p>
      <button className="button primary" onClick={onAdd}><Plus size={18} /> Cadastrar máquina</button>
    </section>
  )
}

function OfflineScreen({ onRetry }: { onRetry: () => void }) {
  return (
    <main className="offline-screen">
      <Brand />
      <section className="offline-card" role="alert">
        <span className="empty-icon"><WifiOff size={30} /></span>
        <h1>Sem conexão com o servidor</h1>
        <p>Verifique a internet do aparelho e se o Remote Wake está no ar. Sua sessão continua salva.</p>
        <button className="button primary" onClick={onRetry}><RotateCcw size={17} /> Tentar novamente</button>
      </section>
    </main>
  )
}

function ProjectCallToAction() {
  return (
    <div className="showcase-cta">
      <div><span>QUER ACOMPANHAR?</span><strong>O Remote Wake está sendo construído em público.</strong></div>
      <a href="https://github.com/Victordemelo/wake-on-lan" target="_blank" rel="noreferrer">Ver projeto no GitHub <ArrowRight size={17} /></a>
    </div>
  )
}

type AuthMode = 'login' | 'register' | 'two-factor'

function AuthScreen({ onAuthenticated }: { onAuthenticated: (user: User) => void }) {
  const [registration, setRegistration] = useState<RegistrationStatus>({ open: false, setupRequired: false })
  const [mode, setMode] = useState<AuthMode>('login')
  // Kept only in memory, to finish the login after the two-step code.
  const [credentials, setCredentials] = useState<{ email: string; password: string } | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [showPassword, setShowPassword] = useState(false)

  useEffect(() => {
    api.registration().then((status) => {
      setRegistration(status)
      if (status.setupRequired) setMode('register')
    }, () => undefined)
  }, [])

  const switchMode = (next: AuthMode) => {
    setMode(next)
    setError('')
    if (next !== 'two-factor') setCredentials(null)
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const email = String(data.get('email') ?? credentials?.email ?? '')
    const password = String(data.get('password') ?? credentials?.password ?? '')
    setLoading(true)
    setError('')
    try {
      const result = mode === 'register'
        ? await api.register(String(data.get('name')), email, password, registration.setupRequired ? String(data.get('setupToken')) : undefined)
        : await api.login(email, password, mode === 'two-factor' ? String(data.get('code')) : undefined)
      onAuthenticated(result.user)
    } catch (problem) {
      if (problem instanceof ApiError && problem.twoFactorRequired && mode === 'login') {
        setCredentials({ email, password })
        setMode('two-factor')
      } else {
        setError(problem instanceof Error ? problem.message : 'Não foi possível entrar.')
      }
    } finally {
      setLoading(false)
    }
  }

  const heading = mode === 'two-factor'
    ? { eyebrow: 'VERIFICAÇÃO EM DUAS ETAPAS', title: 'Digite o código', text: 'Abra o aplicativo autenticador ou use um dos códigos de recuperação.' }
    : mode === 'register' && registration.setupRequired
      ? { eyebrow: 'CONFIGURAÇÃO INICIAL', title: 'Crie a primeira conta', text: 'Ela será a conta principal desta instalação.' }
      : mode === 'register'
        ? { eyebrow: 'NOVA CONTA', title: 'Crie seu acesso', text: 'Leva menos de um minuto para começar.' }
        : { eyebrow: 'ACESSO SEGURO', title: 'Bem-vindo de volta', text: 'Entre para acessar suas máquinas.' }

  return (
    <div className="landing-page">
      <header className="landing-header">
        <Brand />
        <nav><a href="#project">O projeto</a><a className="button primary" href="#access">Entrar</a></nav>
      </header>

      <ProjectShowcase><ProjectCallToAction /></ProjectShowcase>

      <section className="landing-access" id="access">
        <div className="access-background"><i /><i /><i /></div>
        <div className="access-copy">
          <p className="eyebrow">COMECE AGORA</p>
          <h2>Seu computador,<br />ao alcance de um toque.</h2>
          <p>Entre no painel para cadastrar máquinas, enviar Magic Packets e preparar seu ambiente para o controle remoto.</p>
          <div className="access-features">
            <span><ShieldCheck size={17} /> Seus dados no seu servidor</span>
            <span><Wifi size={17} /> Wake-on-LAN, VPN e Tailscale</span>
          </div>
          <div className="creator-signature"><span>VM</span><div><strong>Victor de Melo da Rosa</strong><small>Estudante de Engenharia da Computação · Criador do Remote Wake</small></div></div>
        </div>

        <form className="auth-card" onSubmit={submit}>
          <div className="auth-card-logo"><Brand compact /></div>
          <p className="eyebrow">{heading.eyebrow}</p>
          <h2>{heading.title}</h2>
          <p>{heading.text}</p>
          {mode === 'two-factor' ? (
            <label>Código de verificação
              <input key="code" name="code" required autoFocus autoComplete="one-time-code" placeholder="123456 ou código de recuperação" />
            </label>
          ) : (
            <>
              {mode === 'register' && registration.setupRequired && (
                <label>Código de configuração
                  <input name="setupToken" required autoComplete="off" spellCheck={false} placeholder="XXXX-XXXX-XXXX" />
                  <small>Aparece nos logs da API: <code>docker compose logs api</code></small>
                </label>
              )}
              {mode === 'register' && <label>Nome completo<input name="name" minLength={2} required autoComplete="name" placeholder="Como devemos chamar você?" /></label>}
              <label>E-mail<input name="email" type="email" required autoComplete="email" placeholder="voce@exemplo.com" /></label>
              <label>Senha
                <span className="password-field"><input name="password" type={showPassword ? 'text' : 'password'} minLength={8} required autoComplete={mode === 'register' ? 'new-password' : 'current-password'} placeholder="Mínimo de 8 caracteres" /><button type="button" onClick={() => setShowPassword(!showPassword)} aria-label="Mostrar senha">{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></span>
              </label>
            </>
          )}
          {error && <div className="error" role="alert">{error}</div>}
          <button className="button primary submit-button" disabled={loading}>
            {loading ? 'Aguarde...' : mode === 'register' ? 'Criar conta' : mode === 'two-factor' ? 'Verificar' : 'Entrar no painel'} {!loading && <ArrowRight size={18} />}
          </button>
          {mode === 'two-factor' && <div className="auth-switch"><span>Entrou com outra conta?</span><button type="button" onClick={() => switchMode('login')}>Voltar</button></div>}
          {mode !== 'two-factor' && registration.open && !registration.setupRequired && (
            <div className="auth-switch">
              <span>{mode === 'register' ? 'Já possui uma conta?' : 'Primeira vez por aqui?'}</span>
              <button type="button" onClick={() => switchMode(mode === 'register' ? 'login' : 'register')}>{mode === 'register' ? 'Fazer login' : 'Criar conta'}</button>
            </div>
          )}
        </form>
      </section>

      <footer className="landing-footer"><span>Projeto criado por Victor de Melo da Rosa · Estudante de Engenharia da Computação</span><a href="https://github.com/Victordemelo/wake-on-lan" target="_blank" rel="noreferrer">MIT License · GitHub ↗</a></footer>
    </div>
  )
}

function MachineDialog({ initial, onClose, onSaved }: { initial: Machine | null; onClose: () => void; onSaved: () => void }) {
  const [machine, setMachine] = useState<MachineInput>(initial ? toInput(initial) : emptyMachine)
  const [error, setError] = useState('')

  const update = <K extends keyof MachineInput>(key: K, value: MachineInput[K]) =>
    setMachine((current) => ({ ...current, [key]: value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    try {
      if (initial) await api.updateMachine(initial.id, machine)
      else await api.addMachine(machine)
      onSaved()
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível cadastrar.')
    }
  }

  return (
    <div className="dialog-backdrop" onMouseDown={onClose}>
      <form className="dialog" onSubmit={submit} onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog-title"><div><span className="dialog-icon"><Monitor size={20} /></span><div><h2>{initial ? 'Editar máquina' : 'Nova máquina'}</h2><p>Configure o destino do Magic Packet.</p></div></div><button type="button" className="icon-button" onClick={onClose} aria-label="Fechar"><X size={20} /></button></div>
        <div className="dialog-body">
          <label>Nome da máquina<input value={machine.name} onChange={(e) => update('name', e.target.value)} placeholder="Ex.: PC principal" required /></label>
          <label>Endereço MAC<input value={machine.macAddress} onChange={(e) => update('macAddress', e.target.value)} placeholder="AA:BB:CC:DD:EE:FF" required /><small>Use o MAC da placa Ethernet que receberá o pacote.</small></label>
          <label>Método de ativação
            <select value={machine.wakeMethod} onChange={(e) => update('wakeMethod', e.target.value as WakeMethod)}>
              <option value="LocalBroadcast">Rede local ou VPN</option>
              <option value="WakeOnWan">Wake-on-WAN</option>
              <option value="TailscaleGateway">Gateway residencial / Tailscale</option>
            </select>
          </label>
          <div className="form-row">
            <label>Destino<input value={machine.broadcastAddress} onChange={(e) => update('broadcastAddress', e.target.value)} required /></label>
            <label>Porta UDP<input type="number" min="1" max="65535" value={machine.wolPort} onChange={(e) => update('wolPort', Number(e.target.value))} required /></label>
          </div>
          <label>Hostname <span className="optional">OPCIONAL</span><input value={machine.hostname} onChange={(e) => update('hostname', e.target.value)} placeholder="desktop-casa" /></label>
          {error && <div className="error">{error}</div>}
        </div>
        <div className="dialog-actions"><button type="button" className="button secondary" onClick={onClose}>Cancelar</button><button className="button primary">{initial ? <><Check size={17} /> Salvar alterações</> : <><Plus size={17} /> Salvar máquina</>}</button></div>
      </form>
    </div>
  )
}

const formatMac = (mac: string) => mac.match(/.{1,2}/g)?.join(':') ?? mac
// Only the editable fields, with values the inputs can hold (the API may send null).
const toInput = (machine: Machine): MachineInput => ({
  name: machine.name,
  macAddress: formatMac(machine.macAddress),
  hostname: machine.hostname ?? '',
  broadcastAddress: machine.broadcastAddress,
  wolPort: machine.wolPort,
  wakeMethod: machine.wakeMethod,
})
const methodLabel = (method: WakeMethod) => ({
  LocalBroadcast: 'Rede local / VPN', WakeOnWan: 'Wake-on-WAN', TailscaleGateway: 'Gateway residencial',
})[method]

export default App
