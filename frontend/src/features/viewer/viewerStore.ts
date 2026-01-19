import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';

export interface AnimationState {
  name: string;
  clip: string; // clip UUID or name
  playing: boolean;
  time: number;
  duration: number;
}

export interface MaterialInfo {
  uuid: string;
  name: string;
  color: string;
  metalness: number;
  roughness: number;
  wireframe: boolean;
  doubleSided: boolean;
}

export interface SceneNode {
  uuid: string;
  name: string;
  type: string;
  children: SceneNode[];
  visible: boolean;
  vertexCount?: number;
  faceCount?: number;
  materialName?: string;
}

interface ViewerState {
  cameraType: 'perspective' | 'orthographic';
  activeCamera: string;
  nearClip: number;
  farClip: number;
  autoRotate: boolean;
  autoRotateSpeed: number;

  displayMode: 'game-preview' | 'studio';
  showPlatform: boolean;

  showBackground: boolean;
  backgroundColor: string;
  showGrid: boolean;

  usePunctualLights: boolean;
  ambientIntensity: number;
  ambientColor: string;
  directionalIntensity: number;
  directionalColor: string;

  toneMapping: 'linear' | 'aces';
  exposure: number;
  pixelRatioLimit: number;

  animations: AnimationState[];
  playbackSpeed: number;
  loopMode: 'once' | 'repeat' | 'pingpong';

  materials: MaterialInfo[];
  selectedMaterialUuid: string | null;
  globalWireframe: boolean;

  showStats: boolean;
  showSkeleton: boolean;
  pointSize: number;

  sceneGraph: SceneNode | null;
  platformSceneGraph: SceneNode | null;
  selectedNodeUuid: string | null;
  boundingBoxNodes: Set<string>;
  wireframeNodes: Set<string>;
  hiddenNodes: Set<string>;
  soloNode: string | null;

  setCameraType: (type: 'perspective' | 'orthographic') => void;
  setActiveCamera: (camera: string) => void;
  setNearClip: (value: number) => void;
  setFarClip: (value: number) => void;
  setAutoRotate: (enabled: boolean) => void;
  setAutoRotateSpeed: (speed: number) => void;

  setDisplayMode: (mode: 'game-preview' | 'studio') => void;
  setShowPlatform: (show: boolean) => void;

  setShowBackground: (show: boolean) => void;
  setBackgroundColor: (color: string) => void;
  setShowGrid: (show: boolean) => void;

  setUsePunctualLights: (enabled: boolean) => void;
  setAmbientIntensity: (intensity: number) => void;
  setAmbientColor: (color: string) => void;
  setDirectionalIntensity: (intensity: number) => void;
  setDirectionalColor: (color: string) => void;

  setToneMapping: (mode: 'linear' | 'aces') => void;
  setExposure: (value: number) => void;
  setPixelRatioLimit: (limit: number) => void;

  setAnimations: (animations: AnimationState[]) => void;
  updateAnimation: (name: string, updates: Partial<AnimationState>) => void;
  setPlaybackSpeed: (speed: number) => void;
  setLoopMode: (mode: 'once' | 'repeat' | 'pingpong') => void;

  setMaterials: (materials: MaterialInfo[]) => void;
  updateMaterial: (uuid: string, updates: Partial<MaterialInfo>) => void;
  setSelectedMaterialUuid: (uuid: string | null) => void;
  setGlobalWireframe: (enabled: boolean) => void;

  setShowStats: (show: boolean) => void;
  setShowSkeleton: (show: boolean) => void;
  setPointSize: (size: number) => void;

  setSceneGraph: (graph: SceneNode | null) => void;
  setPlatformSceneGraph: (graph: SceneNode | null) => void;
  setSelectedNodeUuid: (uuid: string | null) => void;
  toggleBoundingBox: (uuid: string) => void;
  clearBoundingBoxes: () => void;
  toggleWireframe: (uuid: string) => void;
  clearWireframes: () => void;
  toggleNodeVisibility: (uuid: string) => void;
  setSoloNode: (uuid: string | null) => void;

  resetToDefaults: () => void;
}

const defaultState = {
  cameraType: 'perspective' as const,
  activeCamera: 'default',
  nearClip: 0.1,
  farClip: 1000,
  autoRotate: false,
  autoRotateSpeed: 1,

  displayMode: 'studio' as const,
  showPlatform: false,

  showBackground: false,
  backgroundColor: '#191919',
  showGrid: false,

  usePunctualLights: true,
  ambientIntensity: 0.3,
  ambientColor: '#ffffff',
  directionalIntensity: 0.8 * Math.PI,
  directionalColor: '#ffffff',

  toneMapping: 'linear' as const,
  exposure: 0,
  pixelRatioLimit: 2,

  animations: [],
  playbackSpeed: 1,
  loopMode: 'repeat' as const,

  materials: [],
  selectedMaterialUuid: null,
  globalWireframe: false,

  showStats: false,
  showSkeleton: false,
  pointSize: 1,

  sceneGraph: null,
  platformSceneGraph: null,
  selectedNodeUuid: null,
  boundingBoxNodes: new Set<string>(),
  wireframeNodes: new Set<string>(),
  hiddenNodes: new Set<string>(),
  soloNode: null,
};

