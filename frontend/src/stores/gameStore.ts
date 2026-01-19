import { create } from 'zustand';

interface GameState {
  isGameReady: boolean;
  setGameReady: (ready: boolean) => void;
}

export const useGameStore = create<GameState>((set) => ({
  isGameReady: false,
  setGameReady: (ready) => set({ isGameReady: ready }),
}));
