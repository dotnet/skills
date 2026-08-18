import { Button, MessageBar, MessageBarBody, Spinner, Text, Title1 } from '@fluentui/react-components'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { githubSubmissionClient } from '../../api/githubSubmissionClient'
import type { Contribution, ContributionState } from '../../api/githubSubmissionModels'

const states: ContributionState[] = ['Preparing','ForkReady','BranchReady','CommitReady','PullRequestOpen','ChecksPending','AwaitingReview','Merged']

export function ContributionStatusPage() {
  const { contributionId = '' } = useParams()
  const [contribution, setContribution] = useState<Contribution>()
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState('')

  const load = async (refresh = false) => {
    setBusy(true)
    setError('')
    try {
      setContribution(await githubSubmissionClient.contribution(contributionId, refresh))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Contribution status could not be loaded.')
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => { void load() }, [contributionId])

  if (busy && !contribution) return <main id="main"><Spinner label="Loading contribution status" /></main>
  return <main id="main" className="submission-page">
    <Title1>Contribution progress</Title1>
    {error && <MessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></MessageBar>}
    {contribution && <>
      <Text block>Current state: <strong>{label(contribution.state)}</strong></Text>
      <Text block>Last refreshed: {contribution.lastReconciledAt ? new Date(contribution.lastReconciledAt).toLocaleString() : 'Not yet reconciled'}</Text>
      <ol aria-label="Contribution timeline">
        {states.map(state => <li key={state} aria-current={state === contribution.state ? 'step' : undefined}>{label(state)}</li>)}
      </ol>
      {contribution.recoveryMessage && <MessageBar intent="warning"><MessageBarBody>{contribution.recoveryMessage}</MessageBarBody></MessageBar>}
      {contribution.evidence?.length ? <section aria-labelledby="evidence-title">
        <h2 id="evidence-title">GitHub evidence</h2>
        <ul>{contribution.evidence.map((item, index) => <li key={`${item.kind}-${index}`}>
          {item.url ? <a href={item.url} target="_blank" rel="noreferrer">{item.label}</a> : item.label} {item.status && <Text>— {item.status}</Text>}
        </li>)}</ul>
      </section> : null}
      <Button onClick={() => void load(true)} disabled={busy}>Refresh from GitHub</Button>
      {contribution.pullRequestUrl && <Button as="a" href={contribution.pullRequestUrl} target="_blank">Open pull request</Button>}
    </>}
    <Link to="/">Return to catalog</Link>
  </main>
}

function label(state: ContributionState) {
  return state.replace(/([a-z])([A-Z])/g, '$1 $2')
}
