import { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { searchApi } from '@/api/client';
import type { SearchResultDto, EntityType } from '@/api/types';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';
import { useLabels } from '@/hooks/useLabels';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';

const entityRoutes: Record<EntityType, (id: string) => string> = {
  Unit: (id) => `/units/${id}`,
  Hero: (id) => `/heroes/${id}`,
  Skill: (id) => `/skills/${id}`,
  SubSkill: (id) => `/skills/${id}`,
  Spell: (id) => `/spells/${id}`,
  Artifact: (id) => `/artifacts/${id}`,
  Building: (id) => `/buildings/${id}`,
  MapObject: (id) => `/map-objects/${id}`,
  FactionLaw: (id) => `/faction-laws/${id}`,
  Ability: (id) => `/abilities/${id}`,
  Subclass: (id) => `/subclasses/${id}`,
};

export default function GlobalSearch() {
  const { label } = useLabels();
  const [isOpen, setIsOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResultDto[]>([]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const navigate = useNavigate();

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setIsOpen(false);
      }
    };

    const handleOpenEvent = () => setIsOpen(true);

    window.addEventListener('keydown', handleKeyDown);
    window.addEventListener('openGlobalSearch', handleOpenEvent);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('openGlobalSearch', handleOpenEvent);
    };
  }, []);

  useEffect(() => {
    if (isOpen) {
      inputRef.current?.focus();
    } else {
      setQuery('');
      setResults([]);
      setSelectedIndex(0);
    }
  }, [isOpen]);

  useEffect(() => {
    if (query.length < 2) {
      setResults([]);
      setSelectedIndex(0);
      return;
    }

    const timer = setTimeout(async () => {
      setIsLoading(true);
      try {
        const response = await searchApi.search(query, 20);
        setResults(response.results);
        setSelectedIndex(0);
      } catch (error) {
        console.error('Search failed:', error);
        setResults([]);
      } finally {
        setIsLoading(false);
      }
    }, 200);

    return () => clearTimeout(timer);
  }, [query]);

  const navigateToResult = useCallback(
    (result: SearchResultDto) => {
      const routeFn = entityRoutes[result.type];
      if (routeFn) {
        navigate(routeFn(result.id), {
          state: {
            highlightText: query,
            matchLocation: result.matchLocation,
            entityId: result.id
          }
        });
      }
      setIsOpen(false);
    },
    [navigate, query]
  );

  const handleBlur = () => {
    setTimeout(() => {
      setIsOpen(false);
    }, 150);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setSelectedIndex((prev) => Math.min(prev + 1, results.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setSelectedIndex((prev) => Math.max(prev - 1, 0));
    } else if (e.key === 'Enter' && results[selectedIndex]) {
      e.preventDefault();
      navigateToResult(results[selectedIndex]);
    }
  };

  const showResults = results.length > 0 || (query.length >= 2 && !isLoading);

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="h-[47px] w-[47px] flex items-center justify-center rounded-xl text-muted-foreground hover:text-foreground hover:bg-accent transition-colors cursor-pointer"
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="32"
          height="32"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="icon icon-tabler icons-tabler-outline icon-tabler-search"
        >
          <path stroke="none" d="M0 0h24v24H0z" fill="none"/>
          <path d="M3 10a7 7 0 1 0 14 0a7 7 0 1 0 -14 0" />
          <path d="M21 21l-6 -6" />
        </svg>
      </button>

      {isOpen && (
        <>
          <div className="absolute top-0 right-0 w-80 lg:w-105 max-[850px]:w-72 max-[650px]:w-64 max-[500px]:w-[90vw] bg-accent rounded-xl shadow-[0_8px_32px_rgba(0,0,0,0.5)] z-[100]">
            <div className={cn(
              "flex items-center gap-3 px-2 h-[47px]",
              showResults && "border-b border-border"
            )}>
              <svg
                xmlns="http://www.w3.org/2000/svg"
                width="32"
                height="32"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
                className="text-muted-foreground shrink-0 icon icon-tabler icons-tabler-outline icon-tabler-search"
              >
                <path stroke="none" d="M0 0h24v24H0z" fill="none"/>
                <path d="M3 10a7 7 0 1 0 14 0a7 7 0 1 0 -14 0" />
                <path d="M21 21l-6 -6" />
              </svg>
              <Input
                ref={inputRef}
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={handleKeyDown}
                onBlur={handleBlur}
                placeholder={label('search_placeholder')}
                className="border-0 bg-transparent shadow-none focus-visible:ring-0 !text-lg h-auto p-0 flex-1 my-2"
              />
              {isLoading && (
                <span className="text-muted-foreground text-sm shrink-0">{label('common_loading', '')}</span>
              )}
              <button
                onClick={() => setIsOpen(false)}
                className="p-2 rounded text-muted-foreground hover:text-foreground transition-colors shrink-0 cursor-pointer"
              >
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  width="32"
                  height="32"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className="icon icon-tabler icons-tabler-outline icon-tabler-x"
                >
                  <path stroke="none" d="M0 0h24v24H0z" fill="none"/>
                  <path d="M18 6l-12 12" />
                  <path d="M6 6l12 12" />
                </svg>
              </button>
            </div>

            {results.length > 0 ? (
              <div className="max-h-80 overflow-y-auto">
                {results.map((result, index) => (
                  <button
                    key={`${result.type}-${result.id}`}
                    onClick={() => navigateToResult(result)}
                    onMouseEnter={() => setSelectedIndex(index)}
                    className={cn(
                      "flex items-start gap-3 w-full px-4 py-3 border-0 text-foreground text-left cursor-pointer transition-colors bg-card hover:bg-muted",
                      index === selectedIndex && "bg-muted",
                      index === results.length - 1 && "rounded-b-xl"
                    )}
                  >
                    {result.type === 'Building' ? (
                      <div className="overflow-hidden rounded shrink-0" style={{ width: 40, height: 40 }}>
                        <ProgressiveIcon
                          iconPath={result.iconPath}
                          alt={result.name}
                          size={40}
                          style={{ transform: 'scale(1.35)' }}
                        />
                      </div>
                    ) : (
                      <div className="shrink-0">
                        <ProgressiveIcon
                          iconPath={result.iconPath}
                          alt={result.name}
                          size={40}
                        />
                      </div>
                    )}
                    <span className="flex-1 text-[0.95rem]">{result.name}</span>
                    {result.id !== result.name && (
                      <span className="text-sm text-muted-foreground/50 font-mono max-w-[150px] break-all">
                        {result.id}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            ) : query.length >= 2 && !isLoading ? (
              <div className="h-[47px] flex items-center justify-center text-muted-foreground">
                {label('search_no_results', query)}
              </div>
            ) : null}
          </div>
        </>
      )}
    </div>
  );
}
