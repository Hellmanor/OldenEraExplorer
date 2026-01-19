import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { gameApi } from '../api/client';
import { useImageStore } from '../stores/imageStore';

export function useGameStatus() {
  return useQuery({
    queryKey: ['game', 'status'],
    queryFn: () => gameApi.getStatus(),
    staleTime: 30000,
  });
}

export function useGameDetect() {
  return useMutation({
    mutationFn: () => gameApi.detect(),
  });
}

export function useSetGamePath() {
  return useMutation({
    mutationFn: ({ path, locale }: { path: string; locale?: string }) =>
      gameApi.setPath(path, locale),
    onSuccess: () => {
      useImageStore.getState().reset();
    },
  });
}

export function useLoadGameData() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => gameApi.load(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['game'] });
      queryClient.invalidateQueries({ queryKey: ['units'] });
      queryClient.invalidateQueries({ queryKey: ['models'] });
    },
  });
}
