import { useEffect, useRef, useCallback, useState } from 'react';
import GUI from 'lil-gui';
import { useViewerStore } from './viewerStore';
import { attachColorPicker } from '@/lib/colorPicker';
import { useSceneHierarchyGui } from './useSceneHierarchyGui';
import type { CameraPreset } from './ModelViewer';

interface UseViewerGuiOptions {
  containerRef: React.RefObject<HTMLDivElement | null>;
  onResetCamera: () => void;
  onCameraPreset: (preset: CameraPreset) => void;
  isOrphan?: boolean;
}

interface GuiState {
  // Camera
  autoRotate: boolean;
  autoRotateSpeed: number;

  // Display
  displayMode: string;
  showPlatform: boolean;
  showBackground: boolean;
  backgroundColor: string;
  showGrid: boolean; // Grid + Axes combined

  // Lighting
  usePunctualLights: boolean;
  ambientIntensity: number;
  ambientColor: string;
  directionalIntensity: number;
  directionalColor: string;

  // Renderer
  toneMapping: string;
  exposure: number;

  // Animation
  activeAnimation: string;
  playbackSpeed: number;
  loopMode: string;

  // Debug
  showStats: boolean;
  showSkeleton: boolean;
  pointSize: number;
  globalWireframe: boolean;
}

