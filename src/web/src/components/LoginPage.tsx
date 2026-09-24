import { FormEvent, useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, Eye, EyeOff } from 'lucide-react'
import { api, ApiError, RegistrationStatus, User } from '../api'
import { links } from '../project'
import Brand from './Brand'
import GithubMark from './GithubMark'
import Link from './Link'

type AuthMode = 'login' | 'register' | 'two-factor'

export default function LoginPage({ onAuthenticated }: { onAuthenticated: (user: User) => void }) {
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
    <div className="login-page">
      <header className="login-header">
        <Brand />
        <Link className="login-back" to="/"><ArrowLeft size={16} /> Conhecer o projeto</Link>
      </header>

      <main className="login-main">
        <form className="auth-card" onSubmit={submit}>
          <p className="eyebrow">{heading.eyebrow}</p>
          <h1>{heading.title}</h1>
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
      </main>

      <footer className="login-footer">
        <span>Remote Wake · projeto de código aberto</span>
        <a href={links.repository} target="_blank" rel="noreferrer"><GithubMark /> GitHub</a>
      </footer>
    </div>
  )
}
