import {
  ArrowRight, BookOpen, Cable, Check, CircleCheck, Container, Download, Globe, KeyRound, Monitor, Moon,
  Power, PowerOff, RotateCcw, Router, Server, ShieldCheck, Smartphone, Snowflake,
} from 'lucide-react'
import { links } from '../../project'
import Brand from '../Brand'
import GithubMark from '../GithubMark'
import Link from '../Link'
import './landing.css'

const flow = [
  { icon: Smartphone, title: 'Você pede', text: 'Pelo celular ou pelo navegador, de qualquer lugar, com login e verificação em duas etapas.' },
  { icon: Server, title: 'O servidor confere', text: 'A API valida a sua conta, registra o pedido no histórico e o entrega ao gateway da sua casa.' },
  { icon: Router, title: 'O gateway acorda o PC', text: 'Um aparelho sempre ligado na mesma rede, como um Raspberry Pi, envia o Magic Packet.' },
  { icon: Monitor, title: 'O PC confirma', text: 'Quando o computador liga, o agente avisa o painel. Por ele você também desliga, reinicia, suspende ou hiberna.' },
]

const features = [
  { icon: Power, title: 'Ligar com Wake-on-LAN', text: 'Pela rede local, por VPN, por Wake-on-WAN ou pelo gateway residencial com Tailscale.' },
  { icon: PowerOff, title: 'Desligar, reiniciar, suspender e hibernar', text: 'Um agente leve para Windows e Linux executa as ações, com uma chave própria para cada máquina.' },
  { icon: CircleCheck, title: 'Confirmação de que ligou', text: 'O histórico mostra quando o computador realmente voltou, não só que o pacote foi enviado.' },
  { icon: ShieldCheck, title: 'Conta protegida', text: 'Verificação em duas etapas, códigos de recuperação, controle de sessões e bloqueio de tentativas.' },
  { icon: Smartphone, title: 'Pensado para o celular', text: 'Painel responsivo que pode ser instalado como app. O APK para Android está a caminho.' },
  { icon: Container, title: 'Seu servidor, seus dados', text: 'Sobe com Docker Compose em PC, NAS ou Raspberry Pi. Contas, máquinas e histórico ficam com você.' },
]

const requirements = [
  { icon: Router, title: 'Um aparelho sempre ligado', text: 'Na mesma rede do PC (Raspberry Pi, NAS ou mini PC), rodando o servidor e o gateway.' },
  { icon: Globe, title: 'Acesso seguro de fora', text: 'Tailscale, sem abrir portas no roteador, ou um domínio com HTTPS.' },
  { icon: Cable, title: 'Wake-on-LAN ativado', text: 'Na BIOS e na placa de rede do computador, de preferência ligado por cabo.' },
  { icon: KeyRound, title: 'Agente no computador', text: 'Necessário para desligar, reiniciar, suspender e hibernar.' },
]

const installSteps: ({ comment: string } | { command: string })[] = [
  { comment: 'baixe o projeto' },
  { command: `git clone ${links.repository}.git` },
  { command: 'cd wake-on-lan' },
  { comment: 'defina as senhas e chaves no .env' },
  { command: 'cp .env.example .env' },
  { command: 'docker compose up --build -d' },
]

const external = { target: '_blank', rel: 'noreferrer' }

