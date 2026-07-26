export type Diagnostic = { severity: string; message: string; plugin?: string; skill?: string }
export type Catalog = { pluginCount: number; skillCount: number; revision: string; refreshedAt: string; plugins: string[]; diagnostics: Diagnostic[] }
export type SkillSummary = { plugin: string; name: string; description: string; license?: string; url: string }
export type PagedSkills = { items: SkillSummary[]; total: number; page: number; pageSize: number }
export type SkillResource = { path: string; size: number; kind: string; previewable: boolean }
export type SkillDetail = SkillSummary & { markdown: string; resources: SkillResource[]; sourceUrl: string; diagnostics: Diagnostic[] }
