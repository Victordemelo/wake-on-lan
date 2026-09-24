import { AnchorHTMLAttributes } from 'react'
import { navigate } from '../navigation'

type LinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & { to: string }

// In-app link: a real <a href> (open in new tab still works) that switches pages without a reload.
export default function Link({ to, onClick, ...props }: LinkProps) {
  return (
    <a
      {...props}
      href={to}
      onClick={(event) => {
        onClick?.(event)
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
        event.preventDefault()
        navigate(to)
      }}
    />
  )
}
