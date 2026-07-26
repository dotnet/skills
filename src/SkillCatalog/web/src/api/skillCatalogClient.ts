import type { Catalog, PagedSkills, SkillDetail } from './models'
async function get<T>(path: string): Promise<T> { const response = await fetch(path); if (!response.ok) throw new Error(response.status === 404 ? 'Not found' : 'The catalog is temporarily unavailable.'); return response.json() }
export const catalogClient = {
  catalog: () => get<Catalog>('/api/catalog'),
  skills: (query: URLSearchParams) => get<PagedSkills>(`/api/skills?${query}`),
  skill: (plugin: string, skill: string) => get<SkillDetail>(`/api/skills/${encodeURIComponent(plugin)}/${encodeURIComponent(skill)}`),
  downloadUrl: (plugin: string, skill: string) => `/api/skills/${encodeURIComponent(plugin)}/${encodeURIComponent(skill)}/download`
}
