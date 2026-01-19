import { useId } from 'react';
import { cn } from '@/lib/utils';

interface HexagonFrameProps {
  children: React.ReactNode;
  size?: number;
  className?: string;
}

export default function HexagonFrame({
  children,
  size = 104,
  className
}: HexagonFrameProps) {
  const clipId = useId();
  const hexPoints = "50,0 100,25 100,75 50,100 0,75 0,25";

  return (
    <div
      className={cn("relative inline-block", className)}
      style={{ width: size, height: size }}
    >
      <svg
        width={size}
        height={size}
        viewBox="0 0 100 100"
        className="absolute inset-0"
      >
        <defs>
          <clipPath id={clipId}>
            <polygon points={hexPoints} />
          </clipPath>
        </defs>
        <polygon
          points={hexPoints}
          fill="currentColor"
          className="text-background"
        />
      </svg>

      <div
        className="absolute inset-0 overflow-hidden"
        style={{ clipPath: `polygon(50% 0%, 100% 25%, 100% 75%, 50% 100%, 0% 75%, 0% 25%)` }}
      >
        {children}
      </div>
    </div>
  );
}
