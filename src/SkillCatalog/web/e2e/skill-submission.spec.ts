import {expect,test} from '@playwright/test'

const valid=`---
name: browser-upload
description: A valid browser upload scenario.
---
# browser-upload
## Workflow
1. Inspect the request.
2. Return the result.
## Validation
Confirm the result.
`
test('uploads and validates SKILL.md',async({page})=>{
 await page.goto('/contribute/skill')
 await page.locator('input[type=file]').setInputFiles({name:'SKILL.md',mimeType:'text/markdown',buffer:Buffer.from(valid)})
 await expect(page.getByRole('heading',{name:'browser-upload'})).toBeVisible({timeout:15_000})
 await expect(page.getByRole('button',{name:/download normalized/i})).toBeEnabled()
})
test('replacing an upload replaces its result',async({page})=>{
 await page.goto('/contribute/skill')
 const input=page.locator('input[type=file]')
 await input.setInputFiles({name:'SKILL.md',mimeType:'text/markdown',buffer:Buffer.from(valid)})
 await expect(page.getByRole('heading',{name:'browser-upload'})).toBeVisible({timeout:15_000})
 await input.setInputFiles({name:'SKILL.md',mimeType:'text/markdown',buffer:Buffer.from('not valid')})
 await expect(page.getByText('Needs correction')).toBeVisible({timeout:15_000})
 await expect(page.getByRole('heading',{name:'browser-upload'})).toHaveCount(0)
})
