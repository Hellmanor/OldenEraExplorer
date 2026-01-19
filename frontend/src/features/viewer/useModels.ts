import { useQuery } from '@tanstack/react-query';
import { modelsApi } from '@/api/client';
import type { UnitListItemDto, MapObjectListItemDto, ArtifactListItemDto } from '@/api/types';

export function useUnitModels(search?: string) {
  return useQuery<UnitListItemDto[]>({
    queryKey: ['models', 'units', search],
    queryFn: () => modelsApi.listUnits(search),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}

export function useMapObjectModels(search?: string) {
  return useQuery<MapObjectListItemDto[]>({
    queryKey: ['models', 'map-objects', search],
    queryFn: () => modelsApi.listMapObjects(search),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}

export function useArtifactModels(search?: string) {
  return useQuery<ArtifactListItemDto[]>({
    queryKey: ['models', 'artifacts', search],
    queryFn: () => modelsApi.listArtifacts(search),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}
