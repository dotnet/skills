import type {SubmissionOptions,UploadInspection} from './submissionModels'
async function problem(response:Response):Promise<never>{let message=`Request failed (${response.status})`;try{const body=await response.json();message=body.detail??body.title??message}catch{}throw new Error(message)}
function body(file:File){const data=new FormData();data.append('file',file,file.name);return data}
export const submissionClient={
 options:async()=>{const r=await fetch('/api/submissions/options');if(!r.ok)return problem(r);return r.json() as Promise<SubmissionOptions>},
 inspect:async(file:File)=>{const r=await fetch('/api/submissions/inspect',{method:'POST',body:body(file)});if(!r.ok)return problem(r);return r.json() as Promise<UploadInspection>},
 normalize:async(file:File)=>{const r=await fetch('/api/submissions/normalize',{method:'POST',body:body(file)});if(r.status===422)return {inspection:await r.json() as UploadInspection};if(!r.ok)return problem(r);return {blob:await r.blob(),fileName:filename(r.headers.get('content-disposition'))??'skill-normalized.zip'}}
}
function filename(value:string|null){return value?.match(/filename="?([^";]+)"?/i)?.[1]}