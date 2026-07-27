import '../../../test/setup'
import { expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { DownloadSkillButton } from './DownloadSkillButton'
it('creates an accessible download URL',()=>{render(<DownloadSkillButton plugin="dotnet" skill="setup-local-sdk"/>);expect(screen.getByRole('link',{name:/download skill/i})).toHaveAttribute('href','/api/skills/dotnet/setup-local-sdk/download')})
