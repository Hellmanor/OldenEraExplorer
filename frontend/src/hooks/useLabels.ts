import { useQuery } from '@tanstack/react-query';
import { gameApi, labelsApi } from '../api/client';
import type { ColumnLabelsDto } from '../api/types';

const defaultColumnLabels: ColumnLabelsDto = {
  name: 'Name',
  tier: 'Tier',
  faction: 'Faction',
  id: 'ID',
  category: 'Category',
  school: 'Magic School',
  level: 'Level',
  class: 'Class',
};

export function useLabels() {
  const labelsQuery = useQuery({
    queryKey: ['labels'],
    queryFn: () => labelsApi.getLabels(),
    staleTime: 60000,
  });

  const statusQuery = useQuery({
    queryKey: ['game', 'status'],
    queryFn: () => gameApi.getStatus(),
    staleTime: 30000,
  });

  const uiLabels = statusQuery.data?.uiLabels ?? {};

  const label = (key: string, ...args: (string | number)[]): string => {
    let text = uiLabels[key] ?? key;

    args.forEach((arg, index) => {
      text = text.replace(`{${index}}`, String(arg));
    });

    return text;
  };

  return {
    label,
    uiLabels,
    columns: labelsQuery.data?.columns ?? defaultColumnLabels,
    isLoading: labelsQuery.isLoading || statusQuery.isLoading,
  };
}

export function useColumnLabels(): ColumnLabelsDto {
  const { columns } = useLabels();
  return columns;
}
