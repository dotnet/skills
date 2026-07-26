import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CatalogPage } from './CatalogPage'

describe('CatalogPage', () => {
  afterEach(() => vi.restoreAllMocks())
  it('renders repository skills and the snapshot count', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url=String(input)
      const body=url.includes('/api/catalog')
        ? { pluginCount:1, skillCount:1, refreshedAt:'2026-07-26T00:00:00Z', plugins:['dotnet'], diagnostics:[] }
        : { items:[{plugin:'dotnet',name:'build-dotnet',description:'Build a .NET project',license:'MIT',url:'/skills/dotnet/build-dotnet'}],total:1,page:1,pageSize:24 }
      return new Response(JSON.stringify(body),{status:200,headers:{'Content-Type':'application/json'}})
    })
    render(<MemoryRouter><CatalogPage/></MemoryRouter>)
    expect(await screen.findByText('build-dotnet')).toBeInTheDocument()
    expect(screen.getByText('1 skills')).toBeInTheDocument()
    expect(screen.getByRole('combobox',{name:'Filter by plugin'})).toHaveValue('')
    expect(await screen.findByRole('option',{name:'dotnet'})).toBeInTheDocument()
  })
})
