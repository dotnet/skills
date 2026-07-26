import { expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SkillMarkdown } from './SkillMarkdown'
it('sanitizes executable markdown markup',()=>{const {container}=render(<SkillMarkdown markdown={'# Safe\n<script>alert(1)</script>'}/>);expect(screen.getByRole('heading',{name:'Safe'})).toBeVisible();expect(container.querySelector('script')).toBeNull()})
