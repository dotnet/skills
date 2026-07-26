import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Badge, Button, Card, CardHeader, Dropdown, Input, Option, Spinner, Text, Title2 } from '@fluentui/react-components'
import { ArrowRightRegular, SearchRegular } from '@fluentui/react-icons'
import { catalogClient } from '../../api/skillCatalogClient'
import type { Catalog, PagedSkills } from '../../api/models'

export function CatalogPage() {
 const [params, setParams] = useSearchParams(); const [catalog, setCatalog] = useState<Catalog>(); const [result, setResult] = useState<PagedSkills>(); const [error, setError] = useState(''); const q=params.get('q')??''; const plugin=params.get('plugin')??'';
 const query=useMemo(()=>{const x=new URLSearchParams(); if(q)x.set('q',q);if(plugin)x.set('plugin',plugin);return x},[q,plugin]);
 useEffect(()=>{catalogClient.catalog().then(setCatalog).catch(e=>setError(e.message))},[])
 useEffect(()=>{setResult(undefined);catalogClient.skills(query).then(setResult).catch(e=>setError(e.message))},[query])
 const update=(key:string,value:string)=>{const next=new URLSearchParams(params); value?next.set(key,value):next.delete(key);setParams(next)}
 return <main id="main" className="page">
   <section className="hero"><Badge appearance="tint" color="brand">Open source • Agent-ready</Badge><h1 tabIndex={-1}>Find the right skill.<br/><span>Build with confidence.</span></h1><Text size={500}>Browse practical, repository-backed skills for .NET developers and AI coding agents.</Text>
   <div className="search"><Input size="large" contentBefore={<SearchRegular/>} aria-label="Search skills" placeholder="Search skills, tools, or workflows" value={q} onChange={(_,d)=>update('q',d.value)}/><Button appearance="primary" size="large">Search</Button></div></section>
   {error && <div className="state error" role="alert"><Title2>Catalog unavailable</Title2><p>{error}</p><Button onClick={()=>location.reload()}>Try again</Button></div>}
   {!error && <section className="content"><div className="toolbar"><div><Title2>Explore skills</Title2><Text block>{result ? `${result.total} skills` : 'Loading skills…'}</Text></div><Dropdown aria-label="Filter by plugin" placeholder="All collections" value={plugin || 'All collections'} selectedOptions={plugin?[plugin]:[]} onOptionSelect={(_,d)=>update('plugin',String(d.optionValue??''))}><Option value="">All collections</Option>{catalog?.plugins.map(x=><Option key={x} value={x}>{x}</Option>)}</Dropdown></div>
   {!result ? <div className="state"><Spinner label="Loading catalog"/></div> : result.items.length===0 ? <div className="state"><Title2>No skills found</Title2><p>Try a broader search or clear your filter.</p><Button onClick={()=>setParams({})}>Reset search</Button></div> : <div className="grid">{result.items.map(skill=><Card key={`${skill.plugin}/${skill.name}`} className="skill-card"><CardHeader header={<Text weight="semibold" size={500}>{skill.name}</Text>} description={<Badge appearance="tint">{skill.plugin}</Badge>}/><Text className="description">{skill.description}</Text><div className="card-footer">{skill.license && <Text size={200}>{skill.license}</Text>}<Link to={`/skills/${encodeURIComponent(skill.plugin)}/${encodeURIComponent(skill.name)}`}>View skill <ArrowRightRegular/></Link></div></Card>)}</div>}
   {catalog && <Text className="freshness" size={200}>Catalog snapshot refreshed {new Date(catalog.refreshedAt).toLocaleString()}</Text>}</section>}
 </main>
}
