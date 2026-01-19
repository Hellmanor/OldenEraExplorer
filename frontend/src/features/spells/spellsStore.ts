import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type SpellSortField = 'name' | 'school' | 'rank' | 'category';

interface SpellsState {
  selectedSpellId: string | null;
  setSelectedSpellId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: SpellSortField;
  sortDirection: SortDirection;
  setSort: (field: SpellSortField, direction: SortDirection) => void;
}

export const useSpellsStore = create<SpellsState>()(
  persist(
    (set) => ({
      selectedSpellId: null,
      setSelectedSpellId: (id) => set({ selectedSpellId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'school',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-spells-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
