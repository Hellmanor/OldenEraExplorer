import { useQuery } from '@tanstack/react-query';
import { factionLawsApi } from '@/api/client';
import type { FactionLawListItemDto, FactionLawDetailDto } from '@/api/types';

export function useFactionLaws(search?: string) {
  return useQuery<FactionLawListItemDto[]>({
    queryKey: ['factionLaws', search],
    queryFn: () => factionLawsApi.list(search),
    retry: false,
  });
}

export function useFactionLaw(id: string | null) {
  return useQuery<FactionLawDetailDto>({
    queryKey: ['factionLaw', id],
    queryFn: () => factionLawsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
