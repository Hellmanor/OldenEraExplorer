import { useQuery } from '@tanstack/react-query';
import { spellsApi } from '@/api/client';
import type { SpellListItemDto, SpellDetailDto } from '@/api/types';

export function useSpells(search?: string) {
  return useQuery<SpellListItemDto[]>({
    queryKey: ['spells', search],
    queryFn: () => spellsApi.list(search),
    retry: false,
  });
}

export function useSpell(id: string | null) {
  return useQuery<SpellDetailDto>({
    queryKey: ['spell', id],
    queryFn: () => spellsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
