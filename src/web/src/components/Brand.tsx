import Link from './Link'

export default function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <Link className={`brand ${compact ? 'brand-compact' : ''}`} to="/" aria-label="Remote Wake">
      <span className="brand-symbol"><img src="/brand/remote-wake-mark.svg" alt="" /></span>
      {!compact && <span>Remote <strong>Wake</strong></span>}
    </Link>
  )
}
