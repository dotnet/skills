import { Button } from '@fluentui/react-components'
import { ArrowDownloadRegular } from '@fluentui/react-icons'
import { catalogClient } from '../../../api/skillCatalogClient'
export function DownloadSkillButton({ plugin, skill }: { plugin:string; skill:string }) { return <Button as="a" href={catalogClient.downloadUrl(plugin,skill)} appearance="primary" icon={<ArrowDownloadRegular/>}>Download skill</Button> }
