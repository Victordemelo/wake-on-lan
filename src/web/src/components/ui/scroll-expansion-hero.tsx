import { ReactNode, useRef } from 'react'
import { motion, useReducedMotion, useScroll, useTransform } from 'framer-motion'
import { ArrowDown, Code2, Globe2, Monitor, Network, Radio, Server, ShieldCheck } from 'lucide-react'

interface ScrollExpansionHeroProps {
  children?: ReactNode
}

export default function ScrollExpansionHero({ children }: ScrollExpansionHeroProps) {
  const trackRef = useRef<HTMLDivElement>(null)
  const reduceMotion = useReducedMotion()
  const { scrollYProgress } = useScroll({
    target: trackRef,
    offset: ['start start', 'end end'],
  })

  const mediaWidth = useTransform(
    scrollYProgress,
    [0, 0.72],
    reduceMotion ? ['min(1160px, 94vw)', 'min(1160px, 94vw)'] : ['min(360px, 84vw)', 'min(1160px, 94vw)'],
  )
  const mediaHeight = useTransform(
    scrollYProgress,
    [0, 0.72],
    reduceMotion ? ['78vh', '78vh'] : ['440px', '78vh'],
  )
  const mediaRadius = useTransform(scrollYProgress, [0, 0.72], ['28px', '18px'])
  const titleLeftOffset = useTransform(
    scrollYProgress,
    [0, 0.58],
    reduceMotion ? ['0vw', '0vw'] : ['0vw', '-8vw'],
  )
  const titleRightOffset = useTransform(
    scrollYProgress,
    [0, 0.58],
    reduceMotion ? ['0vw', '0vw'] : ['0vw', '8vw'],
  )
  const titleOpacity = useTransform(scrollYProgress, [0, 0.38, 0.62], [1, 0.92, 0])
  const titleScale = useTransform(scrollYProgress, [0, 0.58], [1, 1.04])
  const overlayOpacity = useTransform(scrollYProgress, [0, 0.72], [0.52, 0.1])
  const interfaceScale = useTransform(scrollYProgress, [0, 0.72], [0.83, 1])
  const interfaceOpacity = useTransform(scrollYProgress, [0.12, 0.72], [0.45, 1])
  const cueOpacity = useTransform(scrollYProgress, [0, 0.24], [1, 0])

  return (
    <section className="project-showcase" id="project">
      <div className="showcase-track" ref={trackRef}>
      <div className="showcase-sticky">
        <motion.div
          className="showcase-heading"
          aria-hidden="true"
          style={{ opacity: titleOpacity, scale: titleScale }}
        >
          <motion.span style={{ x: titleLeftOffset }}>REMOTE</motion.span>
          <motion.span style={{ x: titleRightOffset }}>WAKE</motion.span>
        </motion.div>

        <motion.div
          className="showcase-media"
          style={{ width: mediaWidth, height: mediaHeight, borderRadius: mediaRadius }}
        >
          <div className="showcase-grid" />
          <motion.div className="showcase-overlay" style={{ opacity: overlayOpacity }} />
          <div className="showcase-orb showcase-orb-one" />
          <div className="showcase-orb showcase-orb-two" />

          <motion.div
            className="showcase-interface"
            style={{ scale: interfaceScale, opacity: interfaceOpacity }}
          >
            <div className="showcase-interface-top">
              <div className="showcase-mini-brand">
                <span><img src="/brand/remote-wake-mark.svg" alt="" /></span>
                <strong>Remote Wake</strong>
              </div>
              <span className="showcase-live"><i /> ONLINE</span>
            </div>

            <div className="showcase-copy">
              <span>OPEN SOURCE · SELF-HOSTED</span>
              <h2>Controle começa<br />com um único pacote.</h2>
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
          </motion.div>
        </motion.div>

        <motion.div className="scroll-cue" style={{ opacity: cueOpacity }}>
          <span>ROLE PARA EXPANDIR</span><ArrowDown size={16} />
        </motion.div>
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
