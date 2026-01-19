import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type UnitSortField = 'name' | 'tier' | 'faction' | 'id';

interface UnitsState {
  selectedUnitId: string | null;
  setSelectedUnitId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: UnitSortField;
  sortDirection: SortDirection;
  setSort: (field: UnitSortField, direction: SortDirection) => void;
}

export const useUnitsStore = create<UnitsState>()(
  persist(
    (set) => ({
      selectedUnitId: null,
      setSelectedUnitId: (id) => set({ selectedUnitId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'faction',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-units-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
