import { useEffect, useState } from 'react'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { ArrowUpRight, Check, Database, LoaderCircle, MessageCircle, Search, Send, ShieldCheck, Table2, Terminal, X } from 'lucide-react'
import { apiScope, authenticationEnabled } from './auth'
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

type McpRpcResponse<T> = {
  result?: T
  error?: { message: string }
}

type McpInitializeResult = {
  protocolVersion: string
  capabilities: { tools?: Record<string, unknown> }
  serverInfo: { name: string; version: string }
}

type TableDefinition = {
  schema: string
  name: string
  columns: string[]
}

function parseTableCatalog(value: unknown): TableDefinition[] {
  if (!Array.isArray(value)) return []

  return value.flatMap((item) => {
    if (!item || typeof item !== 'object') return []
    const table = item as Record<string, unknown>
    const schema = table.schema ?? table.Schema
    const name = table.name ?? table.Name
    const columns = table.columns ?? table.Columns
    if (typeof schema !== 'string' || typeof name !== 'string') return []

    return [{
      schema,
      name,
      columns: Array.isArray(columns) ? columns.filter((column): column is string => typeof column === 'string') : [],
    }]
  })
}

type ChatResponse = {
  message: string
  tool: string
}

type ErrorResponse = {
  error?: string
}

const apiUrl = import.meta.env.VITE_GATEWAY_URL
  ? `${import.meta.env.VITE_GATEWAY_URL.replace(/\/$/, '')}/api/v1`
  : '/api/v1'

function correlationId() {
  return crypto.randomUUID()
}

let nextMcpRequestId = 1

async function readJson<T>(response: Response): Promise<T | null> {
  const body = await response.text()
  if (!body) return null

  try {
    return JSON.parse(body) as T
  } catch {
    return null
  }
}

function gatewayStatusMessage(status: number): string {
  switch (status) {
    case 401:
      return 'Authentication is required. Sign in and try again.'
    case 403:
      return 'Your account is missing the required API scope.'
    case 503:
      return 'The database assistant is temporarily unavailable. Try again shortly.'
    case 429:
      return 'Too many requests. Wait a minute and try again.'
    default:
      return `Gateway rejected the request (${status}).`
  }
}

function responseErrorMessage(data: unknown): string | undefined {
  if (!data || typeof data !== 'object' || !('error' in data)) return undefined

  const error = (data as { error?: unknown }).error
  if (typeof error === 'string') return error
  if (error && typeof error === 'object' && 'message' in error && typeof (error as { message?: unknown }).message === 'string') {
    return (error as { message: string }).message
  }

  return undefined
}

function formatNetworkError(error: unknown, fallback: string): string {
  if (error instanceof TypeError && error.message.toLowerCase().includes('fetch')) {
    return `Unable to reach the gateway at ${apiUrl}. Check network access and CORS origin configuration.`
  }

  return error instanceof Error ? error.message : fallback
}

