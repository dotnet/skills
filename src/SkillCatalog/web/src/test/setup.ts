import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
Object.defineProperty(globalThis, 'NodeFilter', { value: window.NodeFilter ?? { SHOW_ELEMENT: 1, FILTER_ACCEPT: 1, FILTER_REJECT: 2, FILTER_SKIP: 3 }, configurable: true })
class ResizeObserverStub { observe() {} unobserve() {} disconnect() {} }
Object.defineProperty(globalThis, 'ResizeObserver', { value: ResizeObserverStub, configurable: true })
Object.defineProperty(window, 'ResizeObserver', { value: ResizeObserverStub, configurable: true })
afterEach(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
