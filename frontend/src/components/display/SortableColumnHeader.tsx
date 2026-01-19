import { cn } from '@/lib/utils';
import { ChevronUp, ChevronDown, ChevronsUpDown } from 'lucide-react';

export type SortDirection = 'asc' | 'desc';

interface SortableColumnHeaderProps {
  label: string;
  field: string;
  currentField: string | null;
  direction: SortDirection;
  onSort: (field: string) => void;
  className?: string;
}

export default function SortableColumnHeader({
  label,
  field,
  currentField,
  direction,
  onSort,
  className,
}: SortableColumnHeaderProps) {
  const isActive = currentField === field;

  const handleClick = () => {
    onSort(field);
  };

  return (
    <button
      onClick={handleClick}
      className={cn(
        "inline-flex items-center gap-1 px-2 py-1 rounded-md text-sm font-medium transition-colors",
        "hover:bg-accent hover:text-accent-foreground",
        "focus:outline-none",
        isActive ? "text-foreground" : "text-muted-foreground",
        className
      )}
    >
      {label}
      <span className="w-4 h-4 flex items-center justify-center">
        {isActive ? (
          direction === 'asc' ? (
            <ChevronUp className="h-4 w-4" />
          ) : (
            <ChevronDown className="h-4 w-4" />
          )
        ) : (
          <ChevronsUpDown className="h-3 w-3 opacity-50" />
        )}
      </span>
    </button>
  );
}
