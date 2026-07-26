import {Button,Text} from '@fluentui/react-components'
import {ArrowUploadRegular,DocumentRegular} from '@fluentui/react-icons'
import {useRef,useState} from 'react'

export function PackageDropzone({file,busy,onSelect}:{file?:File;busy:boolean;onSelect:(file:File)=>void}){
 const input=useRef<HTMLInputElement>(null);const [over,setOver]=useState(false)
 const pick=(files:FileList|null)=>{const next=files?.[0];if(next)onSelect(next)}
 return <section className={`package-dropzone ${over?'is-over':''}`} onDragOver={e=>{e.preventDefault();setOver(true)}} onDragLeave={()=>setOver(false)} onDrop={e=>{e.preventDefault();setOver(false);pick(e.dataTransfer.files)}}>
  <DocumentRegular fontSize={46}/><h2>{file?'Package selected':'Upload an existing skill'}</h2>
  <Text>{file?`${file.name} • ${format(file.size)}`:'Drop one repository-shaped ZIP or SKILL.md here.'}</Text>
  <input ref={input} type="file" accept=".zip,.md,text/markdown,application/zip" hidden onChange={e=>pick(e.target.files)}/>
  <Button appearance="primary" icon={<ArrowUploadRegular/>} disabled={busy} onClick={()=>input.current?.click()}>{file?'Choose another file':'Choose file'}</Button>
  <Text size={200}>Files are validated without executing their contents and are not retained by the server.</Text>
 </section>
}
function format(bytes:number){return bytes<1024?`${bytes} B`:bytes<1048576?`${(bytes/1024).toFixed(1)} KB`:`${(bytes/1048576).toFixed(1)} MB`}