export const useViewerStore = create<ViewerState>()(
  persist(
    (set) => ({
      ...defaultState,

  setCameraType: (type) => set({ cameraType: type }),
  setActiveCamera: (camera) => set({ activeCamera: camera }),
  setNearClip: (value) => set({ nearClip: value }),
  setFarClip: (value) => set({ farClip: value }),
  setAutoRotate: (enabled) => set({ autoRotate: enabled }),
  setAutoRotateSpeed: (speed) => set({ autoRotateSpeed: speed }),

  setDisplayMode: (mode) => set({
    displayMode: mode,
    showPlatform: mode === 'game-preview',
  }),
  setShowPlatform: (show) => set({ showPlatform: show }),

  setShowBackground: (show) => set({ showBackground: show }),
  setBackgroundColor: (color) => set({ backgroundColor: color }),
  setShowGrid: (show) => set({ showGrid: show }),

  setUsePunctualLights: (enabled) => set({ usePunctualLights: enabled }),
  setAmbientIntensity: (intensity) => set({ ambientIntensity: intensity }),
  setAmbientColor: (color) => set({ ambientColor: color }),
  setDirectionalIntensity: (intensity) => set({ directionalIntensity: intensity }),
  setDirectionalColor: (color) => set({ directionalColor: color }),

  setToneMapping: (mode) => set({ toneMapping: mode }),
  setExposure: (value) => set({ exposure: value }),
  setPixelRatioLimit: (limit) => set({ pixelRatioLimit: limit }),

  setAnimations: (animations) => set({ animations }),
  updateAnimation: (name, updates) =>
    set((state) => ({
      animations: state.animations.map((anim) =>
        anim.name === name ? { ...anim, ...updates } : anim
      ),
    })),
  setPlaybackSpeed: (speed) => set({ playbackSpeed: speed }),
  setLoopMode: (mode) => set({ loopMode: mode }),

  setMaterials: (materials) => set({ materials }),
  updateMaterial: (uuid, updates) =>
    set((state) => ({
      materials: state.materials.map((mat) =>
        mat.uuid === uuid ? { ...mat, ...updates } : mat
      ),
    })),
  setSelectedMaterialUuid: (uuid) => set({ selectedMaterialUuid: uuid }),
  setGlobalWireframe: (enabled) => set({ globalWireframe: enabled }),

  setShowStats: (show) => set({ showStats: show }),
  setShowSkeleton: (show) => set({ showSkeleton: show }),
  setPointSize: (size) => set({ pointSize: size }),

  setSceneGraph: (graph) => set({ sceneGraph: graph }),
  setPlatformSceneGraph: (graph) => set({ platformSceneGraph: graph }),
  setSelectedNodeUuid: (uuid) => set({ selectedNodeUuid: uuid }),
  toggleBoundingBox: (uuid) =>
    set((state) => {
      const newBoundingBoxNodes = new Set(state.boundingBoxNodes);
      if (newBoundingBoxNodes.has(uuid)) {
        newBoundingBoxNodes.delete(uuid);
      } else {
        newBoundingBoxNodes.add(uuid);
      }
      return { boundingBoxNodes: newBoundingBoxNodes };
    }),
  clearBoundingBoxes: () => set({ boundingBoxNodes: new Set<string>() }),
  toggleWireframe: (uuid) =>
    set((state) => {
      const newWireframeNodes = new Set(state.wireframeNodes);
      if (newWireframeNodes.has(uuid)) {
        newWireframeNodes.delete(uuid);
      } else {
        newWireframeNodes.add(uuid);
      }
      return { wireframeNodes: newWireframeNodes };
    }),
  clearWireframes: () => set({ wireframeNodes: new Set<string>() }),
  toggleNodeVisibility: (uuid) =>
    set((state) => {
      const newHiddenNodes = new Set(state.hiddenNodes);
      if (newHiddenNodes.has(uuid)) {
        newHiddenNodes.delete(uuid);
      } else {
        newHiddenNodes.add(uuid);
      }
      return { hiddenNodes: newHiddenNodes };
    }),
  setSoloNode: (uuid) => set({ soloNode: uuid }),

      resetToDefaults: () => set(defaultState),
    }),
    {
      name: 'viewer-settings',
      storage: createJSONStorage(() => sessionStorage),
      partialize: (state) => ({
        showBackground: state.showBackground,
      }),
    }
  )
);
