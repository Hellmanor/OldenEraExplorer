import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type SubclassSortField = 'name' | 'faction' | 'class';

interface SubclassesState {
  selectedSubclassId: string | null;
  setSelectedSubclassId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: SubclassSortField;
  sortDirection: SortDirection;
  setSort: (field: SubclassSortField, direction: SortDirection) => void;
}

export const useSubclassesStore = create<SubclassesState>()(
  persist(
    (set) => ({
      selectedSubclassId: null,
      setSelectedSubclassId: (id) => set({ selectedSubclassId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'faction',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-subclasses-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
