import { useState, useEffect, useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import { filesystemApi } from '@/api/client';
import { cn } from '@/lib/utils';
import type { FilesystemRootDto, DirectoryEntryDto } from '@/api/types';

interface FolderPickerModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSelect: (path: string) => void;
  title?: string;
}

export function FolderPickerModal({
  open,
  onOpenChange,
  onSelect,
  title = 'Select Folder',
}: FolderPickerModalProps) {
  const [currentPath, setCurrentPath] = useState<string | null>(null);
  const [selectedPath, setSelectedPath] = useState<string | null>(null);

  // Fetch filesystem roots (drives/mount points)
  const { data: rootsData } = useQuery({
    queryKey: ['filesystem', 'roots'],
    queryFn: () => filesystemApi.getRoots(),
    enabled: open,
    staleTime: 60000,
  });

  // Fetch current directory listing
  const { data: listingData, isLoading, error } = useQuery({
    queryKey: ['filesystem', 'list', currentPath],
    queryFn: () => filesystemApi.listDirectory(currentPath ?? undefined),
    enabled: open && currentPath !== null,
    staleTime: 5000,
  });

  useEffect(() => {
    if (open) {
      // Reset state when modal opens - intentional cascading render for clean slate
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setCurrentPath(null);
       
      setSelectedPath(null);
    }
  }, [open]);

  const handleRootClick = useCallback((root: FilesystemRootDto) => {
    setCurrentPath(root.id);
    setSelectedPath(root.id);
  }, []);

  const handleFolderDoubleClick = useCallback((entry: DirectoryEntryDto) => {
    setCurrentPath(entry.id);
    setSelectedPath(entry.id);
  }, []);

  const handleFolderClick = useCallback((entry: DirectoryEntryDto) => {
    setSelectedPath(entry.id);
  }, []);

  const handleParentClick = useCallback(() => {
    if (listingData?.parent) {
      setCurrentPath(listingData.parent);
      setSelectedPath(listingData.parent);
    }
  }, [listingData]);

  const handleBreadcrumbClick = useCallback((path: string) => {
    setCurrentPath(path);
    setSelectedPath(path);
  }, []);

  const handleSelect = useCallback(() => {
    if (selectedPath) {
      onSelect(selectedPath);
      onOpenChange(false);
    }
  }, [selectedPath, onSelect, onOpenChange]);

  // Parse current path into breadcrumb segments
  const breadcrumbs = currentPath ? parseBreadcrumbs(currentPath) : [];

  if (!open) return null;

  return (
    <>
      <div
        className="fixed inset-0 bg-black/50 z-50"
        onClick={() => onOpenChange(false)}
      />

      <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          className="bg-popover border border-border rounded-lg shadow-lg max-w-3xl w-full h-[500px] flex flex-col"
          onClick={(e) => e.stopPropagation()}
        >
          <div className="px-4 py-3 border-b border-border">
            <h2 className="text-lg font-semibold">{title}</h2>
          </div>

        <div className="flex flex-1 min-h-0">
          <div className="w-48 border-r border-border flex flex-col">
            <div className="px-3 py-2 text-xs font-medium text-muted-foreground uppercase tracking-wider">
              Locations
            </div>
            <div className="flex-1 overflow-auto">
              <div className="px-2 pb-2 space-y-0.5">
                {rootsData?.roots.map((root) => (
                  <button
                    key={root.id}
                    onClick={() => handleRootClick(root)}
                    className={cn(
                      "w-full px-2 py-1.5 rounded text-sm text-left flex items-center gap-2 transition-colors",
                      currentPath?.startsWith(root.id)
                        ? "bg-primary/10 text-primary"
                        : "text-foreground hover:bg-accent"
                    )}
                  >
                    <RootIcon type={root.icon} />
                    <span className="truncate">{root.name}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div className="flex-1 flex flex-col min-w-0">
            {currentPath && (
              <div className="px-3 py-2 border-b border-border flex items-center gap-1 text-sm overflow-x-auto">
                {listingData?.parent && (
                  <button
                    onClick={handleParentClick}
                    className="p-1 rounded hover:bg-accent text-muted-foreground"
                    title="Go up"
                  >
                    <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <path d="M15 18l-6-6 6-6" />
                    </svg>
                  </button>
                )}
                {breadcrumbs.map((segment, index) => (
                  <span key={segment.path} className="flex items-center">
                    {index > 0 && <span className="text-muted-foreground mx-1">/</span>}
                    <button
                      onClick={() => handleBreadcrumbClick(segment.path)}
                      className={cn(
                        "px-1.5 py-0.5 rounded hover:bg-accent transition-colors truncate max-w-[120px]",
                        index === breadcrumbs.length - 1 ? "text-foreground font-medium" : "text-muted-foreground"
                      )}
                      title={segment.name}
                    >
                      {segment.name}
                    </button>
                  </span>
                ))}
              </div>
            )}

            <div className="flex-1 overflow-auto">
              {!currentPath ? (
                <div className="flex items-center justify-center h-full text-muted-foreground">
                  Select a location from the sidebar
                </div>
              ) : isLoading ? (
                <div className="flex items-center justify-center h-full">
                  <Spinner />
                </div>
              ) : error ? (
                <div className="flex items-center justify-center h-full text-destructive text-sm">
                  {error instanceof Error ? error.message : 'Failed to load directory'}
                </div>
              ) : listingData?.entries.length === 0 ? (
                <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
                  This folder is empty
                </div>
              ) : (
                <div className="p-2 space-y-0.5">
                  {listingData?.entries.map((entry) => (
                    <button
                      key={entry.id}
                      onClick={() => handleFolderClick(entry)}
                      onDoubleClick={() => handleFolderDoubleClick(entry)}
                      className={cn(
                        "w-full px-3 py-2 rounded text-sm text-left flex items-center gap-3 transition-colors",
                        selectedPath === entry.id
                          ? "bg-primary/10 text-primary ring-1 ring-primary/30"
                          : "text-foreground hover:bg-accent"
                      )}
                    >
                      <svg className="w-5 h-5 flex-shrink-0 text-muted-foreground" viewBox="0 0 24 24" fill="currentColor">
                        <path d="M4 4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-8l-2-2H4z" />
                      </svg>
                      <span className="truncate">{entry.name}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="px-4 py-3 border-t border-border flex items-center gap-2">
          <div className="flex-1 text-sm text-muted-foreground truncate mr-4">
            {selectedPath && <span title={selectedPath}>{selectedPath}</span>}
          </div>
          <button
            onClick={() => onOpenChange(false)}
            className="px-4 py-2 text-sm rounded bg-secondary text-secondary-foreground hover:bg-secondary/80 transition-colors cursor-pointer"
          >
            Cancel
          </button>
          <button
            onClick={handleSelect}
            disabled={!selectedPath}
            className={cn(
              "px-4 py-2 text-sm rounded transition-colors",
              selectedPath
                ? "bg-primary text-primary-foreground hover:bg-primary/90 cursor-pointer"
                : "bg-muted text-muted-foreground cursor-not-allowed"
            )}
          >
            Select
          </button>
        </div>
        </div>
      </div>
    </>
  );
}

function parseBreadcrumbs(path: string): { name: string; path: string }[] {
  const segments: { name: string; path: string }[] = [];
  const isWindows = path.match(/^[A-Z]:\\/i);
  const separator = isWindows ? '\\' : '/';

  const parts = path.split(/[/\\]/).filter(Boolean);

  let currentPath = isWindows ? '' : '/';

  for (const part of parts) {
    if (isWindows && currentPath === '') {
      currentPath = part + '\\';
    } else {
      currentPath = currentPath + (currentPath.endsWith(separator) || currentPath === '/' ? '' : separator) + part;
    }
    segments.push({ name: part, path: currentPath });
  }

  return segments;
}

function RootIcon({ type }: { type: string }) {
  if (type === 'home') {
    return (
      <svg className="w-4 h-4 text-muted-foreground" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
        <path d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
      </svg>
    );
  }
  if (type === 'drive') {
    return (
      <svg className="w-4 h-4 text-muted-foreground" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
        <rect x="3" y="4" width="18" height="16" rx="2" />
        <line x1="3" y1="12" x2="21" y2="12" />
        <circle cx="7" cy="16" r="1" fill="currentColor" />
      </svg>
    );
  }
  return (
    <svg className="w-4 h-4 text-muted-foreground" viewBox="0 0 24 24" fill="currentColor">
      <path d="M4 4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-8l-2-2H4z" />
    </svg>
  );
}

function Spinner() {
  return (
    <svg className="animate-spin w-6 h-6 text-primary" viewBox="0 0 24 24" fill="none">
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeOpacity="0.3" strokeWidth="4" />
      <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" strokeWidth="4" strokeLinecap="round" />
    </svg>
  );
}
