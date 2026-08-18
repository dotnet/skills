import { Button, MessageBar, MessageBarBody, Text } from '@fluentui/react-components'
import type { GitHubSession } from '../../../api/githubSubmissionModels'

export function GitHubSignInPanel({ session, busy, error, onSignIn, onLogout }: {
  session?: GitHubSession
  busy: boolean
  error?: string
  onSignIn: () => void
  onLogout: () => void
}) {
  return <section aria-labelledby="github-signin-title">
    <h3 id="github-signin-title">GitHub identity</h3>
    {error && <MessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></MessageBar>}
    {session?.authenticated
      ? <div><Text>Signed in as <strong>{session.login}</strong></Text> <Button appearance="subtle" onClick={onLogout}>Sign out</Button></div>
      : <div>
          <Text block>Sign in through a secure popup. Your uploaded package stays in this browser tab.</Text>
          <Text block>You need an existing fork of the target repository and must grant the Skill Catalog GitHub App access to that fork. If either is missing, submission guidance will link you to the required GitHub step.</Text>
          <Button appearance="primary" disabled={busy} onClick={onSignIn}>Sign in with GitHub</Button>
        </div>}
  </section>
}
