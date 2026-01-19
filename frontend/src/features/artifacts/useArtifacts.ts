import { useQuery } from '@tanstack/react-query';
import { artifactsApi } from '@/api/client';
import type { ArtifactListItemDto, ArtifactDetailDto } from '@/api/types';

export function useArtifacts(search?: string) {
  return useQuery<ArtifactListItemDto[]>({
    queryKey: ['artifacts', search],
    queryFn: () => artifactsApi.list(search),
    retry: false,
  });
}

export function useArtifact(id: string | null) {
  return useQuery<ArtifactDetailDto>({
    queryKey: ['artifact', id],
    queryFn: () => artifactsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
