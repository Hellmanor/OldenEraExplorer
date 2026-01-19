import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { router } from './router';
import { queryClient } from './lib/queryClient';
import './styles/globals.css';

// Suppress noisy Vite HMR messages that clutter the browser console during development
if (import.meta.env.DEV) {
  const originalDebug = console.debug;
  console.debug = (...args: unknown[]) => {
    const firstArg = String(args[0]);
    if (firstArg.startsWith('[vite]')) {
      return;
    }
    originalDebug(...args);
  };
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>
);
