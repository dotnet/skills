import { Text } from '@fluentui/react-components'
import type { SkillResource } from '../../../api/models'
export function ResourceList({ resources }: { resources: SkillResource[] }) { if(!resources.length)return null;return <><h3>Included files</h3><ul className="resources">{resources.map(x=><li key={x.path}><span>{x.path}</span><Text size={200}>{Math.ceil(x.size/1024)} KB</Text></li>)}</ul></> }
