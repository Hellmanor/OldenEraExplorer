import { useQuery } from '@tanstack/react-query';
import { abilitiesApi } from '@/api/client';
import type { AbilityListItemDto, AbilityDetailDto } from '@/api/types';

export function useAbilities(search?: string, type?: string) {
  return useQuery<AbilityListItemDto[]>({
    queryKey: ['abilities', search, type],
    queryFn: () => abilitiesApi.list(search, type),
    retry: false,
  });
}

export function useAbility(id: string | null) {
  return useQuery<AbilityDetailDto>({
    queryKey: ['ability', id],
    queryFn: () => abilitiesApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
