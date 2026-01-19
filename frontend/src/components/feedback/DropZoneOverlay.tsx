/* eslint-disable react-refresh/only-export-components */
import { useState, useCallback, useRef, useEffect } from 'react';
import { useLabels } from '@/hooks/useLabels';

const ENABLE_DROP_ZONE = true;

interface DropZoneOverlayProps {
  onFileDropped: (file: File, blobUrl: string) => void;
  children: React.ReactNode;
}

export default function DropZoneOverlay({ onFileDropped, children }: DropZoneOverlayProps) {
  const { label } = useLabels();
  const [isDragging, setIsDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dragCounterRef = useRef(0);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (error) {
      const timer = setTimeout(() => setError(null), 3000);
      return () => clearTimeout(timer);
    }
  }, [error]);

  const handleDragEnter = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current++;
    if (e.dataTransfer.items && e.dataTransfer.items.length > 0) {
      setIsDragging(true);
    }
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current--;
    if (dragCounterRef.current === 0) {
      setIsDragging(false);
    }
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    dragCounterRef.current = 0;

    const files = e.dataTransfer.files;
    if (files.length === 0) return;

    const file = files[0];
    const isGlb = file.name.toLowerCase().endsWith('.glb');

    if (!isGlb) {
      setError(label('dropzone_unsupported'));
      return;
    }

    const blobUrl = URL.createObjectURL(file);
    onFileDropped(file, blobUrl);
  }, [onFileDropped, label]);

  if (!ENABLE_DROP_ZONE) {
    return <>{children}</>;
  }

  return (
    <div
      ref={containerRef}
      className="relative w-full h-full"
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      onDragOver={handleDragOver}
      onDrop={handleDrop}
    >
      {children}

      {isDragging && (
        <div className="absolute inset-0 z-50 flex items-center justify-center bg-primary/20 backdrop-blur-sm border-4 border-dashed border-primary rounded-lg pointer-events-none">
          <div className="flex flex-col items-center gap-4 text-primary">
            <svg
              width="80"
              height="80"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="17 8 12 3 7 8" />
              <line x1="12" y1="3" x2="12" y2="15" />
            </svg>
            <div className="text-xl font-semibold">{label('dropzone_text')}</div>
            <div className="text-sm opacity-75">{label('dropzone_unsupported')}</div>
          </div>
        </div>
      )}

      {error && (
        <div className="absolute bottom-4 left-1/2 -translate-x-1/2 z-50 px-4 py-2 bg-destructive text-destructive-foreground rounded-lg shadow-lg text-sm animate-in fade-in slide-in-from-bottom-2">
          {error}
        </div>
      )}
    </div>
  );
}

export function useDroppedFile() {
  const [droppedFile, setDroppedFile] = useState<{
    file: File;
    blobUrl: string;
  } | null>(null);

  const handleFileDropped = useCallback((file: File, blobUrl: string) => {
    if (droppedFile?.blobUrl) {
      URL.revokeObjectURL(droppedFile.blobUrl);
    }
    setDroppedFile({ file, blobUrl });
  }, [droppedFile]);

  const clearDroppedFile = useCallback(() => {
    if (droppedFile?.blobUrl) {
      URL.revokeObjectURL(droppedFile.blobUrl);
    }
    setDroppedFile(null);
  }, [droppedFile]);

  useEffect(() => {
    return () => {
      if (droppedFile?.blobUrl) {
        URL.revokeObjectURL(droppedFile.blobUrl);
      }
    };
  }, [droppedFile]);

  return {
    droppedFile,
    handleFileDropped,
    clearDroppedFile,
  };
}
