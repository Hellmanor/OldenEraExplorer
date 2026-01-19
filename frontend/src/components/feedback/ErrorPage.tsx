import { useRouteError, isRouteErrorResponse, useNavigate } from 'react-router-dom';
import { useState } from 'react';
import { useLabels } from '@/hooks/useLabels';

export default function ErrorPage() {
  const error = useRouteError();
  const navigate = useNavigate();
  const [showDetails, setShowDetails] = useState(false);
  const { label } = useLabels();

  let title = label('error_generic');
  let message = label('error_generic');
  let statusCode: number | null = null;
  let stack: string | null = null;

  if (isRouteErrorResponse(error)) {
    statusCode = error.status;
    if (error.status === 404) {
      title = label('error_page_not_found');
      message = label('error_page_not_found_msg');
    } else if (error.status === 500) {
      title = label('error_server');
      message = label('error_server_msg');
    } else {
      message = error.statusText || error.data?.message || message;
    }
  } else if (error instanceof Error) {
    message = error.message;
    stack = error.stack || null;
  } else if (typeof error === 'string') {
    message = error;
  }

  const handleGoBack = () => {
    navigate(-1);
  };

  const handleGoHome = () => {
    navigate('/');
  };

  const handleReload = () => {
    window.location.reload();
  };

  return (
    <div className="min-h-screen flex flex-col justify-center items-center bg-background text-foreground p-6">
      <div className="mb-8 relative">
        <div className="absolute -inset-8 bg-gradient-to-br from-destructive/10 to-orange-500/10 rounded-full blur-2xl" />

        <div className="relative w-32 h-32 flex items-center justify-center rounded-full bg-gradient-to-br from-destructive/20 to-orange-500/20 border border-destructive/20">
          {statusCode === 404 ? (
            <svg
              className="w-16 h-16 text-destructive/80"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={1.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M9 6.75V15m6-6v8.25m.503 3.498l4.875-2.437c.381-.19.622-.58.622-1.006V4.82c0-.836-.88-1.38-1.628-1.006l-3.869 1.934c-.317.159-.69.159-1.006 0L9.503 3.252a1.125 1.125 0 00-1.006 0L3.622 5.689C3.24 5.88 3 6.27 3 6.695V19.18c0 .836.88 1.38 1.628 1.006l3.869-1.934c.317-.159.69-.159 1.006 0l4.994 2.497c.317.158.69.158 1.006 0z"
              />
            </svg>
          ) : (
            <svg
              className="w-16 h-16 text-destructive/80"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={1.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z"
              />
            </svg>
          )}
        </div>
      </div>

      {statusCode && (
        <div className="mb-4 px-3 py-1 rounded-full bg-destructive/10 border border-destructive/20 text-destructive text-sm font-mono">
          Error {statusCode}
        </div>
      )}

      <h1 className="text-3xl font-bold text-center mb-3 bg-gradient-to-br from-destructive to-orange-500 bg-clip-text text-transparent">
        {title}
      </h1>

      <p className="text-lg text-muted-foreground text-center max-w-md mb-8">
        {message}
      </p>

      <div className="flex gap-3 mb-8">
        <button
          onClick={handleGoBack}
          className="px-4 py-2 rounded-md border border-border bg-background hover:bg-accent hover:text-accent-foreground transition-colors flex items-center cursor-pointer"
        >
          <svg className="w-4 h-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5L3 12m0 0l7.5-7.5M3 12h18" />
          </svg>
          {label('nav_back')}
        </button>
        <button
          onClick={handleGoHome}
          className="px-4 py-2 rounded-md border border-border bg-background hover:bg-accent hover:text-accent-foreground transition-colors flex items-center cursor-pointer"
        >
          <svg className="w-4 h-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 12l8.954-8.955c.44-.439 1.152-.439 1.591 0L21.75 12M4.5 9.75v10.125c0 .621.504 1.125 1.125 1.125H9.75v-4.875c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21h4.125c.621 0 1.125-.504 1.125-1.125V9.75M8.25 21h8.25" />
          </svg>
          {label('nav_home')}
        </button>
        <button
          onClick={handleReload}
          className="px-4 py-2 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 transition-colors flex items-center cursor-pointer"
        >
          <svg className="w-4 h-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
          </svg>
          {label('nav_reload')}
        </button>
      </div>

      {stack && (
        <div className="w-full max-w-2xl">
          <button
            onClick={() => setShowDetails(!showDetails)}
            className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors mx-auto"
          >
            <svg
              className={`w-4 h-4 transition-transform ${showDetails ? 'rotate-90' : ''}`}
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2}
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
            </svg>
            {label('error_btn_technical_details')}
          </button>

          {showDetails && (
            <div className="mt-4 p-4 bg-muted rounded-lg border border-border overflow-auto">
              <pre className="text-xs font-mono text-muted-foreground whitespace-pre-wrap break-words">
                {stack}
              </pre>
            </div>
          )}
        </div>
      )}

      <p className="mt-12 text-xs text-muted-foreground/60">
        {label('error_check_console')}
      </p>
    </div>
  );
}
