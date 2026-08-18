import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'
const nodeFilter = window.NodeFilter ?? { SHOW_ELEMENT: 1, FILTER_ACCEPT: 1, FILTER_REJECT: 2, FILTER_SKIP: 3 }
Object.defineProperty(globalThis, 'NodeFilter', { value: nodeFilter, configurable: true })
Object.defineProperty(window, 'NodeFilter', { value: nodeFilter, configurable: true })
class ResizeObserverStub { observe() {} unobserve() {} disconnect() {} }
Object.defineProperty(globalThis, 'ResizeObserver', { value: ResizeObserverStub, configurable: true })
Object.defineProperty(window, 'ResizeObserver', { value: ResizeObserverStub, configurable: true })
afterEach(async () => {
  cleanup()
  await new Promise(resolve => setTimeout(resolve, 0))
})