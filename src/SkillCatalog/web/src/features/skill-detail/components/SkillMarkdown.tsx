import ReactMarkdown from 'react-markdown'
import rehypeSanitize from 'rehype-sanitize'
export function SkillMarkdown({ markdown }: { markdown: string }) { return <article className="markdown"><ReactMarkdown rehypePlugins={[rehypeSanitize]} components={{ pre: props => <pre {...props} tabIndex={0} /> }}>{markdown}</ReactMarkdown></article> }
