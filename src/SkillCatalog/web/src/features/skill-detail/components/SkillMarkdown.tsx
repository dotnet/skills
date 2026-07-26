import ReactMarkdown from 'react-markdown'
import rehypeSanitize from 'rehype-sanitize'
import remarkGfm from 'remark-gfm'
export function SkillMarkdown({ markdown }: { markdown: string }) { return <article className="markdown"><ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]} components={{ pre: props => <pre {...props} tabIndex={0} />, table: props => <table {...props} tabIndex={0} /> }}>{markdown}</ReactMarkdown></article> }
