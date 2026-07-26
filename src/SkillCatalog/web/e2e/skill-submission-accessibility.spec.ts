import AxeBuilder from '@axe-core/playwright'
import {expect,test} from '@playwright/test'

test('upload workspace is keyboard accessible and responsive',async({page})=>{
 await page.goto('/contribute/skill')
 await page.keyboard.press('Tab')
 await expect(page.getByRole('link',{name:/skip to content/i})).toBeFocused()
 await page.getByRole('button',{name:/choose file/i}).focus()
 await expect(page.getByRole('button',{name:/choose file/i})).toBeFocused()
 const results=await new AxeBuilder({page}).analyze()
 expect(results.violations.filter(x=>['serious','critical'].includes(x.impact??''))).toEqual([])
 await page.setViewportSize({width:390,height:844})
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true)
})
