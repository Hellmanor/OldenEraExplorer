import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMapObjectsStore, type MapObjectSortField } from './mapObjectsStore';
import { useMapObjects, useMapObject } from './useMapObjects';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import RichText from '@/components/display/RichText';
import type { MapObjectListItemDto, MapObjectDetailDto } from '@/api/types';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import DetailContainer from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

/**
 * Single-level sorting for Map Objects (no multi-level like Units).
 * Default: Name (asc)
 */
function compareMapObjects(
  a: MapObjectListItemDto,
  b: MapObjectListItemDto,
  field: MapObjectSortField,
  direction: SortDirection
): number {
  const dir = direction === 'asc' ? 1 : -1;

  switch (field) {
    case 'name':
      return (a.name || '').localeCompare(b.name || '') * dir;
    case 'id':
      return a.id.localeCompare(b.id, undefined, { numeric: true }) * dir;
    default:
      return 0;
  }
}

interface MapObjectDetailPanelProps {
  mapObject: MapObjectDetailDto | null;
  selectedMapObjectId: string | null;
  error?: Error | null;
}

function MapObjectDetailPanel({ mapObject, selectedMapObjectId, error }: MapObjectDetailPanelProps) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('nav_map_objects'), label(error.message))}
      </div>
    );
  }

  if (!mapObject) {
    // Only show "select" message if no map object is selected
    if (!selectedMapObjectId) {
      return (
        <div className="p-10 text-center text-muted-foreground flex flex-col items-center justify-center h-full">
          <div className="text-5xl mb-4">[ ]</div>
          <div>{label('detail_select')}</div>
        </div>
      );
    }
    // Loading state - prevents flash
    return null;
  }

  return (
    <DetailContainer>
        <div className="bg-card border border-border rounded-lg p-4 grid grid-cols-1 md:grid-cols-[112px_1fr] gap-4">
        <div>
          <ProgressiveIcon
            iconPath={mapObject.icon}
            alt={mapObject.name}
            size={112}
            className="rounded-md shrink-0"
          />
        </div>

        <div className="flex flex-col gap-2">
          <h2 className="text-xl font-semibold text-foreground m-0">
            {mapObject.name}
          </h2>

          {mapObject.description && (
            <RichText
              text={mapObject.description}
              className="text-muted-foreground text-sm leading-relaxed block"
            />
          )}

          {mapObject.narrativeDescription && (
            <RichText
              text={mapObject.narrativeDescription}
              className="text-muted-foreground text-sm leading-relaxed italic m-0 block"
            />
          )}
        </div>
      </div>
    </DetailContainer>
  );
}

export default function MapObjectsPage() {
  const navigate = useNavigate();
  const { '*': urlPath } = useParams<{ '*'?: string }>();

  useHighlightText();

  const {
    selectedMapObjectId,
    setSelectedMapObjectId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useMapObjectsStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  const urlMapObjectId = urlPath || null;

  const prevUrlMapObjectIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (urlMapObjectId && urlMapObjectId !== selectedMapObjectId && urlMapObjectId !== prevUrlMapObjectIdRef.current) {
      setSelectedMapObjectId(urlMapObjectId);
    }
    prevUrlMapObjectIdRef.current = urlMapObjectId;
  }, [urlMapObjectId, selectedMapObjectId, setSelectedMapObjectId]);

  const mapObjectsQuery = useMapObjects(searchQuery || undefined);
  const mapObjectQuery = useMapObject(selectedMapObjectId);

  const sortedMapObjects = useMemo(() => {
    if (!mapObjectsQuery.data) return [];
    return [...mapObjectsQuery.data].sort((a, b) =>
      compareMapObjects(a, b, sortField, sortDirection)
    );
  }, [mapObjectsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      // Toggle direction if same field
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      // New field, start with ascending
      setSort(field as MapObjectSortField, 'asc');
    }
  };

  const handleSelectMapObject = (id: string) => {
    setSelectedMapObjectId(id);
    // Navigate with the category/name format
    navigate(`/map-objects/${id}`);
  };

  const handleSearchChange = (query: string) => {
    setSearchQuery(query);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
  };

  interface MapObjectListProps {
    mapObjects: MapObjectListItemDto[];
    selectedMapObjectId: string | null;
    onSelectMapObject: (id: string) => void;
    isLoading?: boolean;
  }

  function MapObjectList({ mapObjects, selectedMapObjectId, onSelectMapObject, isLoading }: MapObjectListProps) {
    // Scroll selected item into view when selection changes
    useEffect(() => {
      if (selectedMapObjectId && mapObjects.length > 0) {
        document.getElementById(`mapobject-${selectedMapObjectId}`)?.scrollIntoView({
          behavior: 'instant',
          block: 'nearest',
        });
      }
    }, [selectedMapObjectId, mapObjects.length]);

    if (isLoading) {
      return (
        <div className="p-5 text-center text-muted-foreground">
          {label('common_loading', ' ' + label('nav_map_objects'))}
        </div>
      );
    }

    if (mapObjects.length === 0) {
      return (
        <div className="p-5 text-center text-muted-foreground">
          {label('no_results', label('nav_map_objects'))}
        </div>
      );
    }

    return (
      <div className="flex flex-col gap-1">
        {mapObjects.map((mapObject) => (
          <button
            key={mapObject.id}
            id={`mapobject-${mapObject.id}`}
            onClick={() => onSelectMapObject(mapObject.id)}
            className={cn(
              "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
              selectedMapObjectId === mapObject.id
                ? "bg-primary text-primary-foreground"
                : "bg-transparent text-foreground hover:bg-accent"
            )}
          >
            <ProgressiveIcon
              iconPath={mapObject.icon}
              alt={mapObject.name}
              size={40}
            />
            <div className="flex-1 min-w-0">
              <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
                {mapObject.name}
              </div>
              <div className={cn(
                "text-xs mt-0.5",
                selectedMapObjectId === mapObject.id ? "text-primary-foreground/70" : "text-muted-foreground"
              )}>
                {mapObject.id}
              </div>
            </div>
          </button>
        ))}
      </div>
    );
  }

  return (
    <div className="h-full overflow-hidden">
      <aside className="absolute left-0 top-0 bottom-0 w-80 lg:w-105 z-10 border-r border-border flex flex-col bg-card">
        <div className="px-2 h-[47px] border-b border-border flex items-center gap-0.5 relative">
          <SortableColumnHeader
            label={columnLabels.name}
            field="name"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={columnLabels.id}
            field="id"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', label('nav_map_objects'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {mapObjectsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_map_objects'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(mapObjectsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <MapObjectList
              mapObjects={sortedMapObjects}
              selectedMapObjectId={selectedMapObjectId}
              onSelectMapObject={handleSelectMapObject}
              isLoading={mapObjectsQuery.isLoading}
            />
          )}
        </div>

        {mapObjectsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground">
            <span>{label('total_count', mapObjectsQuery.data.length, label('nav_map_objects'))}</span>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <MapObjectDetailPanel
            mapObject={mapObjectQuery.data || null}
            selectedMapObjectId={selectedMapObjectId}
            error={mapObjectQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
