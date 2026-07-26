import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
Object.defineProperty(globalThis, 'NodeFilter', { value: window.NodeFilter, configurable: true })
afterEach(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
