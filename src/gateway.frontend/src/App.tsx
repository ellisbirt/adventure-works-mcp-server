import { useEffect, useState } from 'react'
import { ArrowUpRight, Check, Database, LoaderCircle, Search, ShieldCheck, Terminal, X } from 'lucide-react'
import './App.css'

type ToolResponse = {
  tools: Array<{
    name: string
    description: string
    inputSchema: { required?: string[] }
  }>
}

type CallResponse = {
  content: Array<{ type: string; text: string }>
  isError: boolean
}

const apiUrl = import.meta.env.VITE_GATEWAY_URL ?? '/api'

function App() {
  const [customerId, setCustomerId] = useState('')
  const [tool, setTool] = useState<ToolResponse['tools'][number] | null>(null)
  const [result, setResult] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    fetch(`${apiUrl}/mcp/tools`)
      .then((response) => {
        if (!response.ok) throw new Error('Gateway unavailable')
        return response.json() as Promise<ToolResponse>
      })
      .then((data) => {
        setTool(data.tools[0] ?? null)
        setConnected(true)
      })
      .catch(() => setConnected(false))
  }, [])

  async function searchCustomer(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setResult(null)

    if (!/^\d+$/.test(customerId)) {
      setError('Enter a numeric customer ID to continue.')
      return
    }

    setLoading(true)
    try {
      const response = await fetch(`${apiUrl}/mcp/tools/call`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: 'get_customer_history', arguments: { customerId: Number(customerId) } }),
      })
      const data = (await response.json()) as CallResponse
      if (!response.ok || data.isError) throw new Error(data.content?.[0]?.text ?? 'The gateway rejected this request.')
      setResult(data.content?.[0]?.text ?? 'No customer context returned.')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to reach the gateway.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="min-h-screen overflow-hidden bg-paper text-ink">
      <div className="mx-auto min-h-screen max-w-[1440px] px-5 py-5 sm:px-8 lg:px-12">
        <header className="flex items-center justify-between border-b border-ink/15 pb-5">
          <div className="flex items-center gap-3">
            <div className="grid size-10 place-items-center rounded-full bg-ink text-mint"><Terminal size={19} /></div>
            <div><p className="font-display text-lg font-bold tracking-tight">Grounding Desk</p><p className="font-mono text-[10px] uppercase tracking-[0.2em] text-moss">Enterprise AI Gateway</p></div>
          </div>
          <div className="flex items-center gap-2 rounded-full border border-ink/15 bg-white/40 px-3 py-2 font-mono text-[10px] uppercase tracking-[0.14em]"><span className={`size-2 rounded-full ${connected ? 'bg-emerald-600' : 'bg-coral'}`} />{connected ? 'Gateway online' : 'Gateway offline'}</div>
        </header>

        <section className="grid gap-12 pb-16 pt-14 lg:grid-cols-[1.05fr_0.95fr] lg:items-end lg:pt-24">
          <div><p className="mb-5 font-mono text-xs uppercase tracking-[0.25em] text-coral">MCP / Customer context</p><h1 className="max-w-3xl font-display text-5xl font-bold leading-[0.95] tracking-[-0.06em] sm:text-7xl lg:text-[6.7rem]">Ask the <span className="text-moss">source.</span></h1><p className="mt-7 max-w-xl text-base leading-7 text-moss sm:text-lg">Retrieve governed customer context through the gateway. Sensitive contact fields are masked before they reach the model boundary.</p></div>
          <div className="relative border-l border-ink/15 pl-6 lg:mb-2"><div className="absolute -left-[5px] top-0 size-2.5 rounded-full bg-coral" /><p className="font-mono text-[10px] uppercase tracking-[0.2em] text-moss">Active tool</p><h2 className="mt-3 font-display text-2xl font-bold">{tool?.name ?? 'get_customer_history'}</h2><p className="mt-2 max-w-sm text-sm leading-6 text-moss">{tool?.description ?? 'Loading tool definition from gateway...'}</p></div>
        </section>

        <section className="grid gap-5 border-t border-ink/15 py-7 lg:grid-cols-[0.75fr_1.25fr]"><div className="flex items-center gap-3 font-mono text-[10px] uppercase tracking-[0.16em] text-moss"><Database size={15} /><span>Customer lookup</span><ArrowUpRight size={14} className="ml-auto" /></div><form onSubmit={searchCustomer} className="flex flex-col gap-3 sm:flex-row"><label className="sr-only" htmlFor="customer-id">Customer ID</label><input id="customer-id" value={customerId} onChange={(event) => setCustomerId(event.target.value)} placeholder="Enter customer ID" inputMode="numeric" className="min-h-14 flex-1 border-b-2 border-ink bg-transparent px-1 font-mono text-lg outline-none placeholder:text-moss/50 focus:border-coral" /><button type="submit" disabled={loading || !connected} className="inline-flex min-h-14 items-center justify-center gap-2 bg-ink px-6 font-mono text-xs uppercase tracking-[0.15em] text-paper transition hover:bg-moss disabled:cursor-not-allowed disabled:opacity-40">{loading ? <LoaderCircle size={17} className="animate-spin" /> : <Search size={17} />}Query source</button></form></section>

        <section className="grid gap-5 pb-16 lg:grid-cols-[0.75fr_1.25fr]"><div className="flex gap-3 border-t border-ink/15 pt-5 text-sm text-moss"><ShieldCheck size={18} className="mt-0.5 shrink-0 text-coral" /><p>Governance boundary active. Email and phone values are redacted in the returned context.</p></div><div className="min-h-44 border border-ink/15 bg-white/35 p-5">{error ? <div className="flex items-start gap-3 text-coral"><X size={18} className="mt-0.5" /><p>{error}</p></div> : result ? <div><div className="mb-4 flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.16em] text-emerald-700"><Check size={15} />Source response</div><p className="whitespace-pre-wrap font-mono text-sm leading-7 text-ink">{result}</p></div> : <div className="grid min-h-32 place-items-center text-center font-mono text-xs uppercase tracking-[0.15em] text-moss/60">Response will appear here</div>}</div></section>

        <footer className="flex flex-col gap-3 border-t border-ink/15 py-5 font-mono text-[10px] uppercase tracking-[0.16em] text-moss sm:flex-row sm:justify-between"><span>Secure customer grounding</span><span>Gateway protocol / MCP</span></footer>
      </div>
    </main>
  )
}

export default App
