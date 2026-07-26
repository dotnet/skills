import { expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SkillMarkdown } from './SkillMarkdown'
it('sanitizes executable markdown markup',()=>{const {container}=render(<SkillMarkdown markdown={'# Safe\n<script>alert(1)</script>'}/>);expect(screen.getByRole('heading',{name:'Safe'})).toBeVisible();expect(container.querySelector('script')).toBeNull()})

it('renders GitHub-flavored Markdown tables',()=>{render(<SkillMarkdown markdown={'| Name | Required |\n| --- | --- |\n| Source | Yes |'}/>);expect(screen.getByRole('table')).toBeVisible();expect(screen.getByRole('table')).toHaveAttribute('tabindex','0');expect(screen.getByRole('columnheader',{name:'Name'})).toBeVisible();expect(screen.getByRole('cell',{name:'Source'})).toBeVisible()})
