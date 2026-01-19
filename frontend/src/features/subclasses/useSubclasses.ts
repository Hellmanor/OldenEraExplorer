import { useQuery } from '@tanstack/react-query';
import { subclassesApi } from '@/api/client';
import type { SubclassListItemDto, SubclassDetailDto } from '@/api/types';

export function useSubclasses(search?: string) {
  return useQuery<SubclassListItemDto[]>({
    queryKey: ['subclasses', search],
    queryFn: () => subclassesApi.list(search),
    retry: false,
  });
}

export function useSubclass(id: string | null) {
  return useQuery<SubclassDetailDto>({
    queryKey: ['subclass', id],
    queryFn: () => subclassesApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
