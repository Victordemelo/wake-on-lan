import { FormEvent, useEffect, useState } from 'react'
import {
  Activity, ArrowRight, Check, ChevronRight, Eye, EyeOff, LayoutDashboard,
  LogOut, Monitor, Network, Plus, Power, Radio, ScrollText, Server, Settings,
  ShieldCheck, Trash2, Wifi, X, RotateCcw, KeyRound,
} from 'lucide-react'
import { api, AuthResponse, Machine, MachineInput, session, WakeMethod } from './api'
import ProjectShowcase from './components/ui/ProjectShowcase'

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

function App() {
  const [authenticated, setAuthenticated] = useState(Boolean(session.get()))
  const [machines, setMachines] = useState<Machine[]>([])
  const [showForm, setShowForm] = useState(false)
  const [message, setMessage] = useState('')
  const [loadingMachines, setLoadingMachines] = useState(true)
  const [agentSetup, setAgentSetup] = useState<{ machine: Machine; key: string } | null>(null)

  const loadMachines = async (quiet = false) => {
    if (!quiet) setLoadingMachines(true)
    try {
      setMachines(await api.machines())
    } catch {
      if (!quiet) {
        session.clear()
        setAuthenticated(false)
      }
    } finally {
      if (!quiet) setLoadingMachines(false)
    }
  }

  useEffect(() => {
    if (!authenticated) return
    void loadMachines()
    const timer = window.setInterval(() => void loadMachines(true), 15000)
    return () => window.clearInterval(timer)
  }, [authenticated])

  if (!authenticated) {
    return <AuthScreen onAuthenticated={(auth) => {
      session.set(auth.token)
      setAuthenticated(true)
    }} />
  }

  const wake = async (machine: Machine) => {
    setMessage(`Enviando Magic Packet para ${machine.name}...`)
    try {
      const result = await api.wake(machine.id)
      setMessage(result.message)
      await loadMachines()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Falha ao enviar o comando.')
    }
  }

  const logout = () => {
    session.clear()
    setAuthenticated(false)
  }

  const powerAction = async (machine: Machine, action: 'shutdown' | 'restart') => {
    const label = action === 'shutdown' ? 'desligar' : 'reiniciar'
    if (!confirm(`Deseja ${label} ${machine.name}? Salve o trabalho aberto nessa máquina antes de continuar.`)) return
    try {
      const result = await api.action(machine.id, action)
      setMessage(result.message)
      await loadMachines()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : `Não foi possível ${label} a máquina.`)
    }
  }

  const openAgentSetup = async (machine: Machine) => {
    try {
      const result = await api.agentKey(machine.id)
      setAgentSetup({ machine, key: result.key })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Não foi possível gerar a chave do agente.')
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
          <button className="nav-item" disabled><ScrollText size={18} /> Atividades</button>
        </nav>
        <div className="sidebar-bottom">
          <button className="nav-item" disabled><Settings size={18} /> Configurações</button>
          <div className="local-status"><span /><div><strong>Servidor local</strong><small>Operacional</small></div></div>
          <button className="nav-item logout" onClick={logout}><LogOut size={18} /> Sair</button>
        </div>
      </aside>

      <main className="workspace">
        <header className="mobile-topbar">
          <Brand />
          <button className="icon-button" onClick={logout} aria-label="Sair"><LogOut size={19} /></button>
        </header>

        <section className="content" id="machines">
          <div className="page-heading">
            <div>
              <div className="breadcrumb">PAINEL <ChevronRight size={13} /> VISÃO GERAL</div>
              <h1>Olá, pronto para acordar suas máquinas?</h1>
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
              <div><small>GATEWAY</small><strong>{machines.some((machine) => machine.gatewayOnline) ? 'Online' : 'Pendente'}</strong><p>Disponível após configurar o serviço na residência</p></div>
            </article>
            <article className="stat-card">
              <span className="stat-icon violet"><ShieldCheck size={21} /></span>
              <div><small>API</small><strong>Protegida</strong><p>Autenticação JWT ativa</p></div>
            </article>
          </section>

          {message && <div className="toast" role="status"><Check size={17} /><span>{message}</span><button onClick={() => setMessage('')} aria-label="Fechar"><X size={16} /></button></div>}

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
                <MachineCard key={machine.id} machine={machine} onWake={() => void wake(machine)} onAction={(action) => void powerAction(machine, action)} onAgentSetup={() => void openAgentSetup(machine)} onRemove={async () => {
                  if (confirm(`Remover ${machine.name}?`)) {
                    await api.removeMachine(machine.id)
                    await loadMachines()
                  }
                }} />
              ))}
              <button className="add-machine-card" onClick={() => setShowForm(true)}><span><Plus size={23} /></span><strong>Adicionar outra máquina</strong><small>Configure um novo dispositivo</small></button>
            </section>
          )}
        </section>
      </main>

      {showForm && <MachineDialog onClose={() => setShowForm(false)} onSaved={async () => {
        setShowForm(false)
        setMessage('Máquina cadastrada com sucesso.')
        await loadMachines()
      }} />}
      {agentSetup && <div className="dialog-backdrop" onMouseDown={() => setAgentSetup(null)}>
        <div className="dialog agent-setup" role="dialog" aria-modal="true" aria-label="Configurar agente" onMouseDown={(event) => event.stopPropagation()}>
          <div className="dialog-title"><div><span className="dialog-icon"><KeyRound size={20} /></span><div><h2>Agente de {agentSetup.machine.name}</h2><p>Guarde esta chave somente no computador controlado.</p></div></div><button className="icon-button" onClick={() => setAgentSetup(null)} aria-label="Fechar"><X size={20} /></button></div>
          <div className="dialog-body"><p>ID da máquina</p><code>{agentSetup.machine.id}</code><p>Chave do agente</p><code className="secret-key">{agentSetup.key}</code><p>Configure <code>REMOTE_WAKE_MACHINE_ID</code> e <code>REMOTE_WAKE_KEY</code> no serviço local. Veja o <a href="https://github.com/Victordemelo/wake-on-lan/blob/main/docs/REMOTE_SETUP.md" target="_blank" rel="noreferrer">guia de instalação</a>.</p></div>
          <div className="dialog-actions"><button className="button secondary" onClick={() => setAgentSetup(null)}>Fechar</button></div>
        </div>
      </div>}
    </div>
  )
}

