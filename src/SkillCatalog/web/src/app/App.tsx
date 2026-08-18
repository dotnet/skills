import { useEffect, useState } from 'react'
import { FluentProvider, Button, Text } from '@fluentui/react-components'
import { DarkThemeRegular, WeatherSunnyRegular } from '@fluentui/react-icons'
import { BrowserRouter, Link, Route, Routes } from 'react-router-dom'
import { lightTheme, darkTheme } from './theme'
import { Accessibility } from './Accessibility'
import { CatalogPage } from '../features/catalog/CatalogPage'
import { SkillDetailPage } from '../features/skill-detail/SkillDetailPage'
import { SkillSubmissionPage } from '../features/skill-submission/SkillSubmissionPage'
import { ContributionStatusPage } from '../features/github-submission/ContributionStatusPage'

export default function App(){const [dark,setDark]=useState(()=>localStorage.getItem('theme')==='dark');useEffect(()=>localStorage.setItem('theme',dark?'dark':'light'),[dark]);return <FluentProvider theme={dark?darkTheme:lightTheme} className="app"><BrowserRouter><Accessibility/><header className="site-header"><Link className="brand" to="/"><span className="brand-mark">S</span><span><strong>Skill Catalog</strong><Text block size={200}>Build better with agents</Text></span></Link><nav aria-label="Primary"><Link to="/">Explore</Link><Link to="/contribute/skill">Upload skill</Link><a href="https://github.com/dotnet/skills">GitHub</a><Button appearance="subtle" aria-label="Toggle color theme" icon={dark?<WeatherSunnyRegular/>:<DarkThemeRegular/>} onClick={()=>setDark(!dark)}/></nav></header><Routes><Route path="/" element={<CatalogPage/>}/><Route path="/skills/:plugin/:skill" element={<SkillDetailPage/>}/><Route path="/contribute/skill" element={<SkillSubmissionPage/>}/><Route path="/contributions/:contributionId" element={<ContributionStatusPage/>}/></Routes><footer><strong>Skill Catalog</strong><Text>Open source skills for modern development.</Text><a href="https://github.com/dotnet/skills">Contribute on GitHub</a></footer></BrowserRouter></FluentProvider>}
