import { useQuery } from '@tanstack/react-query';
import { heroesApi } from '@/api/client';
import type { HeroListItemDto, HeroDetailDto } from '@/api/types';

export function useHeroes(search?: string) {
  return useQuery<HeroListItemDto[]>({
    queryKey: ['heroes', search],
    queryFn: () => heroesApi.list(search),
    retry: false,
  });
}

export function useHero(id: string | null) {
  return useQuery<HeroDetailDto>({
    queryKey: ['hero', id],
    queryFn: () => heroesApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