function App() {
  const { accounts, instance } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const [tables, setTables] = useState<TableDefinition[]>([])
  const [selectedTable, setSelectedTable] = useState('')
  const [rowLimit, setRowLimit] = useState('20')
  const [tool, setTool] = useState<ToolResponse['tools'][number] | null>(null)
  const [result, setResult] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [connected, setConnected] = useState(false)
  const [chatMessage, setChatMessage] = useState('')
  const [chatResponse, setChatResponse] = useState<ChatResponse | null>(null)
  const [chatError, setChatError] = useState<string | null>(null)
  const [chatLoading, setChatLoading] = useState(false)

  async function apiHeaders() {
    const headers: Record<string, string> = { 'X-Correlation-ID': correlationId() }
    if (!authenticationEnabled) return headers

    const account = instance.getActiveAccount() ?? accounts[0]
    if (!account) throw new Error('Sign in to access the gateway.')
    const token = await instance.acquireTokenSilent({ account, scopes: [apiScope] })
    headers.Authorization = `Bearer ${token.accessToken}`
    return headers
  }

  async function mcpRequest<T>(method: string, params: Record<string, unknown> = {}) {
    let response: Response
    try {
      response = await fetch(`${apiUrl}/mcp`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(await apiHeaders()) },
        body: JSON.stringify({ jsonrpc: '2.0', id: nextMcpRequestId++, method, params }),
      })
    } catch (networkError) {
      throw new Error(formatNetworkError(networkError, 'Unable to reach the gateway.'))
    }

    const data = await readJson<McpRpcResponse<T> | ErrorResponse>(response)
    if (!response.ok) {
      throw new Error(responseErrorMessage(data) ?? gatewayStatusMessage(response.status))
    }
    if (!data) throw new Error('Gateway returned an empty response.')
    if ('error' in data && data.error && typeof data.error !== 'string') throw new Error(data.error.message)
    if (!('result' in data)) throw new Error('Gateway returned an invalid MCP result envelope.')
    if (data.result === undefined) throw new Error('Gateway returned an empty MCP result.')
    return data.result
  }

  function signIn() {
    void instance.loginRedirect({ scopes: [apiScope] })
  }

  function signOut() {
    void instance.logoutRedirect({ account: instance.getActiveAccount() ?? accounts[0] })
  }

  useEffect(() => {
    if (authenticationEnabled && !isAuthenticated) return

    async function loadCatalog() {
      try {
        setError(null)
        await mcpRequest<McpInitializeResult>('initialize', {
          protocolVersion: '2025-06-18',
          capabilities: {},
          clientInfo: { name: 'grounding-desk', version: '1.0.0' },
        })
        const data = await mcpRequest<ToolResponse>('tools/list')
        setTool(data.tools[0] ?? null)
        setConnected(true)

        const catalogData = await mcpRequest<CallResponse>('tools/call', { name: 'list_database_tables', arguments: {} })
        const catalog = parseTableCatalog(JSON.parse(catalogData.content?.[0]?.text ?? '[]'))
        setTables(catalog)
        setSelectedTable(catalog[0] ? `${catalog[0].schema}.${catalog[0].name}` : '')
      } catch (loadError) {
        setConnected(false)
        setError(formatNetworkError(loadError, 'Unable to initialize the gateway catalog.'))
      }
    }

    void loadCatalog()
  }, [accounts, instance, isAuthenticated])

  async function readTable(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setResult(null)

    const [schema, table] = selectedTable.split('.')
    const limit = Number(rowLimit)
    if (!schema || !table || !Number.isInteger(limit) || limit < 1 || limit > 100) {
      setError('Choose a table and a row limit from 1 through 100.')
      return
    }

    setLoading(true)
    try {
      const data = await mcpRequest<CallResponse>('tools/call', { name: 'read_database_table', arguments: { schema, table, limit } })
      if (!data || data.isError) throw new Error(data?.content?.[0]?.text ?? 'The gateway rejected this request.')
      setResult(data.content?.[0]?.text ?? 'No customer context returned.')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to reach the gateway.')
    } finally {
      setLoading(false)
    }
  }

  async function sendChatMessage(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setChatError(null)
    setChatResponse(null)
    if (!chatMessage.trim()) {
      setChatError('Enter a question for the database assistant.')
      return
    }

    setChatLoading(true)
    try {
      const response = await fetch(`${apiUrl}/chat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(await apiHeaders()) },
        body: JSON.stringify({ message: chatMessage.trim() }),
      })
      const data = await readJson<ChatResponse | ErrorResponse>(response)
      if (!response.ok || !data || !('message' in data)) {
        throw new Error(responseErrorMessage(data) ?? gatewayStatusMessage(response.status))
      }
      setChatResponse(data)
    } catch (requestError) {
      setChatError(formatNetworkError(requestError, 'Unable to reach the database assistant.'))
    } finally {
      setChatLoading(false)
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
          <div className="flex items-center gap-2"><div className="rounded-full border border-ink/15 bg-white/40 px-3 py-2 font-mono text-[10px] uppercase tracking-[0.14em]"><span className={`size-2 rounded-full ${connected ? 'bg-emerald-600' : 'bg-coral'}`} />{connected ? 'Gateway online' : 'Gateway offline'}</div>{authenticationEnabled && (isAuthenticated ? <button data-testid="sign-out-button" type="button" onClick={signOut} className="min-h-9 border border-ink px-3 font-mono text-[10px] uppercase tracking-[0.14em]">Sign out</button> : <button data-testid="sign-in-button" type="button" onClick={signIn} className="min-h-9 bg-ink px-3 font-mono text-[10px] uppercase tracking-[0.14em] text-paper">Sign in</button>)}</div>
        </header>

        <section className="grid gap-12 pb-16 pt-14 lg:grid-cols-[1.05fr_0.95fr] lg:items-end lg:pt-24">
          <div><p className="mb-5 font-mono text-xs uppercase tracking-[0.25em] text-coral">MCP / Governed data catalog</p><h1 className="max-w-3xl font-display text-5xl font-bold leading-[0.95] tracking-[-0.06em] sm:text-7xl lg:text-[6.7rem]">Ask the <span className="text-moss">source.</span></h1><p className="mt-7 max-w-xl text-base leading-7 text-moss">Browse every available database table through the gateway. Personal, contact, and location fields are redacted and marked before data reaches the model boundary.</p></div>
          <div className="relative border-l border-ink/15 pl-6 lg:mb-2"><div className="absolute -left-[5px] top-0 size-2.5 rounded-full bg-coral" /><p className="font-mono text-[10px] uppercase tracking-[0.2em] text-moss">Catalog</p><h2 data-testid="catalog-table-count" className="mt-3 font-display text-2xl font-bold">{tables.length} safe tables</h2><p className="mt-2 max-w-sm text-sm leading-6 text-moss">{tool?.description ?? 'Loading gateway tool definitions...'}</p></div>
        </section>

        <section className="grid gap-5 border-t border-ink/15 py-7 lg:grid-cols-[0.75fr_1.25fr]"><div className="flex items-center gap-3 font-mono text-[10px] uppercase tracking-[0.16em] text-moss"><Database size={15} /><span>Table browser</span><ArrowUpRight size={14} className="ml-auto" /></div><form onSubmit={readTable} className="flex flex-col gap-3 sm:flex-row"><label className="sr-only" htmlFor="table">Database table</label><select data-testid="table-selector" id="table" value={selectedTable} onChange={(event) => setSelectedTable(event.target.value)} disabled={!tables.length} className="min-h-14 flex-1 border-b-2 border-ink bg-transparent px-1 font-mono text-sm outline-none focus:border-coral"><option value="">Select a table</option>{tables.map((item) => <option data-testid={`table-option-${item.schema}-${item.name}`} key={`${item.schema}.${item.name}`} value={`${item.schema}.${item.name}`}>{item.schema}.{item.name} ({item.columns.length} safe columns)</option>)}</select><label className="sr-only" htmlFor="row-limit">Rows</label><input data-testid="row-limit-input" id="row-limit" value={rowLimit} onChange={(event) => setRowLimit(event.target.value)} inputMode="numeric" className="min-h-14 w-24 border-b-2 border-ink bg-transparent px-1 font-mono text-sm outline-none focus:border-coral" /><button data-testid="read-rows-button" type="submit" disabled={loading || !connected || !selectedTable} className="inline-flex min-h-14 items-center justify-center gap-2 bg-ink px-6 font-mono text-xs uppercase tracking-[0.15em] text-paper transition hover:bg-moss disabled:cursor-not-allowed disabled:opacity-40">{loading ? <LoaderCircle size={17} className="animate-spin" /> : <Search size={17} />}Read rows</button></form></section>

        <section className="grid gap-5 pb-16 lg:grid-cols-[0.75fr_1.25fr]"><div className="flex gap-3 border-t border-ink/15 pt-5 text-sm text-moss"><ShieldCheck size={18} className="mt-0.5 shrink-0 text-coral" /><p>Governance boundary active. Credential fields are never returned; personal, contact, and location fields are redacted and marked.</p></div><div className="min-h-44 border border-ink/15 bg-white/35 p-5">{error ? <div data-testid="gateway-error" className="flex items-start gap-3 text-coral"><X size={18} className="mt-0.5" /><p>{error}</p></div> : result ? <div data-testid="source-response"><div className="mb-4 flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.16em] text-emerald-700"><Check size={15} />Source response</div><p className="whitespace-pre-wrap break-words font-mono text-sm leading-7 text-ink">{result}</p></div> : <div className="grid min-h-32 place-items-center text-center font-mono text-xs uppercase tracking-[0.15em] text-moss/60"><Table2 size={16} className="mr-2" />Select a table to inspect its safe rows</div>}</div></section>

        <section className="grid gap-5 border-t border-ink/15 py-10 lg:grid-cols-[0.75fr_1.25fr]"><div className="flex gap-3 pt-1 text-sm text-moss"><MessageCircle size={18} className="mt-0.5 shrink-0 text-coral" /><p>Ask in plain language. The assistant selects only from the governed MCP catalog and returns an answer grounded in its safe tool result.</p></div><div className="border border-ink/15 bg-white/35 p-5"><form onSubmit={sendChatMessage} className="flex flex-col gap-3 sm:flex-row"><label className="sr-only" htmlFor="chat-message">Question for database assistant</label><input data-testid="chat-message-input" id="chat-message" value={chatMessage} onChange={(event) => setChatMessage(event.target.value)} placeholder="Ask about products, categories, or orders" className="min-h-14 flex-1 border-b-2 border-ink bg-transparent px-1 font-mono text-sm outline-none placeholder:text-moss/50 focus:border-coral" /><button data-testid="chat-send-button" type="submit" disabled={chatLoading || !connected} className="inline-flex min-h-14 items-center justify-center gap-2 bg-coral px-6 font-mono text-xs uppercase tracking-[0.15em] text-ink transition hover:bg-ink hover:text-paper disabled:cursor-not-allowed disabled:opacity-40">{chatLoading ? <LoaderCircle size={17} className="animate-spin" /> : <Send size={17} />}Ask assistant</button></form>{chatError ? <div data-testid="chat-error" className="mt-5 text-sm text-coral">{chatError}</div> : chatResponse ? <div data-testid="chat-response" className="mt-5 border-t border-ink/15 pt-4"><p className="mb-2 font-mono text-[10px] uppercase tracking-[0.16em] text-emerald-700">MCP / {chatResponse.tool}</p><p className="whitespace-pre-wrap break-words text-sm leading-7 text-ink">{chatResponse.message}</p></div> : null}</div></section>

        <footer className="flex flex-col gap-3 border-t border-ink/15 py-5 font-mono text-[10px] uppercase tracking-[0.16em] text-moss sm:flex-row sm:justify-between"><span>Secure database grounding</span><span>Gateway protocol / MCP</span></footer>
      </div>
    </main>
  )
}

export default App
