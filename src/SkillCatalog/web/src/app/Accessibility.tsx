import { useEffect, useRef } from 'react'
import { useLocation } from 'react-router-dom'
export function Accessibility() { const location = useLocation(); const previousPath=useRef(location.pathname); useEffect(() => { if(previousPath.current!==location.pathname){previousPath.current=location.pathname;document.querySelector<HTMLElement>('main h1')?.focus()} }, [location.pathname]); return <a className="skip-link" href="#main">Skip to content</a> }
