import '../test/setup'
import { expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { Accessibility } from './Accessibility'
it('provides keyboard skip navigation with a labeled target',()=>{render(<MemoryRouter><Accessibility/><main id="main"><h1>Catalog</h1></main></MemoryRouter>);const skip=screen.getByRole('link',{name:/skip to content/i});expect(skip).toHaveAttribute('href','#main')})
