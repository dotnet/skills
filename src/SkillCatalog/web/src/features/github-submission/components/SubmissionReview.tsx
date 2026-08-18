import { Badge, Button, Checkbox, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Text } from '@fluentui/react-components'
import type { SubmissionFile, SubmissionIntent } from '../../../api/githubSubmissionModels'

export function SubmissionReview({ intent, confirmed, busy, onConfirmed, onSubmit }: {
  intent: SubmissionIntent
  confirmed: boolean
  busy: boolean
  onConfirmed: (value: boolean) => void
  onSubmit: () => void
}) {
  const isUpdate = intent.contributionType === 'Update'
  const groups = (['add', 'change', 'delete'] as const)
    .map(operation => ({ operation, files: intent.files.filter(file => file.operation === operation) }))
    .filter(group => group.files.length > 0)

  return <section aria-labelledby="submission-review-title">
    <h3 id="submission-review-title">Review GitHub contribution</h3>
    <Badge appearance="tint" color={isUpdate ? 'warning' : 'success'}>{isUpdate ? 'Existing skill update' : 'New skill'}</Badge>
    <dl>
      <dt>Target</dt><dd>{intent.targetRepository}</dd>
      <dt>Type</dt><dd>{intent.contributionType}</dd>
      <dt>Destination</dt><dd><code>{intent.destinationPath}</code></dd>
      <dt>Pull request</dt><dd>{intent.pullRequestTitle}</dd>
    </dl>
    {groups.map(group => <FileOperationGroup key={group.operation} operation={group.operation} files={group.files} />)}
    <Checkbox
      checked={confirmed}
      onChange={(_, data) => onConfirmed(data.checked === true)}
      label={isUpdate
        ? 'I explicitly confirm these added, changed, and removed files at the reviewed repository revision'
        : 'I reviewed the destination and affected files'}
    />
    {isUpdate && <Text block>If the target repository changes before submission, this review expires and must be refreshed before any GitHub write.</Text>}
    <Text block>The workspace creates a branch in your existing fork and opens a pull request. It cannot merge or approve it.</Text>
    <Button appearance="primary" disabled={!confirmed || busy} onClick={onSubmit}>Create pull request</Button>
  </section>
}

function FileOperationGroup({ operation, files }: { operation: SubmissionFile['operation']; files: SubmissionFile[] }) {
  const heading = operation === 'add' ? 'Added files' : operation === 'change' ? 'Changed files' : 'Removed files'
  return <section aria-labelledby={`files-${operation}`}>
    <h4 id={`files-${operation}`}>{heading}</h4>
    <Table aria-label={heading}>
      <TableHeader><TableRow><TableHeaderCell>Operation</TableHeaderCell><TableHeaderCell>Path</TableHeaderCell><TableHeaderCell>Size</TableHeaderCell></TableRow></TableHeader>
      <TableBody>{files.map(file => <TableRow key={file.path}>
        <TableCell>{file.operation}</TableCell><TableCell><code>{file.path}</code></TableCell><TableCell>{file.size}</TableCell>
      </TableRow>)}</TableBody>
    </Table>
  </section>
}
