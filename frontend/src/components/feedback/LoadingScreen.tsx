import { useLabels } from '@/hooks/useLabels';

interface LoadingScreenProps {
  message?: string;
}

export function LoadingScreen({ message }: LoadingScreenProps) {
  const { label } = useLabels();
  const displayMessage = message || label('common_loading', '');

  return (
    <div className="min-h-screen flex flex-col justify-center items-center bg-card text-white">
      <div className="w-12 h-12 border-4 border-border border-t-primary rounded-full animate-spin" />
      <p className="mt-4 text-muted-foreground">{displayMessage}</p>
    </div>
  );
}
