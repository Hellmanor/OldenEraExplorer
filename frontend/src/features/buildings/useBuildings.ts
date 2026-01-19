import { useQuery } from '@tanstack/react-query';
import { buildingsApi } from '@/api/client';
import type { BuildingListItemDto, BuildingDetailDto } from '@/api/types';

export function useBuildings(search?: string, faction?: string, category?: string) {
  return useQuery<BuildingListItemDto[]>({
    queryKey: ['buildings', search, faction, category],
    queryFn: () => buildingsApi.list(search, faction, category),
    retry: false,
  });
}

export function useBuilding(id: string | null) {
  return useQuery<BuildingDetailDto>({
    queryKey: ['building', id],
    queryFn: () => buildingsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