export function useViewerGui({
  containerRef,
  onResetCamera,
  onCameraPreset,
  isOrphan = false,
}: UseViewerGuiOptions) {
  const guiRef = useRef<GUI | null>(null);
  const [guiInstance, setGuiInstance] = useState<GUI | null>(null);
  const stateRef = useRef<GuiState | null>(null);
  const animationFolderRef = useRef<GUI | null>(null);
  const animationControllersRef = useRef<Map<string, ReturnType<GUI['add']>>>(new Map());
  const displayModeControllerRef = useRef<ReturnType<GUI['add']> | null>(null);
  const envControllersRef = useRef<{
    showPlatform: ReturnType<GUI['add']> | null;
    backgroundColor: ReturnType<GUI['add']> | null;
  }>({ showPlatform: null, backgroundColor: null });

  const lightControllersRef = useRef<ReturnType<GUI['add']>[]>([]);

  const {
    autoRotate,
    setAutoRotate,
    autoRotateSpeed,
    setAutoRotateSpeed,

    displayMode,
    setDisplayMode,
    showPlatform,
    setShowPlatform,
    showBackground,
    backgroundColor,
    setBackgroundColor,
    showGrid,
    setShowGrid,

    usePunctualLights,
    setUsePunctualLights,
    ambientIntensity,
    setAmbientIntensity,
    ambientColor,
    setAmbientColor,
    directionalIntensity,
    setDirectionalIntensity,
    directionalColor,
    setDirectionalColor,

    toneMapping,
    setToneMapping,
    exposure,
    setExposure,

    animations,
    setAnimations,
    playbackSpeed,
    setPlaybackSpeed,
    loopMode,
    setLoopMode,

    showStats,
    setShowStats,
    showSkeleton,
    setShowSkeleton,
    pointSize,
    globalWireframe,
    setGlobalWireframe,
  } = useViewerStore();

  const activeAnimation = animations.find((a) => a.playing)?.name || 'None';

  const selectAnimation = useCallback(
    (name: string) => {
      if (name === 'None') {
        setAnimations(
          animations.map((anim) => ({
            ...anim,
            playing: false,
          }))
        );
      } else {
        setAnimations(
          animations.map((anim) => ({
            ...anim,
            playing: anim.name === name,
            time: anim.name === name ? 0 : anim.time,
          }))
        );
      }
    },
    [animations, setAnimations]
  );

  useEffect(() => {
    if (!containerRef.current) return;

    const animationControllers = animationControllersRef.current;

    const state: GuiState = {
      autoRotate,
      autoRotateSpeed,
      displayMode,
      showPlatform,
      showBackground,
      backgroundColor,
      showGrid,
      usePunctualLights,
      ambientIntensity,
      ambientColor,
      directionalIntensity,
      directionalColor,
      toneMapping,
      exposure,
      activeAnimation,
      playbackSpeed,
      loopMode,
      showStats,
      showSkeleton,
      pointSize,
      globalWireframe,
    };
    stateRef.current = state;

    const gui = new GUI({ autoPlace: false, title: 'Viewer Settings' });
    guiRef.current = gui;
    setGuiInstance(gui);

    gui.domElement.style.position = 'absolute';
    gui.domElement.style.top = '0';
    gui.domElement.style.right = '0';
    gui.domElement.style.zIndex = '100';
    gui.domElement.style.maxHeight = '100%';
    gui.domElement.style.overflowY = 'auto';
    gui.domElement.style.setProperty('--width', '280px');

    containerRef.current.appendChild(gui.domElement);

    const cameraFolder = gui.addFolder('Camera');
    cameraFolder.add(state, 'autoRotate').name('Auto Rotate').onChange(setAutoRotate);
    cameraFolder.add(state, 'autoRotateSpeed', 0.1, 5, 0.1).name('Rotate Speed').onChange(setAutoRotateSpeed);

    const cameraActions = {
      resetCamera: onResetCamera,
      front: () => onCameraPreset('front'),
      back: () => onCameraPreset('back'),
      left: () => onCameraPreset('left'),
      right: () => onCameraPreset('right'),
      top: () => onCameraPreset('top'),
      bottom: () => onCameraPreset('bottom'),
      isometric: () => onCameraPreset('isometric'),
    };
    cameraFolder.add(cameraActions, 'resetCamera').name('Reset Camera');

    const styleId = 'lil-gui-presets-grid';
    if (!document.getElementById(styleId)) {
      const style = document.createElement('style');
      style.id = styleId;
      style.textContent = `
        .camera-presets-grid {
          display: grid;
          grid-template-columns: 1fr 1fr 1fr 1fr 2fr;
          gap: 2px;
          padding: 4px;
        }
        .camera-presets-grid button {
          background: var(--widget-color);
          border: none;
          border-radius: var(--widget-border-radius);
          color: var(--text-color);
          cursor: pointer;
          font-family: var(--font-family);
          font-size: var(--font-size);
          height: var(--widget-height);
          text-align: center;
        }
        .camera-presets-grid button:hover {
          background: var(--hover-color);
        }
        .camera-presets-grid button:active {
          background: var(--focus-color);
        }
        .camera-presets-grid .preset-front { grid-row: 1; grid-column: 2 / 4; }
        .camera-presets-grid .preset-top { grid-row: 1; grid-column: 5; }
        .camera-presets-grid .preset-left { grid-row: 2; grid-column: 1 / 3; }
        .camera-presets-grid .preset-right { grid-row: 2; grid-column: 3 / 5; }
        .camera-presets-grid .preset-back { grid-row: 3; grid-column: 2 / 4; }
        .camera-presets-grid .preset-bottom { grid-row: 3; grid-column: 5; }
        .camera-presets-grid .preset-isometric { grid-row: 4; grid-column: 1 / 6; }
      `;
      document.head.appendChild(style);
    }

    const gridContainer = document.createElement('div');
    gridContainer.className = 'camera-presets-grid';

    const presetButtons = [
      { key: 'front', label: 'Front', class: 'preset-front' },
      { key: 'top', label: 'Top', class: 'preset-top' },
      { key: 'left', label: 'Left', class: 'preset-left' },
      { key: 'right', label: 'Right', class: 'preset-right' },
      { key: 'back', label: 'Back', class: 'preset-back' },
      { key: 'bottom', label: 'Bottom', class: 'preset-bottom' },
      { key: 'isometric', label: 'Isometric', class: 'preset-isometric' },
    ] as const;

    presetButtons.forEach(({ key, label, class: className }) => {
      const button = document.createElement('button');
      button.textContent = label;
      button.className = className;
      button.addEventListener('click', () => cameraActions[key]());
      gridContainer.appendChild(button);
    });

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const cameraChildren = (cameraFolder as any).$children as HTMLElement;
    if (cameraChildren) {
      cameraChildren.appendChild(gridContainer);
    }

    cameraFolder.open();

    const envFolder = gui.addFolder('Environment');

    const updateEnvControllerVisibility = (mode: 'game-preview' | 'studio') => {
      const { showPlatform, backgroundColor } = envControllersRef.current;
      if (mode === 'game-preview') {
        showPlatform?.show();
        backgroundColor?.hide();
      } else {
        showPlatform?.hide();
        backgroundColor?.show();
      }
    };

    displayModeControllerRef.current = envFolder
      .add(state, 'displayMode', { 'Game Preview': 'game-preview', Studio: 'studio' })
      .name('Display Mode')
      .onChange((value: string) => {
        setDisplayMode(value as 'game-preview' | 'studio');
        updateEnvControllerVisibility(value as 'game-preview' | 'studio');
      });

    // Disable display mode controller for orphans
    if (isOrphan) {
      displayModeControllerRef.current.disable();
    }

    envControllersRef.current.showPlatform = envFolder
      .add(state, 'showPlatform')
      .name('Enable Platform')
      .onChange(setShowPlatform);
    envControllersRef.current.backgroundColor = envFolder
      .addColor(state, 'backgroundColor')
      .name('Background Color')
      .onChange(setBackgroundColor);
    attachColorPicker(envControllersRef.current.backgroundColor);

    envFolder.add(state, 'showGrid').name('Grid + Axes').onChange(setShowGrid);
    envFolder.close();

    updateEnvControllerVisibility(displayMode);

    const lightFolder = gui.addFolder('Lighting');

    const updateLightControllersEnabled = (enabled: boolean) => {
      lightControllersRef.current.forEach((ctrl) => {
        ctrl.enable(enabled);
      });
    };

    lightFolder.add(state, 'usePunctualLights').name('Enable Lights').onChange((value: boolean) => {
      setUsePunctualLights(value);
      updateLightControllersEnabled(value);
    });

    const ambientIntensityCtrl = lightFolder.add(state, 'ambientIntensity', 0, 2, 0.01).name('Ambient Intensity').onChange(setAmbientIntensity);
    const ambientColorCtrl = lightFolder.addColor(state, 'ambientColor').name('Ambient Color').onChange(setAmbientColor);
    attachColorPicker(ambientColorCtrl);
    const directIntensityCtrl = lightFolder
      .add(state, 'directionalIntensity', 0, 5, 0.01)
      .name('Direct Intensity')
      .onChange(setDirectionalIntensity);
    const directColorCtrl = lightFolder.addColor(state, 'directionalColor').name('Direct Color').onChange(setDirectionalColor);
    attachColorPicker(directColorCtrl);

    lightControllersRef.current = [ambientIntensityCtrl, ambientColorCtrl, directIntensityCtrl, directColorCtrl];

    updateLightControllersEnabled(usePunctualLights);

    lightFolder
      .add(state, 'toneMapping', { Linear: 'linear', 'ACES Filmic': 'aces' })
      .name('Tone Mapping')
      .onChange((value: string) => setToneMapping(value as 'linear' | 'aces'));
    lightFolder.add(state, 'exposure', -10, 10, 0.1).name('Exposure').onChange(setExposure);
    lightFolder.close();

    const animFolder = gui.addFolder('Animation');
    animationFolderRef.current = animFolder;

    animFolder.add(state, 'playbackSpeed', 0, 2, 0.1).name('Playback Speed').onChange(setPlaybackSpeed);
    animFolder
      .add(state, 'loopMode', { Once: 'once', Repeat: 'repeat', 'Ping-Pong': 'pingpong' })
      .name('Loop Mode')
      .onChange((value: string) => setLoopMode(value as 'once' | 'repeat' | 'pingpong'));
    animFolder.open();

    const debugFolder = gui.addFolder('Debug');
    debugFolder.add(state, 'showStats').name('Show Stats').onChange(setShowStats);
    debugFolder.add(state, 'showSkeleton').name('Show Skeleton').onChange(setShowSkeleton);
    debugFolder.add(state, 'globalWireframe').name('Wireframe').onChange(setGlobalWireframe);
    debugFolder.close();

    gui.close();

    return () => {
      gui.destroy();
      guiRef.current = null;
      setGuiInstance(null);
      stateRef.current = null;
      animationFolderRef.current = null;
      animationControllers.clear();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [containerRef]);

  useEffect(() => {
    const { showPlatform, backgroundColor } = envControllersRef.current;
    if (displayMode === 'game-preview') {
      showPlatform?.show();
      backgroundColor?.hide();
    } else {
      showPlatform?.hide();
      backgroundColor?.show();
    }
  }, [displayMode]);

  // Update display mode controller enabled state when isOrphan changes
  useEffect(() => {
    const controller = displayModeControllerRef.current;
    if (!controller) return;

    if (isOrphan) {
      controller.disable();
    } else {
      controller.enable();
    }
  }, [isOrphan]);

  useEffect(() => {
    const state = stateRef.current;
    const gui = guiRef.current;
    if (!state || !gui) return;

    state.autoRotate = autoRotate;
    state.autoRotateSpeed = autoRotateSpeed;
    state.displayMode = displayMode;
    state.showPlatform = showPlatform;
    state.showBackground = showBackground;
    state.backgroundColor = backgroundColor;
    state.showGrid = showGrid;
    state.usePunctualLights = usePunctualLights;
    state.ambientIntensity = ambientIntensity;
    state.ambientColor = ambientColor;
    state.directionalIntensity = directionalIntensity;
    state.directionalColor = directionalColor;
    state.toneMapping = toneMapping;
    state.exposure = exposure;
    state.playbackSpeed = playbackSpeed;
    state.loopMode = loopMode;
    state.showStats = showStats;
    state.showSkeleton = showSkeleton;
    state.pointSize = pointSize;
    state.globalWireframe = globalWireframe;

    gui.controllersRecursive().forEach((controller) => {
      controller.updateDisplay();
    });
  }, [
    autoRotate,
    autoRotateSpeed,
    displayMode,
    showPlatform,
    showBackground,
    backgroundColor,
    showGrid,
    usePunctualLights,
    ambientIntensity,
    ambientColor,
    directionalIntensity,
    directionalColor,
    toneMapping,
    exposure,
    playbackSpeed,
    loopMode,
    showStats,
    showSkeleton,
    pointSize,
    globalWireframe,
  ]);

  useEffect(() => {
    const animFolder = animationFolderRef.current;
    const state = stateRef.current;
    if (!animFolder || !state) return;

    const existingController = animationControllersRef.current.get('activeAnimation');
    if (existingController) {
      existingController.destroy();
      animationControllersRef.current.delete('activeAnimation');
    }

    const animOptions: Record<string, string> = { None: 'None' };
    animations.forEach((anim) => {
      animOptions[anim.name] = anim.name;
    });

    state.activeAnimation = activeAnimation;

    if (animations.length > 0) {
      const controller = animFolder
        .add(state, 'activeAnimation', animOptions)
        .name('Animation')
        .onChange((value: string) => selectAnimation(value));

      const folderChildren = animFolder.domElement.querySelector('.children');
      if (folderChildren && controller.domElement.parentElement) {
        folderChildren.insertBefore(controller.domElement.parentElement, folderChildren.firstChild);
      }

      animationControllersRef.current.set('activeAnimation', controller);
    }
  }, [animations, activeAnimation, selectAnimation]);

  useSceneHierarchyGui({ gui: guiInstance });

  return guiRef;
}
