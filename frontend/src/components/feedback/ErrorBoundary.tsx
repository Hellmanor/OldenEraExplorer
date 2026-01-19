import { Component, type ReactNode } from 'react';
import { useLabels } from '@/hooks/useLabels';

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
  errorTitle?: string;
  unknownError?: string;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

class ErrorBoundaryClass extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
    console.error('ErrorBoundary caught an error:', error);
    console.error('Error stack:', error.stack);
    console.error('Component stack:', errorInfo.componentStack);
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }
      return (
        <div className="p-10 text-center text-destructive">
          <h2>{this.props.errorTitle}</h2>
          <p>{this.state.error?.message || this.props.unknownError}</p>
        </div>
      );
    }

    return this.props.children;
  }
}

export default function ErrorBoundary({ children, fallback }: Omit<Props, 'errorTitle' | 'unknownError'>) {
  const { label } = useLabels();

  return (
    <ErrorBoundaryClass
      errorTitle={label('error_generic')}
      unknownError={label('error_unknown')}
      fallback={fallback}
    >
      {children}
    </ErrorBoundaryClass>
  );
}
