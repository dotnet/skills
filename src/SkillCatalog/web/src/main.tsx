import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './app/App'
import './styles/responsive.css'
import './styles/submission.css'
createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>)
