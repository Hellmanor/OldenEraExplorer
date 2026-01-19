import ProgressiveIcon from './ProgressiveIcon';
import { cn } from '@/lib/utils';

interface CurrencyBadgeProps {
  /** The amount/value to display (number or string for placeholder support) */
  amount: number | string;
  /** Resource key (e.g., 'Gold', 'Mana', 'Wood'). Not used if iconPath is provided. */
  resourceKey?: string;
  /** Custom icon path. If provided, overrides the default resource icon path. */
  iconPath?: string;
  /** Display name for the resource (used as alt text) */
  displayName?: string;
  /** Icon size in pixels. Default: 24 */
  iconSize?: number;
  /** Gap between amount and icon. Default: 'gap-1.5' */
  gap?: string;
  /** Custom class name for the amount text */
  amountClassName?: string;
  /** Whether to use locale string for amount formatting. Default: false */
  useLocaleString?: boolean;
}

export default function CurrencyBadge({
  amount,
  resourceKey,
  iconPath: customIconPath,
  displayName,
  iconSize = 24,
  gap = 'gap-1.5',
  amountClassName,
  useLocaleString = false,
}: CurrencyBadgeProps) {
  const formattedAmount = typeof amount === 'number'
    ? (useLocaleString ? amount.toLocaleString() : amount)
    : amount;

  const altText = displayName || resourceKey || 'Resource';
  const finalIconPath = customIconPath || (resourceKey ? `icons/resources/${resourceKey.toLowerCase()}_64` : '');

  return (
    <div className={cn('flex items-center', gap)}>
      <span className={cn('font-semibold', amountClassName)}>
        {formattedAmount}
      </span>
      <ProgressiveIcon
        iconPath={finalIconPath}
        alt={altText}
        size={iconSize}
      />
    </div>
  );
}