function MachineCard({ machine, onWake, onAction, onAgentSetup, onRemove }: { machine: Machine; onWake: () => void; onAction: (action: 'shutdown' | 'restart') => void; onAgentSetup: () => void; onRemove: () => void }) {
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
        <button className="icon-button danger" onClick={onRemove} aria-label={`Remover ${machine.name}`}><Trash2 size={18} /></button>
      </div>
      <div className="card-actions secondary-actions">
        <button className="button secondary" onClick={() => onAction('shutdown')} disabled={!machine.agentOnline}><Power size={15} /> Desligar</button>
        <button className="button secondary" onClick={() => onAction('restart')} disabled={!machine.agentOnline}><RotateCcw size={15} /> Reiniciar</button>
        <button className="icon-button" onClick={onAgentSetup} aria-label={`Configurar agente de ${machine.name}`}><KeyRound size={16} /></button>
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

function ProjectCallToAction() {
  return (
    <div className="showcase-cta">
      <div><span>QUER ACOMPANHAR?</span><strong>O Remote Wake está sendo construído em público.</strong></div>
      <a href="https://github.com/Victordemelo/wake-on-lan" target="_blank" rel="noreferrer">Ver projeto no GitHub <ArrowRight size={17} /></a>
    </div>
  )
}

function AuthScreen({ onAuthenticated }: { onAuthenticated: (auth: AuthResponse) => void }) {
  const [register, setRegister] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [showPassword, setShowPassword] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    setLoading(true)
    setError('')
    try {
      const result = register
        ? await api.register(String(data.get('name')), String(data.get('email')), String(data.get('password')))
        : await api.login(String(data.get('email')), String(data.get('password')))
      onAuthenticated(result)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível entrar.')
    } finally {
      setLoading(false)
    }
  }

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
          <p className="eyebrow">{register ? 'NOVA CONTA' : 'ACESSO SEGURO'}</p>
          <h2>{register ? 'Crie seu acesso' : 'Bem-vindo de volta'}</h2>
          <p>{register ? 'Leva menos de um minuto para começar.' : 'Entre para acessar suas máquinas.'}</p>
          {register && <label>Nome completo<input name="name" minLength={2} required autoComplete="name" placeholder="Como devemos chamar você?" /></label>}
          <label>E-mail<input name="email" type="email" required autoComplete="email" placeholder="voce@exemplo.com" /></label>
          <label>Senha
            <span className="password-field"><input name="password" type={showPassword ? 'text' : 'password'} minLength={8} required autoComplete={register ? 'new-password' : 'current-password'} placeholder="Mínimo de 8 caracteres" /><button type="button" onClick={() => setShowPassword(!showPassword)} aria-label="Mostrar senha">{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></span>
          </label>
          {error && <div className="error" role="alert">{error}</div>}
          <button className="button primary submit-button" disabled={loading}>{loading ? 'Aguarde...' : register ? 'Criar minha conta' : 'Entrar no painel'} {!loading && <ArrowRight size={18} />}</button>
          <div className="auth-switch"><span>{register ? 'Já possui uma conta?' : 'Primeira vez por aqui?'}</span><button type="button" onClick={() => { setRegister(!register); setError('') }}>{register ? 'Fazer login' : 'Criar conta'}</button></div>
        </form>
      </section>

      <footer className="landing-footer"><span>Projeto criado por Victor de Melo da Rosa · Estudante de Engenharia da Computação</span><a href="https://github.com/Victordemelo/wake-on-lan" target="_blank" rel="noreferrer">MIT License · GitHub ↗</a></footer>
    </div>
  )
}

function MachineDialog({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [machine, setMachine] = useState(emptyMachine)
  const [error, setError] = useState('')

  const update = <K extends keyof MachineInput>(key: K, value: MachineInput[K]) =>
    setMachine((current) => ({ ...current, [key]: value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    try {
      await api.addMachine(machine)
      onSaved()
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível cadastrar.')
    }
  }

  return (
    <div className="dialog-backdrop" onMouseDown={onClose}>
      <form className="dialog" onSubmit={submit} onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog-title"><div><span className="dialog-icon"><Monitor size={20} /></span><div><h2>Nova máquina</h2><p>Configure o destino do Magic Packet.</p></div></div><button type="button" className="icon-button" onClick={onClose}><X size={20} /></button></div>
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
        <div className="dialog-actions"><button type="button" className="button secondary" onClick={onClose}>Cancelar</button><button className="button primary"><Plus size={17} /> Salvar máquina</button></div>
      </form>
    </div>
  )
}

const formatMac = (mac: string) => mac.match(/.{1,2}/g)?.join(':') ?? mac
const formatDate = (date: string) => new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(date))
const methodLabel = (method: WakeMethod) => ({
  LocalBroadcast: 'Rede local / VPN', WakeOnWan: 'Wake-on-WAN', TailscaleGateway: 'Tailscale',
})[method]

export default App
