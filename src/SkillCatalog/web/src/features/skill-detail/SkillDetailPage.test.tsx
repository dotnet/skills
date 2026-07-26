import '../../test/setup'
import { expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { SkillDetailPage } from './SkillDetailPage'
it('renders metadata, sanitized instructions, resources, and download action',async()=>{vi.spyOn(globalThis,'fetch').mockResolvedValue(new Response(JSON.stringify({plugin:'dotnet',name:'sample',description:'Sample skill',license:'MIT',markdown:'# Instructions\n<script>bad()</script>',resources:[{path:'note.txt',size:12,kind:'text',previewable:true}],sourceUrl:'https://example.test/source',diagnostics:[]}),{status:200,headers:{'Content-Type':'application/json'}}));render(<MemoryRouter initialEntries={['/skills/dotnet/sample']}><Routes><Route path="/skills/:plugin/:skill" element={<SkillDetailPage/>}/></Routes></MemoryRouter>);expect(await screen.findByRole('heading',{name:'sample'})).toBeVisible();expect(screen.getByText('note.txt')).toBeVisible();expect(screen.getByRole('link',{name:/download skill/i})).toBeVisible();expect(document.querySelector('.markdown script')).toBeNull()})
