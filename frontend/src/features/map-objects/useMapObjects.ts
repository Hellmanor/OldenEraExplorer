import { useQuery } from '@tanstack/react-query';
import { mapObjectsApi } from '@/api/client';
import type { MapObjectListItemDto, MapObjectDetailDto } from '@/api/types';

export function useMapObjects(search?: string, category?: string) {
  return useQuery<MapObjectListItemDto[]>({
    queryKey: ['map-objects', search, category],
    queryFn: () => mapObjectsApi.list(search, category),
    retry: false,
  });
}

export function useMapObject(id: string | null) {
  return useQuery<MapObjectDetailDto>({
    queryKey: ['map-object', id],
    queryFn: () => mapObjectsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
