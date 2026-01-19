import ProgressiveIcon from './ProgressiveIcon';
import { cn } from '@/lib/utils';

interface ClassBadgeProps {
  classIcon?: string | null;
  classDisplay?: string | null;
  classType?: string | null;
  /** Icon size in pixels. Default: 48 */
  iconSize?: number;
  /** Custom text class name */
  textClassName?: string;
}

export default function ClassBadge({
  classIcon,
  classDisplay,
  classType,
  iconSize = 48,
  textClassName,
}: ClassBadgeProps) {
  const displayName = classDisplay || classType || 'Unknown';

  return (
    <div className="flex items-center gap-2">
      {classIcon && (
        <ProgressiveIcon
          iconPath={classIcon}
          alt={displayName}
          size={iconSize}
        />
      )}
      <span className={cn(textClassName)}>
        {displayName}
      </span>
    </div>
  );
}
