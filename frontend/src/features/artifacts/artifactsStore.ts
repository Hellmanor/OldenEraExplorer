import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type ArtifactSortField = 'name' | 'rarity' | 'slot' | 'id';

interface ArtifactsState {
  selectedArtifactId: string | null;
  setSelectedArtifactId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: ArtifactSortField;
  sortDirection: SortDirection;
  setSort: (field: ArtifactSortField, direction: SortDirection) => void;
}

export const useArtifactsStore = create<ArtifactsState>()(
  persist(
    (set) => ({
      selectedArtifactId: null,
      setSelectedArtifactId: (id) => set({ selectedArtifactId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'name',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-artifacts-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
