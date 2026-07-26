import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Badge, Button, Spinner, Text, Title2 } from '@fluentui/react-components'
import { ArrowLeftRegular, OpenRegular } from '@fluentui/react-icons'
import { SkillMarkdown } from './components/SkillMarkdown'
import { ResourceList } from './components/ResourceList'
import { DownloadSkillButton } from './components/DownloadSkillButton'
import { catalogClient } from '../../api/skillCatalogClient'
import type { SkillDetail } from '../../api/models'

export function SkillDetailPage(){const {plugin='',skill=''}=useParams();const [item,setItem]=useState<SkillDetail>();const [error,setError]=useState('');useEffect(()=>{catalogClient.skill(plugin,skill).then(setItem).catch(e=>setError(e.message))},[plugin,skill]);if(error)return <main id="main" className="page detail state"><h1 tabIndex={-1}>Skill not found</h1><p>{error}</p><Link to="/">Return to catalog</Link></main>;if(!item)return <main id="main" className="page detail state"><Spinner label="Loading skill"/></main>;return <main id="main" className="page detail"><Link className="back" to="/"><ArrowLeftRegular/> All skills</Link><div className="detail-head"><div><Badge appearance="tint">{item.plugin}</Badge><h1 tabIndex={-1}>{item.name}</h1><Text size={500}>{item.description}</Text></div><div className="actions"><DownloadSkillButton plugin={item.plugin} skill={item.name}/><Button as="a" href={item.sourceUrl} target="_blank" icon={<OpenRegular/>}>View source</Button></div></div><div className="detail-layout"><SkillMarkdown markdown={item.markdown}/><aside><Title2>Skill details</Title2><dl><dt>Collection</dt><dd>{item.plugin}</dd><dt>License</dt><dd>{item.license??'See repository'}</dd><dt>Resources</dt><dd>{item.resources.length}</dd></dl><ResourceList resources={item.resources}/></aside></div></main>}
