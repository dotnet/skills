import '../../test/setup'
import '@testing-library/jest-dom/vitest'
import {afterEach,expect,it,vi} from 'vitest'
import {fireEvent,render,screen} from '@testing-library/react'
import {SkillSubmissionPage} from './SkillSubmissionPage'

afterEach(()=>vi.restoreAllMocks())
it('uploads a skill and displays validation results',async()=>{
 vi.spyOn(globalThis,'fetch').mockResolvedValue(new Response(JSON.stringify({uploadRevision:'abc',valid:true,findings:[],preview:{plugin:'dotnet',name:'uploaded-skill',description:'Valid',markdown:'# uploaded-skill',disposition:'new',entries:[{path:'SKILL.md',size:10,kind:'skill'}],evaluationCount:0,ownershipCovered:false},packageManifest:[{path:'SKILL.md',size:10}]}),{status:200,headers:{'content-type':'application/json'}}))
 render(<SkillSubmissionPage/>)
 const input=document.querySelector('input[type=file]') as HTMLInputElement
 fireEvent.change(input,{target:{files:[new File(['skill'],'SKILL.md',{type:'text/markdown'})]}})
 expect(await screen.findByText('uploaded-skill')).toBeInTheDocument()
 expect(screen.getByRole('button',{name:/download normalized/i})).toBeEnabled()
})
it('replaces stale results while another upload validates',async()=>{
 let resolveFetch:(value:Response)=>void=()=>{}
 vi.spyOn(globalThis,'fetch').mockImplementation(()=>new Promise(resolve=>{resolveFetch=resolve}))
 render(<SkillSubmissionPage/>)
 const input=document.querySelector('input[type=file]') as HTMLInputElement
 fireEvent.change(input,{target:{files:[new File(['one'],'one.md')]}})
 expect(await screen.findByText('Inspecting package')).toBeInTheDocument()
 fireEvent.change(input,{target:{files:[new File(['two'],'two.md')]}})
 resolveFetch(new Response(JSON.stringify({uploadRevision:'two',valid:false,findings:[],preview:{name:'two',markdown:'',disposition:'new',entries:[],evaluationCount:0,ownershipCovered:false},packageManifest:[]}),{status:200,headers:{'content-type':'application/json'}}))
 expect(await screen.findByText('two')).toBeInTheDocument()
})
