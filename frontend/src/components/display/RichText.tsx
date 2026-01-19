import DOMPurify from 'dompurify';
import { cn } from '@/lib/utils';

interface RichTextProps {
  text: string;
  /** Additional class names for the container */
  className?: string;
}

function processResolutionTag(
  text: string,
  tagName: 'resolved' | 'unresolved',
  colorClass: string
): string {
  const regex = new RegExp(`([+\\-–]?)<${tagName}>(.*?)</${tagName}>(%)?`, 'gi');

  return text.replace(regex, (_match, prefix, content, suffix) => {
    const styledContent = `${prefix || ''}${content}${suffix || ''}`;
    return `<span class="${colorClass}">${styledContent}</span>`;
  });
}

export default function RichText({ text, className }: RichTextProps) {
  if (!text) return null;

  let processedText = processResolutionTag(text, 'resolved', 'resolved-value');
  processedText = processResolutionTag(processedText, 'unresolved', 'unresolved-value');

  const clean = DOMPurify.sanitize(processedText, {
    ALLOWED_TAGS: ['b', 'i', 'span'],
    ALLOWED_ATTR: ['class'],
    KEEP_CONTENT: true,
  });

  return (
    <span
      className={cn(className)}
      dangerouslySetInnerHTML={{ __html: clean }}
    />
  );
}
