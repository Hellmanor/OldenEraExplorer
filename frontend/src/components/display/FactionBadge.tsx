import ProgressiveIcon from './ProgressiveIcon';
import { cn } from '@/lib/utils';

interface FactionBadgeProps {
  factionIcon?: string | null;
  factionDisplay?: string | null;
  faction?: string | null;
  /** Icon size in pixels. Default: 48 */
  iconSize?: number;
  /** Custom text class name */
  textClassName?: string;
}

export default function FactionBadge({
  factionIcon,
  factionDisplay,
  faction,
  iconSize = 48,
  textClassName,
}: FactionBadgeProps) {
  const displayName = factionDisplay || faction || 'Unknown';

  return (
    <div className="flex items-center gap-2">
      {factionIcon && (
        <ProgressiveIcon
          iconPath={factionIcon}
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
