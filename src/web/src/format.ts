export const formatDate = (date: string) =>
  new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(date))

// Short, human description of a User-Agent: "Chrome no Android".
export function describeAgent(agent?: string) {
  if (!agent) return 'Dispositivo desconhecido'
  if (agent === 'linha de comando') return 'Linha de comando do servidor'
  const browser = /Edg\//.test(agent) ? 'Edge'
    : /OPR\//.test(agent) ? 'Opera'
      : /Firefox\//.test(agent) ? 'Firefox'
        : /Chrome\//.test(agent) ? 'Chrome'
          : /Safari\//.test(agent) ? 'Safari'
            : 'Navegador'
  const system = /Android/.test(agent) ? 'Android'
    : /iPhone|iPad/.test(agent) ? 'iOS'
      : /Windows/.test(agent) ? 'Windows'
        : /Mac OS X/.test(agent) ? 'macOS'
          : /Linux/.test(agent) ? 'Linux'
            : ''
  return system ? `${browser} no ${system}` : browser
}
