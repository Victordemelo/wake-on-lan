import { ReactNode } from 'react'
import { Code2, Globe2, Monitor, Network, Radio, Server, ShieldCheck } from 'lucide-react'

export default function ProjectShowcase({ children }: { children?: ReactNode }) {
  return (
    <section className="project-showcase" id="project">
      <div className="showcase-track">
        <div className="showcase-media">
          <div className="showcase-grid" />
          <div className="showcase-orb showcase-orb-one" />
          <div className="showcase-orb showcase-orb-two" />

          <div className="showcase-interface">
            <div className="showcase-interface-top">
              <div className="showcase-mini-brand">
                <span><img src="/brand/remote-wake-mark.svg" alt="" /></span>
                <strong>Remote Wake</strong>
              </div>
              <span className="showcase-live"><i /> ONLINE</span>
            </div>

            <div className="showcase-copy">
              <span>OPEN SOURCE · SELF-HOSTED</span>
              <h1>Controle começa<br />com um único pacote.</h1>
              <p>Wake-on-LAN, acesso remoto e automação reunidos em uma plataforma criada para a comunidade.</p>
            </div>

            <div className="showcase-network" aria-label="Fluxo Remote Wake">
              <div><span><Globe2 size={20} /></span><small>VOCÊ</small><strong>Web / PWA</strong></div>
              <i className="network-connection"><b /><b /><b /></i>
              <div><span className="highlight"><Server size={20} /></span><small>REMOTE WAKE</small><strong>Gateway</strong></div>
              <i className="network-connection"><b /><b /><b /></i>
              <div><span><Monitor size={20} /></span><small>EM CASA</small><strong>Seu PC</strong></div>
            </div>

            <div className="showcase-badges">
              <span><ShieldCheck size={14} /> Seguro</span>
              <span><Network size={14} /> Multiplataforma</span>
              <span><Code2 size={14} /> Código aberto</span>
            </div>
          </div>
        </div>
      </div>

      <div className="showcase-story">
        <div className="showcase-story-intro">
          <p className="eyebrow">SOBRE O PROJETO</p>
          <h2>De uma necessidade real a um projeto para todos.</h2>
          <p>O Remote Wake nasceu para tornar simples aquilo que normalmente exige vários aplicativos, configurações e soluções isoladas.</p>
        </div>

        <div className="creator-card">
          <div className="creator-monogram">VM</div>
          <div>
            <span>CRIADO POR</span>
            <h3>Victor de Melo da Rosa</h3>
            <p>Estudante de Engenharia da Computação, desenvolvendo o Remote Wake como um projeto open source para explorar redes, segurança, sistemas distribuídos e automação.</p>
          </div>
        </div>

        <div className="project-pillars">
          <article><Radio size={20} /><span>01</span><h3>Acordar</h3><p>Wake-on-LAN local, Wake-on-WAN, VPN e Tailscale.</p></article>
          <article><Server size={20} /><span>02</span><h3>Controlar</h3><p>Agentes Windows e Linux para ações remotas autorizadas.</p></article>
          <article><ShieldCheck size={20} /><span>03</span><h3>Compartilhar</h3><p>Arquitetura self-hosted, transparente e aberta à comunidade.</p></article>
        </div>

        {children}
      </div>
    </section>
  )
}
