import { useQuery } from '@tanstack/react-query';
import { unitsApi } from '@/api/client';
import type { UnitListItemDto, UnitDetailDto } from '@/api/types';

export function useUnits(search?: string) {
  return useQuery<UnitListItemDto[]>({
    queryKey: ['units', search],
    queryFn: () => unitsApi.list(search),
    retry: false,
  });
}

export function useUnit(id: string | null) {
  return useQuery<UnitDetailDto>({
    queryKey: ['unit', id],
    queryFn: () => unitsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