export default function LandingPage() {
  return (
    <div className="lp">
      <header className="lp-header">
        <div className="lp-container lp-header-inner">
          <Brand />
          <nav className="lp-nav" aria-label="Seções da página">
            <a href="#como-funciona">Como funciona</a>
            <a href="#recursos">Recursos</a>
            <a href="#android">Android</a>
            <a href="#instalar">Instalação</a>
          </nav>
          <div className="lp-header-actions">
            <a className="lp-github" href={links.repository} {...external} aria-label="Código no GitHub"><GithubMark size={18} /><span>GitHub</span></a>
            <Link className="button primary lp-enter" to="/login">Entrar</Link>
          </div>
        </div>
      </header>

      <main>
        <section className="lp-hero">
          <div className="lp-container lp-hero-inner">
            <div className="lp-hero-copy">
              <p className="lp-kicker"><span /> Código aberto · Hospedado por você</p>
              <h1>Ligue e desligue <span>seu PC</span> <em>de qualquer lugar.</em></h1>
              <p className="lp-lead">O Remote Wake acorda o computador pela rede com Wake-on-LAN e, com um agente seguro, desliga, reinicia, suspende ou hiberna. Tudo roda no seu próprio servidor.</p>
              <div className="lp-actions">
                <a className="button primary lp-button" href={links.repository} {...external}><GithubMark size={18} /> Ver no GitHub</a>
                <a className="button secondary lp-button" href="#android"><Smartphone size={18} /> App para Android</a>
              </div>
              <ul className="lp-proof">
                <li><Check size={15} /> Licença MIT</li>
                <li><Check size={15} /> Docker para PC e Raspberry Pi</li>
                <li><Check size={15} /> Verificação em duas etapas</li>
              </ul>
            </div>
            <HeroDemo />
          </div>
        </section>

        <section className="lp-section" id="como-funciona">
          <div className="lp-container">
            <div className="lp-heading">
              <p className="eyebrow">COMO FUNCIONA</p>
              <h2>Do toque no celular ao PC ligado.</h2>
              <p>Quatro peças simples, e todas ficam sob o seu controle.</p>
            </div>
            <ol className="lp-flow">
              {flow.map(({ icon: Icon, title, text }, index) => (
                <li key={title}>
                  <span className="lp-flow-icon"><Icon size={22} /></span>
                  <span className="lp-flow-step">{String(index + 1).padStart(2, '0')}</span>
                  <h3>{title}</h3>
                  <p>{text}</p>
                </li>
              ))}
            </ol>
            <p className="lp-note"><ShieldCheck size={18} /> O gateway e o agente só fazem conexões de saída até o servidor: você não abre nenhuma porta para o seu PC.</p>
          </div>
        </section>

        <section className="lp-section" id="recursos">
          <div className="lp-container">
            <div className="lp-heading">
              <p className="eyebrow">RECURSOS</p>
              <h2>Tudo para controlar seus computadores.</h2>
              <p>Do Wake-on-LAN ao histórico de cada ação, num servidor que é seu.</p>
            </div>
            <div className="lp-features">
              {features.map(({ icon: Icon, title, text }) => (
                <article key={title}>
                  <span className="lp-feature-icon"><Icon size={20} /></span>
                  <h3>{title}</h3>
                  <p>{text}</p>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className="lp-section" id="android">
          <div className="lp-container">
            <div className="lp-android">
              <div className="lp-android-copy">
                <p className="eyebrow">ANDROID</p>
                <h2>Seu PC no bolso.</h2>
                <p>O app para Android abre o painel do seu servidor em tela cheia: você informa o endereço uma vez e liga ou desliga seus computadores com um toque.</p>
                <div className="lp-actions">
                  <span className="lp-apk-soon"><Download size={18} /> Baixar APK <em>Em breve</em></span>
                </div>
                <div className="lp-pwa">
                  <strong>Já dá para usar hoje</strong>
                  <p>Abra o endereço do seu servidor no Chrome do Android, toque no menu <b>⋮</b> e escolha <b>Instalar app</b>.</p>
                </div>
              </div>
              <div className="lp-android-visual" aria-hidden="true">
                <span className="lp-app-icon"><img src="/brand/icon-192.png" alt="" /></span>
                <strong>Remote Wake</strong>
                <small>para Android</small>
              </div>
            </div>
          </div>
        </section>

        <section className="lp-section" id="instalar">
          <div className="lp-container">
            <div className="lp-heading">
              <p className="eyebrow">INSTALAÇÃO</p>
              <h2>No ar em poucos minutos.</h2>
              <p>Você só precisa do Docker. O mesmo projeto roda em PC, NAS ou Raspberry Pi.</p>
            </div>
            <div className="lp-install">
              <div className="lp-terminal">
                <div className="lp-terminal-bar" aria-hidden="true"><i /><i /><i /><span>terminal</span></div>
                <pre><code>{installSteps.map((line, index) => 'comment' in line
                  ? <span key={index} className="lp-comment"># {line.comment}{'\n'}</span>
                  : <span key={index}><span className="lp-prompt" aria-hidden="true">$ </span>{line.command}{'\n'}</span>)}</code></pre>
              </div>
              <div className="lp-requirements">
                <h3>Para usar de fora de casa</h3>
                <ul>
                  {requirements.map(({ icon: Icon, title, text }) => (
                    <li key={title}><span><Icon size={18} /></span><div><strong>{title}</strong><p>{text}</p></div></li>
                  ))}
                </ul>
                <div className="lp-links">
                  <a href={links.install} {...external}><BookOpen size={16} /> Instalação passo a passo</a>
                  <a href={links.remoteSetup} {...external}><Globe size={16} /> Guia de acesso remoto</a>
                </div>
              </div>
            </div>
          </div>
        </section>

        <section className="lp-section">
          <div className="lp-container lp-open">
            <div className="lp-open-copy">
              <p className="eyebrow">CÓDIGO ABERTO</p>
              <h2>Construído em público.</h2>
              <p>O Remote Wake é open source, sob licença MIT. Leia o código, acompanhe o que vem por aí, abra uma issue ou contribua com melhorias.</p>
              <div className="lp-actions">
                <a className="button primary lp-button" href={links.repository} {...external}><GithubMark size={18} /> Ver no GitHub</a>
                <a className="button secondary lp-button" href={links.issues} {...external}>Sugerir uma melhoria <ArrowRight size={17} /></a>
              </div>
            </div>
            <aside className="lp-creator" aria-label="Criador do projeto">
              <span className="lp-monogram" aria-hidden="true">VM</span>
              <div>
                <small>CRIADO POR</small>
                <strong>Victor de Melo da Rosa</strong>
                <p>Estudante de Engenharia da Computação, desenvolvendo o Remote Wake para explorar redes, segurança, sistemas distribuídos e automação.</p>
              </div>
            </aside>
          </div>
        </section>
      </main>

      <footer className="lp-footer">
        <div className="lp-container lp-footer-inner">
          <div>
            <Brand />
            <p>Ligue e desligue seus computadores de qualquer lugar.</p>
          </div>
          <nav aria-label="Links do projeto">
            <a href={links.repository} {...external}>GitHub</a>
            <a href={links.install} {...external}>Documentação</a>
            <a href={links.security} {...external}>Segurança</a>
            <a href={links.license} {...external}>Licença MIT</a>
            <Link to="/login">Entrar</Link>
          </nav>
        </div>
      </footer>
    </div>
  )
}

// Decorative product shot: the panel on a phone, looping through a wake request.
function HeroDemo() {
  return (
    <div className="lp-demo" aria-hidden="true">
      <div className="lp-device">
        <div className="lp-demo-glow" />
        <div className="lp-phone">
          <div className="lp-phone-screen">
            <div className="lp-phone-status"><span>9:41</span><b /></div>
            <div className="lp-phone-app"><img src="/brand/remote-wake-mark.svg" alt="" /> Remote <strong>Wake</strong></div>
            <p className="lp-phone-label">Suas máquinas</p>
            <div className="lp-phone-card">
              <div className="lp-phone-card-top">
                <span className="lp-phone-device"><Monitor size={17} /></span>
                <span className="lp-phone-state"><span className="off">Desligado</span><span className="on">Ligado</span></span>
              </div>
              <strong>PC do escritório</strong>
              <small>Gateway residencial</small>
              <span className="lp-phone-wake"><Power size={15} /> Ligar máquina</span>
              <div className="lp-phone-actions">
                <span><Power size={11} /> Desligar</span>
                <span><RotateCcw size={11} /> Reiniciar</span>
                <span><Moon size={11} /> Suspender</span>
                <span><Snowflake size={11} /> Hibernar</span>
              </div>
            </div>
            <div className="lp-phone-card lp-phone-card-small">
              <span className="lp-phone-device"><Monitor size={15} /></span>
              <div><strong>Notebook da sala</strong><small>Rede local</small></div>
            </div>
            <div className="lp-phone-toasts">
              <span className="sent"><Check size={14} /> Magic Packet enviado</span>
              <span className="on"><CircleCheck size={14} /> PC do escritório ligou</span>
            </div>
          </div>
        </div>
        <div className="lp-float lp-float-gateway"><span><Router size={17} /></span><div><strong>Gateway em casa</strong><small><i /> Raspberry Pi · online</small></div></div>
        <div className="lp-float lp-float-security"><span><ShieldCheck size={17} /></span><div><strong>Conta protegida</strong><small><i /> Duas etapas ativa</small></div></div>
      </div>
    </div>
  )
}
