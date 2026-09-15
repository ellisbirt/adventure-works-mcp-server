import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const response = (body: unknown) =>
  Promise.resolve(new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } }))

const rpcResult = (id: number, result: unknown) => response({ jsonrpc: '2.0', id, result })

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('App', () => {
  it('loads safe catalog tables into the table selector', async () => {
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => rpcResult(1, { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'test', version: '1' } }))
      .mockImplementationOnce(() => rpcResult(2, { tools: [{ name: 'list_database_tables', description: 'Lists safe tables.', inputSchema: {} }] }))
      .mockImplementationOnce(() => rpcResult(3, { content: [{ type: 'text', text: '[{"schema":"SalesLT","name":"Product","columns":["ProductID","Name"]}]' }], isError: false }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    expect(await screen.findByTestId('table-option-SalesLT-Product')).toHaveTextContent('SalesLT.Product (2 safe columns)')
    expect(screen.getByTestId('catalog-table-count')).toHaveTextContent('1 safe tables')
  })

  it('reads a selected table with the requested row limit', async () => {
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => rpcResult(1, { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'test', version: '1' } }))
      .mockImplementationOnce(() => rpcResult(2, { tools: [{ name: 'list_database_tables', description: 'Lists safe tables.', inputSchema: {} }] }))
      .mockImplementationOnce(() => rpcResult(3, { content: [{ type: 'text', text: '[{"schema":"SalesLT","name":"Product","columns":["ProductID","Name"]}]' }], isError: false }))
      .mockImplementationOnce(() => rpcResult(4, { content: [{ type: 'text', text: '[{"ProductID":1,"Name":"Road Bike"}]' }], isError: false }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)
    await screen.findByTestId('table-option-SalesLT-Product')
    fireEvent.change(screen.getByTestId('row-limit-input'), { target: { value: '2' } })
    fireEvent.click(screen.getByTestId('read-rows-button'))

    expect(await screen.findByTestId('source-response')).toHaveTextContent('Road Bike')
    expect(JSON.parse(fetchMock.mock.calls[3][1]?.body as string)).toMatchObject({
      method: 'tools/call',
      params: { name: 'read_database_table', arguments: { schema: 'SalesLT', table: 'Product', limit: 2 } },
    })
  })

  it('rejects row limits outside the governed range', async () => {
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => rpcResult(1, { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'test', version: '1' } }))
      .mockImplementationOnce(() => rpcResult(2, { tools: [{ name: 'list_database_tables', description: 'Lists safe tables.', inputSchema: {} }] }))
      .mockImplementationOnce(() => rpcResult(3, { content: [{ type: 'text', text: '[{"schema":"SalesLT","name":"Product","columns":["ProductID"]}]' }], isError: false }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)
    await screen.findByTestId('table-option-SalesLT-Product')
    fireEvent.change(screen.getByTestId('row-limit-input'), { target: { value: '101' } })
    fireEvent.click(screen.getByTestId('read-rows-button'))

    await waitFor(() => expect(screen.getByTestId('gateway-error')).toHaveTextContent('Choose a table and a row limit from 1 through 100.'))
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('sends questions to the MCP-backed chat endpoint', async () => {
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => rpcResult(1, { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'test', version: '1' } }))
      .mockImplementationOnce(() => rpcResult(2, { tools: [{ name: 'list_database_tables', description: 'Lists safe tables.', inputSchema: {} }] }))
      .mockImplementationOnce(() => rpcResult(3, { content: [{ type: 'text', text: '[]' }], isError: false }))
      .mockImplementationOnce(() => response({ message: 'Road bikes are in the product catalog.', tool: 'read_database_table' }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)
    await waitFor(() => expect(screen.getByTestId('chat-send-button')).toBeEnabled())
    fireEvent.change(screen.getByTestId('chat-message-input'), { target: { value: 'Tell me about road bikes' } })
    fireEvent.click(screen.getByTestId('chat-send-button'))

    expect(await screen.findByTestId('chat-response')).toHaveTextContent('Road bikes are in the product catalog.')
    expect(JSON.parse(fetchMock.mock.calls[3][1]?.body as string)).toEqual({ message: 'Tell me about road bikes' })
  })
})