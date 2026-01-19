import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type AbilitySortField = 'name' | 'type' | 'id';

interface AbilitiesState {
  selectedAbilityId: string | null;
  setSelectedAbilityId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: AbilitySortField;
  sortDirection: SortDirection;
  setSort: (field: AbilitySortField, direction: SortDirection) => void;
}

export const useAbilitiesStore = create<AbilitiesState>()(
  persist(
    (set) => ({
      selectedAbilityId: null,
      setSelectedAbilityId: (id) => set({ selectedAbilityId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'id',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-abilities-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
