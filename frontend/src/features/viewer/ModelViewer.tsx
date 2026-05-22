import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from 'react';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { DRACOLoader } from 'three/examples/jsm/loaders/DRACOLoader.js';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { clone as cloneSkeleton } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { Line2 } from 'three/examples/jsm/lines/Line2.js';
import { LineMaterial } from 'three/examples/jsm/lines/LineMaterial.js';
import { LineGeometry } from 'three/examples/jsm/lines/LineGeometry.js';
import StatsJs from 'stats.js';
import { useViewerStore } from './viewerStore';
import { useLabels } from '@/hooks/useLabels';
import type { AnimationState, MaterialInfo, SceneNode } from './viewerStore';

export interface ModelViewerHandle {
  takeScreenshot: () => string | null;
  resetCamera: () => void;
  setCameraPreset: (preset: CameraPreset) => void;
}

export type CameraPreset = 'front' | 'back' | 'left' | 'right' | 'top' | 'bottom' | 'isometric';

interface BoneVertexMapping {
  skinnedMesh: THREE.SkinnedMesh;
  vertexIndices: number[];
  boneIndices: Set<number>;
}

interface BoneWireframeMapping {
  skinnedMesh: THREE.SkinnedMesh;
  triangleIndices: number[];
  vertexIndices: Set<number>;
}

interface ModelViewerProps {
  glbUrl: string;
  onModelLoaded?: () => void;
  statsContainer?: HTMLElement | null;
  unitScale?: number | null;
  faction?: string | null;
  customTextureUrl?: string | null;
}

class ThreeViewer {
  private container: HTMLElement;
  private renderer: THREE.WebGLRenderer;
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private controls: OrbitControls;
  private pmremGenerator: THREE.PMREMGenerator;
  private neutralEnvironment: THREE.Texture | null = null;
  private gameEnvironment: THREE.Texture | null = null;
  private gameBackground: THREE.Texture | null = null;

  private content: THREE.Group | null = null;
  private platformGroup: THREE.Group | null = null;
  private platformScene: THREE.Group | null = null;
  private platformSize: THREE.Vector3 | null = null; 
  private platformInitialCenter: THREE.Vector3 | null = null; 
  private defaultCameraTarget: THREE.Vector3 | null = null;
  private defaultCameraDistance: number = 5;
  private mixer: THREE.AnimationMixer | null = null;
  private actions: Map<string, THREE.AnimationAction> = new Map();
  private platformMixer: THREE.AnimationMixer | null = null;
  private platformActions: Map<string, THREE.AnimationAction> = new Map();

  private lights: THREE.Light[] = [];
  private gridHelper: THREE.GridHelper | null = null;
  private axesHelper: THREE.Group | null = null;
  private skeletonHelper: THREE.SkeletonHelper | null = null;
  private stats: StatsJs | null = null;
  private statsContainer: HTMLElement | null = null;

  private boundingBoxHelpers: Map<string, THREE.BoxHelper> = new Map();
  private boneBoundingBoxHelpers: Map<string, { helper: THREE.LineSegments; mapping: BoneVertexMapping }> = new Map();
  private boneWireframeHelpers: Map<string, { helper: THREE.LineSegments; mapping: BoneWireframeMapping; positions: Float32Array }> = new Map();

  private animationFrameId: number | null = null;
  private prevTime = 0;
  private disposed = false;
  private resizeObserver: ResizeObserver | null = null;
  private skyBackgroundImage: HTMLImageElement | null = null;
  private skyBackgroundPosition = 'center center';

  private materialRegistry: Map<string, THREE.Material> = new Map();
  private customTextures: THREE.Texture[] = [];
  private customTextureLoadVersion = 0;
  private originalMaterialStates: Map<
    string,
    {
      map: THREE.Texture | null;
      alphaTest: number;
      transparent: boolean;
      depthWrite: boolean;
      side: THREE.Side;
    }
  > = new Map();  private backgroundColor = new THREE.Color('#191919');

  constructor(container: HTMLElement, statsContainer?: HTMLElement | null) {
    this.container = container;
    this.statsContainer = statsContainer || null;

    this.scene = new THREE.Scene();
    this.scene.background = this.backgroundColor;

    const aspect = container.clientWidth / container.clientHeight;
    this.camera = new THREE.PerspectiveCamera(60, aspect, 0.01, 1000);
    this.camera.position.set(0, 0, 5);
    this.scene.add(this.camera);

    this.renderer = new THREE.WebGLRenderer({
      antialias: true,
      preserveDrawingBuffer: true,
      alpha: true,
    });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    this.renderer.toneMapping = THREE.LinearToneMapping;
    this.renderer.toneMappingExposure = 1;
    
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.appendChild(this.renderer.domElement);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.screenSpacePanning = true;

    this.pmremGenerator = new THREE.PMREMGenerator(this.renderer);
    this.pmremGenerator.compileEquirectangularShader();

    const roomEnv = new RoomEnvironment();
    this.neutralEnvironment = this.pmremGenerator.fromScene(roomEnv).texture;
    roomEnv.dispose();
    this.scene.environment = this.neutralEnvironment;

    this.platformGroup = new THREE.Group();
    this.scene.add(this.platformGroup);

    this.resizeObserver = new ResizeObserver(this.handleResize);
    this.resizeObserver.observe(container);

    this.animate(0);
  }

  private handleResize = () => {
    if (this.disposed) return;

    const width = this.container.clientWidth;
    const height = this.container.clientHeight;

    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height);

    if (this.axesHelper) {
      this.axesHelper.traverse((child) => {
        if (child instanceof Line2) {
          (child.material as LineMaterial).resolution.set(width, height);
        }
      });
    }
  };

  private animate = (time: number) => {
    if (this.disposed) return;

    this.animationFrameId = requestAnimationFrame(this.animate);

    const dt = (time - this.prevTime) / 1000;
    this.prevTime = time;

    this.controls.update();

    if (this.mixer) {
      this.mixer.update(dt);
    }
    if (this.platformMixer) {
      this.platformMixer.update(dt);
    }

    if (this.stats) {
      this.stats.update();
    }

    this.boundingBoxHelpers.forEach((helper) => {
      helper.update();
    });

    this.updateBoneBoundingBoxHelpers();
    this.updateBoneWireframeHelpers();

    this.renderer.render(this.scene, this.camera);
  };

  async loadModel(url: string): Promise<{ scene: THREE.Group; animations: THREE.AnimationClip[] }> {
    const loader = new GLTFLoader();

    const dracoLoader = new DRACOLoader();
    dracoLoader.setDecoderPath('https://www.gstatic.com/draco/versioned/decoders/1.5.6/');
    loader.setDRACOLoader(dracoLoader);

    return new Promise((resolve, reject) => {
      loader.load(
        url,
        (gltf) => {
          const scene = gltf.scene || gltf.scenes[0];
          const clips = gltf.animations || [];

          if (!scene) {
            reject(new Error('Model contains no scene'));
            return;
          }

          const clonedScene = cloneSkeleton(scene) as THREE.Group;
          clonedScene.updateMatrixWorld(true);

          resolve({ scene: clonedScene, animations: clips });
        },
        undefined,
        (error) => {
          reject(error);
        }
      );
    });
  }

  setContent(
    modelScene: THREE.Group,
    clips: THREE.AnimationClip[],
    onAnimationsReady?: (animations: AnimationState[]) => void,
    skipCameraSetup = false
  ) {
    if (skipCameraSetup) {
      this.clearModel();
    } else {
      this.clear();
    }

    this.content = modelScene;

    modelScene.updateMatrixWorld(true);

    
    let animationStates: AnimationState[] = [];
    if (clips.length > 0) {
      this.mixer = new THREE.AnimationMixer(modelScene);

      
      const lowerNames = clips.map((clip) => clip.name.toLowerCase());
      let defaultIndex = lowerNames.findIndex((name) => name === 'idle');
      if (defaultIndex < 0) {
        defaultIndex = lowerNames.findIndex(
          (name) => name.startsWith('idle') && !name.includes('rare')
        );
      }
      if (defaultIndex < 0) {
        defaultIndex = lowerNames.findIndex((name) => name.includes('idle'));
      }
      if (defaultIndex < 0) {
        defaultIndex = 0; 
      }

      
      animationStates = clips.map((clip, index) => ({
        name: clip.name,
        clip: clip.uuid,
        playing: index === defaultIndex,
        time: 0,
        duration: clip.duration,
      }));

      
      clips.forEach((clip, index) => {
        const action = this.mixer!.clipAction(clip);
        this.actions.set(clip.name, action);

        if (index === defaultIndex) {
          action.play();
        }
      });

      
      this.mixer.update(0);
      modelScene.updateMatrixWorld(true);
    }

    const box = this.computeSkinnedBoundingBox(modelScene);
    const modelSize = box.getSize(new THREE.Vector3());

    console.log(`[setContent] model="${modelScene.name}" bbox.min.y=${box.min.y.toFixed(6)} size=[${modelSize.x.toFixed(4)}, ${modelSize.y.toFixed(4)}, ${modelSize.z.toFixed(4)}]`);

    if (!skipCameraSetup) {
      this.controls.reset();

      const size = modelSize.length();
      this.controls.maxDistance = size * 10;
      this.camera.near = size / 100;
      this.camera.far = size * 100;
      this.camera.updateProjectionMatrix();

      const targetY = modelSize.y / 2 + size * 0.15;
      const distance = Math.sqrt(0.49 + 0.04 + 0.49) * size * 1.2;

      this.defaultCameraTarget = new THREE.Vector3(0, targetY, 0);
      this.defaultCameraDistance = distance;

      this.controls.target.copy(this.defaultCameraTarget);
      this.camera.position.set(0, targetY, distance);
      this.camera.lookAt(0, targetY, 0);
      this.controls.update();
      this.setCameraPreset('isometric');

      console.log(`[Camera] model="${modelScene.name}" size=${size.toFixed(4)} near=${(size/100).toFixed(6)} far=${(size*100).toFixed(2)}`);

      this.controls.saveState();

      this.updateGridSize();
    }

    this.scene.add(modelScene);

    onAnimationsReady?.(animationStates);

    const materials: MaterialInfo[] = [];
    const seenMaterials = new Set<string>();

    modelScene.traverse((obj) => {
      if (obj instanceof THREE.Mesh) {
        const mats = Array.isArray(obj.material) ? obj.material : [obj.material];

        mats.forEach((mat) => {
          this.materialRegistry.set(mat.uuid, mat);

          if (!seenMaterials.has(mat.uuid)) {
            seenMaterials.add(mat.uuid);

            let color = '#ffffff';
            let metalness = 0;
            let roughness = 1;

            if (mat instanceof THREE.MeshStandardMaterial || mat instanceof THREE.MeshPhysicalMaterial) {
              color = '#' + mat.color.getHexString();
              metalness = mat.metalness;
              roughness = mat.roughness;
            } else if ('color' in mat && mat.color instanceof THREE.Color) {
              color = '#' + mat.color.getHexString();
            }

            materials.push({
              uuid: mat.uuid,
              name: mat.name || 'Unnamed Material',
              color,
              metalness,
              roughness,
              wireframe: mat.wireframe || false,
              doubleSided: mat.side === THREE.DoubleSide,
            });
          }
        });
      }
    });

    return { materials, sceneGraph: this.buildSceneGraph(modelScene) };
  }

  private buildSceneGraph(obj: THREE.Object3D): SceneNode {
    const node: SceneNode = {
      uuid: obj.uuid,
      name: obj.name || obj.type,
      type: obj.type,
      children: [],
      visible: obj.visible,
    };

    if (obj instanceof THREE.Mesh) {
      const geometry = obj.geometry;
      if (geometry) {
        const posAttr = geometry.getAttribute('position');
        node.vertexCount = posAttr ? posAttr.count : 0;
        node.faceCount = geometry.index ? geometry.index.count / 3 : (posAttr ? posAttr.count / 3 : 0);
      }
      const mat = obj.material;
      if (Array.isArray(mat)) {
        node.materialName = mat.map((m) => m.name || 'Unnamed').join(', ');
      } else {
        node.materialName = mat.name || 'Unnamed';
      }
    }

    obj.children.forEach((child) => {
      node.children.push(this.buildSceneGraph(child));
    });

    return node;
  }
  private hasTextureMap(
    material: THREE.Material
  ): material is THREE.Material & { map: THREE.Texture | null } {
    return 'map' in material;
  }

  private saveOriginalMaterialState(material: THREE.Material) {
    if (this.originalMaterialStates.has(material.uuid)) {
      return;
    }

    this.originalMaterialStates.set(material.uuid, {
      map: this.hasTextureMap(material) ? material.map ?? null : null,
      alphaTest: material.alphaTest,
      transparent: material.transparent,
      depthWrite: material.depthWrite,
      side: material.side,
    });
  }

  private configureUploadedDiffuseTexture(
    texture: THREE.Texture,
    originalMap: THREE.Texture
  ) {
    
    
    texture.colorSpace = originalMap.colorSpace || THREE.SRGBColorSpace;

    
    texture.flipY = true;

    texture.wrapS = originalMap.wrapS;
    texture.wrapT = originalMap.wrapT;

    texture.offset.copy(originalMap.offset);
    texture.repeat.copy(originalMap.repeat);
    texture.center.copy(originalMap.center);
    texture.rotation = originalMap.rotation;

    texture.matrixAutoUpdate = originalMap.matrixAutoUpdate;
    if (!originalMap.matrixAutoUpdate) {
      texture.matrix.copy(originalMap.matrix);
    }

    texture.generateMipmaps = originalMap.generateMipmaps;
    texture.minFilter = originalMap.minFilter;
    texture.magFilter = originalMap.magFilter;
    texture.anisotropy = originalMap.anisotropy;

    texture.needsUpdate = true;
  }

  private restoreOriginalMaterialStates() {
    this.materialRegistry.forEach((material) => {
      const state = this.originalMaterialStates.get(material.uuid);

      if (!state) {
        return;
      }

      if (this.hasTextureMap(material)) {
        material.map = state.map;
      }

      material.alphaTest = state.alphaTest;
      material.transparent = state.transparent;
      material.depthWrite = state.depthWrite;
      material.side = state.side;
      material.needsUpdate = true;
    });

    this.originalMaterialStates.clear();
  }

  private disposeCustomTextures() {
    this.customTextures.forEach((texture) => texture.dispose());
    this.customTextures = [];
  }

  async setCustomTextureUrl(url: string | null) {
    const version = ++this.customTextureLoadVersion;

    if (!url) {
      this.restoreOriginalMaterialStates();
      this.disposeCustomTextures();
      return;
    }

    const loader = new THREE.TextureLoader();
    const uploadedTexture = await loader.loadAsync(url);

    if (this.disposed || version !== this.customTextureLoadVersion) {
      uploadedTexture.dispose();
      return;
    }

    const texturesInUse: THREE.Texture[] = [];
    let firstTextureAssigned = false;

    this.materialRegistry.forEach((material) => {
      if (!this.hasTextureMap(material)) {
        return;
      }

      const originalMap = material.map;

      
      if (!originalMap) {
        return;
      }

      this.saveOriginalMaterialState(material);

      const textureForMaterial = firstTextureAssigned
        ? uploadedTexture.clone()
        : uploadedTexture;

      firstTextureAssigned = true;

      this.configureUploadedDiffuseTexture(textureForMaterial, originalMap);

      material.map = textureForMaterial;
      material.needsUpdate = true;

      texturesInUse.push(textureForMaterial);
    });

    this.disposeCustomTextures();

    if (texturesInUse.length === 0) {
      uploadedTexture.dispose();
      return;
    }

    this.customTextures = texturesInUse;
  }

  private clear() {
    this.restoreOriginalMaterialStates();
    this.disposeCustomTextures();

    if (this.content) {
      this.scene.remove(this.content);

      
      this.content.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
          if (obj.geometry) {
            obj.geometry.dispose();
          }
          const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
          materials.forEach((mat) => {
            if (mat) {
              
              const textureProps = [
                'map', 'lightMap', 'bumpMap', 'normalMap', 'specularMap',
                'envMap', 'alphaMap', 'aoMap', 'displacementMap',
                'emissiveMap', 'gradientMap', 'metalnessMap', 'roughnessMap'
              ] as const;

              textureProps.forEach((prop) => {
                const texture = (mat as unknown as Record<string, THREE.Texture | undefined>)[prop];
                if (texture instanceof THREE.Texture) {
                  texture.dispose();
                }
              });

              mat.dispose();
            }
          });
        }
      });

      this.content = null;
    }

    
    if (this.mixer) {
      this.mixer.stopAllAction();
      this.mixer = null;
    }
    this.actions.clear();

    
    this.materialRegistry.clear();

    
    if (this.platformScene) {
      this.platformGroup?.remove(this.platformScene);
      this.platformScene = null;
    }
    this.platformSize = null;
    this.defaultCameraTarget = null;
    this.defaultCameraDistance = 5;

    
    this.clearHelpers();
  }

  
  private clearModel() {
    this.restoreOriginalMaterialStates();
    this.disposeCustomTextures();

    
    if (this.content) {
      this.scene.remove(this.content);
      this.content.traverse((node) => {
        if (node instanceof THREE.Mesh) {
          node.geometry?.dispose();
          const materials = Array.isArray(node.material) ? node.material : [node.material];
          materials.forEach((mat) => {
            if (mat) {
              Object.values(mat).forEach((value) => {
                if (value instanceof THREE.Texture) {
                  value.dispose();
                }
              });
              mat.dispose();
            }
          });
        }
      });
      this.content = null;
    }

    
    if (this.mixer) {
      this.mixer.stopAllAction();
      this.mixer = null;
    }
    this.actions.clear();

    
    this.materialRegistry.clear();

    
    this.clearHelpers();
  }

  
  syncAnimations(storeAnimations: AnimationState[], loopMode: 'once' | 'repeat' | 'pingpong') {
    storeAnimations.forEach((anim) => {
      const action = this.actions.get(anim.name);
      if (!action) return;

      
      switch (loopMode) {
        case 'once':
          action.setLoop(THREE.LoopOnce, 1);
          action.clampWhenFinished = true;
          break;
        case 'repeat':
          action.setLoop(THREE.LoopRepeat, Infinity);
          action.clampWhenFinished = false;
          break;
        case 'pingpong':
          action.setLoop(THREE.LoopPingPong, Infinity);
          action.clampWhenFinished = false;
          break;
      }

      
      action.setEffectiveTimeScale(1);
      if (anim.playing) {
        action.play();
      } else {
        action.stop();
      }

      
      if (anim.playing && Number.isFinite(anim.time) && anim.duration > 0) {
        const clampedTime = Math.max(0, Math.min(anim.time, anim.duration));
        if (Math.abs(action.time - clampedTime) > 1e-3) {
          action.time = clampedTime;
        }
      }
    });
  }

  setPlaybackSpeed(speed: number) {
    if (this.mixer) {
      this.mixer.timeScale = speed;
    }
    
    if (this.platformMixer) {
      this.platformMixer.timeScale = speed;
    }
  }

  
  syncPlatformAnimations(loopMode: 'once' | 'repeat' | 'pingpong') {
    this.platformActions.forEach((action) => {
      switch (loopMode) {
        case 'once':
          action.setLoop(THREE.LoopOnce, 1);
          action.clampWhenFinished = true;
          break;
        case 'repeat':
          action.setLoop(THREE.LoopRepeat, Infinity);
          action.clampWhenFinished = false;
          break;
        case 'pingpong':
          action.setLoop(THREE.LoopPingPong, Infinity);
          action.clampWhenFinished = false;
          break;
      }
    });
  }

  
  setBackground(show: boolean, color: string, displayMode: 'studio' | 'game-preview') {
    this.backgroundColor.set(color);

    if (displayMode === 'studio') {
      this.renderer.setClearAlpha(1);
      this.scene.background = show ? this.neutralEnvironment : this.backgroundColor;
    } else {
      if (show) {
        
        this.renderer.setClearAlpha(1);
        this.scene.background = this.gameEnvironment ?? this.backgroundColor;
      } else {
        
        this.renderer.setClearAlpha(0);
        this.scene.background = null;
      }
    }
  }

  setCanvasSkyBackground(url: string | null, position = 'center center') {
    const container = this.renderer.domElement.parentElement;
    if (!container) return;
    if (url) {
      container.style.backgroundImage = `url(${url})`;
      container.style.backgroundSize = 'cover';
      container.style.backgroundPosition = position;
      this.skyBackgroundPosition = position;
      const img = new Image();
      img.onload = () => { this.skyBackgroundImage = img; };
      img.src = url;
    } else {
      container.style.backgroundImage = '';
      this.skyBackgroundImage = null;
    }
  }

  setEnvironment(displayMode: 'studio' | 'game-preview') {
    if (displayMode === 'studio') {
      this.scene.environment = this.neutralEnvironment;
    } else {
      this.scene.environment = this.gameEnvironment ?? this.neutralEnvironment;
    }
  }

  setToneMapping(mode: 'linear' | 'aces', exposure: number) {
    this.renderer.toneMapping = mode === 'aces' ? THREE.ACESFilmicToneMapping : THREE.LinearToneMapping;
    this.renderer.toneMappingExposure = Math.pow(2, exposure);
  }

  setPixelRatioLimit(limit: number) {
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, limit));
  }

  
  updateLights(
    usePunctual: boolean,
    ambientIntensity: number,
    ambientColor: string,
    directionalIntensity: number,
    directionalColor: string,
    
    zenithDeg = 59.5,
    azimuthDeg = 322
  ) {
    
    this.lights.forEach((light) => {
      light.parent?.remove(light);
    });
    this.lights = [];

    if (!usePunctual) return;

    const ambient = new THREE.AmbientLight(ambientColor, ambientIntensity);
    ambient.name = 'ambient_light';
    this.scene.add(ambient);
    this.lights.push(ambient);

    const directional = new THREE.DirectionalLight(directionalColor, directionalIntensity);
    const zenithRad = (zenithDeg * Math.PI) / 180;
    const azimuthRad = (azimuthDeg * Math.PI) / 180;
    directional.position.set(
      Math.sin(zenithRad) * Math.sin(azimuthRad),
      Math.cos(zenithRad),
      Math.sin(zenithRad) * Math.cos(azimuthRad)
    );
    directional.name = 'main_light';
    this.scene.add(directional);
    this.lights.push(directional);
  }

  
  private updateGridSize() {
    if (!this.gridHelper) return;

    const gridSize = this.defaultCameraDistance * 3;

    
    this.scene.remove(this.gridHelper);
    this.gridHelper.geometry.dispose();
    (this.gridHelper.material as THREE.Material).dispose();

    this.gridHelper = new THREE.GridHelper(gridSize, 10);
    (this.gridHelper.material as THREE.Material).polygonOffset = true;
    (this.gridHelper.material as THREE.Material).polygonOffsetFactor = 1;
    (this.gridHelper.material as THREE.Material).polygonOffsetUnits = 1;
    this.scene.add(this.gridHelper);

    
    if (this.axesHelper) {
      this.scene.remove(this.axesHelper);
      this.axesHelper.traverse((child) => {
        if (child instanceof Line2) {
          child.geometry.dispose();
          (child.material as LineMaterial).dispose();
        }
      });

      this.axesHelper = new THREE.Group();
      this.axesHelper.renderOrder = 1;

      const axisLength = this.defaultCameraDistance * 0.5;
      const lineWidth = 5;

      const createAxis = (positions: number[], color: number) => {
        const geom = new LineGeometry();
        geom.setPositions(positions);
        const mat = new LineMaterial({
          color,
          linewidth: lineWidth,
          resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
          polygonOffset: true,
          polygonOffsetFactor: -1,
          polygonOffsetUnits: -1,
        });
        const line = new Line2(geom, mat);
        line.computeLineDistances();
        return line;
      };

      this.axesHelper.add(createAxis([0, 0, 0, axisLength, 0, 0], 0xff0000));
      this.axesHelper.add(createAxis([0, 0, 0, 0, axisLength, 0], 0x00ff00));
      this.axesHelper.add(createAxis([0, 0, 0, 0, 0, axisLength], 0x0000ff));

      this.scene.add(this.axesHelper);
    }
  }

  
  setGrid(show: boolean) {
    if (show && !this.gridHelper) {
      
      const gridSize = this.defaultCameraDistance * 3;

      
      this.gridHelper = new THREE.GridHelper(gridSize, 10);
      
      (this.gridHelper.material as THREE.Material).polygonOffset = true;
      (this.gridHelper.material as THREE.Material).polygonOffsetFactor = 1;
      (this.gridHelper.material as THREE.Material).polygonOffsetUnits = 1;
      this.scene.add(this.gridHelper);

      
      this.axesHelper = new THREE.Group();
      this.axesHelper.renderOrder = 1; 

      const axisLength = this.defaultCameraDistance * 0.5;
      const lineWidth = 5;

      
      const xGeom = new LineGeometry();
      xGeom.setPositions([0, 0, 0, axisLength, 0, 0]);
      const xMat = new LineMaterial({
        color: 0xff0000,
        linewidth: lineWidth,
        resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
        polygonOffset: true,
        polygonOffsetFactor: -1,
        polygonOffsetUnits: -1,
      });
      const xLine = new Line2(xGeom, xMat);
      xLine.computeLineDistances();
      this.axesHelper.add(xLine);

      
      const yGeom = new LineGeometry();
      yGeom.setPositions([0, 0, 0, 0, axisLength, 0]);
      const yMat = new LineMaterial({
        color: 0x00ff00,
        linewidth: lineWidth,
        resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
        polygonOffset: true,
        polygonOffsetFactor: -1,
        polygonOffsetUnits: -1,
      });
      const yLine = new Line2(yGeom, yMat);
      yLine.computeLineDistances();
      this.axesHelper.add(yLine);

      
      const zGeom = new LineGeometry();
      zGeom.setPositions([0, 0, 0, 0, 0, axisLength]);
      const zMat = new LineMaterial({
        color: 0x0000ff,
        linewidth: lineWidth,
        resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
        polygonOffset: true,
        polygonOffsetFactor: -1,
        polygonOffsetUnits: -1,
      });
      const zLine = new Line2(zGeom, zMat);
      zLine.computeLineDistances();
      this.axesHelper.add(zLine);

      this.scene.add(this.axesHelper);
    } else if (!show && this.gridHelper) {
      
      this.scene.remove(this.gridHelper);
      this.gridHelper.geometry.dispose();
      (this.gridHelper.material as THREE.Material).dispose();
      this.gridHelper = null;

      
      if (this.axesHelper) {
        this.scene.remove(this.axesHelper);
        
        this.axesHelper.traverse((child) => {
          if (child instanceof Line2) {
            child.geometry.dispose();
            (child.material as LineMaterial).dispose();
          }
        });
        this.axesHelper = null;
      }
    }
  }

  setSkeleton(show: boolean) {
    
    if (this.skeletonHelper) {
      this.scene.remove(this.skeletonHelper);
      this.skeletonHelper.geometry.dispose();
      (this.skeletonHelper.material as THREE.Material).dispose();
      this.skeletonHelper = null;
    }

    if (!show) return;

    
    const skinnedMeshes: THREE.SkinnedMesh[] = [];
    if (this.content) {
      this.content.traverse((child) => {
        if (child instanceof THREE.SkinnedMesh) {
          skinnedMeshes.push(child);
        }
      });
    }
    if (this.platformScene) {
      this.platformScene.traverse((child) => {
        if (child instanceof THREE.SkinnedMesh) {
          skinnedMeshes.push(child);
        }
      });
    }

    const skinnedMesh = skinnedMeshes[0];
    if (!skinnedMesh || !skinnedMesh.skeleton) return;

    
    let rootBone: THREE.Bone | null = null;
    for (const bone of skinnedMesh.skeleton.bones) {
      if (!bone.parent || !(bone.parent instanceof THREE.Bone)) {
        rootBone = bone;
        break;
      }
    }

    if (rootBone) {
      this.skeletonHelper = new THREE.SkeletonHelper(rootBone);
      this.scene.add(this.skeletonHelper);
    }
  }

  setStats(show: boolean) {
    if (show && !this.stats && this.statsContainer) {
      this.stats = new StatsJs();
      this.stats.showPanel(0);
      this.stats.dom.style.position = 'absolute';
      this.stats.dom.style.top = '0';
      this.stats.dom.style.left = '0';
      this.stats.dom.style.zIndex = '100';
      this.statsContainer.appendChild(this.stats.dom);
    } else if (!show && this.stats) {
      if (this.stats.dom.parentElement) {
        this.stats.dom.parentElement.removeChild(this.stats.dom);
      }
      this.stats = null;
    }
  }

  
  setAutoRotate(enabled: boolean, speed: number) {
    this.controls.autoRotate = enabled;
    this.controls.autoRotateSpeed = speed;
  }

  
  setOrbitMode(mode: 'turntable' | 'free') {
    if (mode === 'turntable') {
      
      const currentPolar = this.controls.getPolarAngle();
      this.controls.minPolarAngle = currentPolar;
      this.controls.maxPolarAngle = currentPolar;
    } else {
      
      this.controls.minPolarAngle = 0;
      this.controls.maxPolarAngle = Math.PI;
    }
  }

  setClipPlanes(near: number, far: number) {
    this.camera.near = near;
    this.camera.far = far;
    this.camera.updateProjectionMatrix();
  }

  
  updateMaterials(
    materials: MaterialInfo[],
    globalWireframe: boolean,
    pointSize: number
  ) {
    
    this.materialRegistry.forEach((material, uuid) => {
      const info = materials.find((m) => m.uuid === uuid);
      if (!info) return;

      if ('wireframe' in material) {
        material.wireframe = globalWireframe || info.wireframe;
      }

      if (material instanceof THREE.PointsMaterial) {
        material.size = pointSize;
      }

      const matWithColor = material as unknown as { color?: THREE.Color };
      if (matWithColor.color instanceof THREE.Color) {
        matWithColor.color.set(info.color);
      }

      if (material instanceof THREE.MeshStandardMaterial || material instanceof THREE.MeshPhysicalMaterial) {
        material.metalness = info.metalness;
        material.roughness = info.roughness;
      }

      const nextSide = info.doubleSided ? THREE.DoubleSide : THREE.FrontSide;
      if (material.side !== nextSide) {
        material.side = nextSide;
        material.needsUpdate = true;
      }
    });

    
    if (this.platformScene) {
      this.platformScene.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
          const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
          mats.forEach((mat) => {
            if ('wireframe' in mat) {
              mat.wireframe = globalWireframe;
            }
          });
        }
      });
    }
  }

  
  updateVisibility(hiddenNodes: Set<string>, soloNode: string | null) {
    
    const applyHidden = (object: THREE.Object3D, parentHidden: boolean) => {
      const isHidden = parentHidden || hiddenNodes.has(object.uuid);
      object.visible = !isHidden;
      object.children.forEach((child) => applyHidden(child, isHidden));
    };

    if (soloNode) {
      
      if (this.content) {
        this.content.traverse((obj) => {
          obj.visible = false;
        });
      }
      if (this.platformScene) {
        this.platformScene.traverse((obj) => {
          obj.visible = false;
        });
      }

      
      const soloObject = this.content?.getObjectByProperty('uuid', soloNode)
        || this.platformScene?.getObjectByProperty('uuid', soloNode);
      if (soloObject) {
        let current: THREE.Object3D | null = soloObject;
        while (current) {
          current.visible = true;
          current = current.parent;
        }
        soloObject.traverse((child) => {
          child.visible = true;
        });
      }
      return;
    }

    
    if (this.content) {
      applyHidden(this.content, false);
    }

    
    if (this.platformScene) {
      applyHidden(this.platformScene, false);
    }
  }

  
  syncBoundingBoxHelpers(nodeUuids: Set<string>) {
    
    this.boundingBoxHelpers.forEach((helper, uuid) => {
      if (!nodeUuids.has(uuid)) {
        this.scene.remove(helper);
        helper.geometry.dispose();
        (helper.material as THREE.Material).dispose();
        this.boundingBoxHelpers.delete(uuid);
      }
    });

    
    this.boneBoundingBoxHelpers.forEach((entry, uuid) => {
      if (!nodeUuids.has(uuid)) {
        this.scene.remove(entry.helper);
        entry.helper.geometry.dispose();
        (entry.helper.material as THREE.Material).dispose();
        this.boneBoundingBoxHelpers.delete(uuid);
      }
    });

    
    nodeUuids.forEach((uuid) => {
      if (this.boundingBoxHelpers.has(uuid) || this.boneBoundingBoxHelpers.has(uuid)) {
        return; 
      }

      
      const target = this.findObjectByUuid(uuid);
      if (!target) return;

      
      if (target instanceof THREE.Bone) {
        this.createBoneBoundingBoxHelper(uuid, target);
      } else {
        
        const helper = new THREE.BoxHelper(target, '#ffff00');
        this.scene.add(helper);
        this.boundingBoxHelpers.set(uuid, helper);
      }
    });
  }

  
  private findObjectByUuid(uuid: string): THREE.Object3D | null {
    if (this.content) {
      const found = this.content.getObjectByProperty('uuid', uuid);
      if (found) return found;
    }
    if (this.platformScene) {
      const found = this.platformScene.getObjectByProperty('uuid', uuid);
      if (found) return found;
    }
    return null;
  }

  
  private createBoneBoundingBoxHelper(uuid: string, bone: THREE.Bone) {
    const searchScene = this.content || this.scene;

    
    const skinnedMesh = this.findSkinnedMeshForBone(searchScene, bone);
    if (!skinnedMesh || !skinnedMesh.skeleton) return;

    
    const boneIndices = this.collectBoneIndices(bone, skinnedMesh.skeleton);

    
    const vertexIndices = this.findInfluencedVertices(skinnedMesh, boneIndices);
    if (vertexIndices.length === 0) return;

    const mapping: BoneVertexMapping = {
      skinnedMesh,
      vertexIndices,
      boneIndices,
    };

    
    const boxGeometry = new THREE.BoxGeometry(1, 1, 1);
    const edgesGeometry = new THREE.EdgesGeometry(boxGeometry);
    const material = new THREE.LineBasicMaterial({ color: '#00ffff' });
    const helper = new THREE.LineSegments(edgesGeometry, material);
    boxGeometry.dispose();

    this.scene.add(helper);
    this.boneBoundingBoxHelpers.set(uuid, { helper, mapping });
  }

  
  private updateBoneBoundingBoxHelpers() {
    const box = new THREE.Box3();
    const positionVec = new THREE.Vector3();

    this.boneBoundingBoxHelpers.forEach(({ helper, mapping }) => {
      const { skinnedMesh, vertexIndices } = mapping;

      
      box.makeEmpty();

      
      for (const idx of vertexIndices) {
        skinnedMesh.getVertexPosition(idx, positionVec);
        positionVec.applyMatrix4(skinnedMesh.matrixWorld);
        box.expandByPoint(positionVec);
      }

      if (box.isEmpty()) return;

      
      const center = box.getCenter(new THREE.Vector3());
      const size = box.getSize(new THREE.Vector3());

      helper.position.copy(center);
      helper.scale.copy(size);
    });
  }

  
  syncWireframeHelpers(nodeUuids: Set<string>) {
    
    this.boneWireframeHelpers.forEach((entry, uuid) => {
      if (!nodeUuids.has(uuid)) {
        this.scene.remove(entry.helper);
        entry.helper.geometry.dispose();
        (entry.helper.material as THREE.Material).dispose();
        this.boneWireframeHelpers.delete(uuid);
      }
    });

    
    nodeUuids.forEach((uuid) => {
      if (this.boneWireframeHelpers.has(uuid)) return;

      const target = this.findObjectByUuid(uuid);
      if (!target || !(target instanceof THREE.Bone)) return;

      this.createBoneWireframeHelper(uuid, target);
    });
  }

  
  private createBoneWireframeHelper(uuid: string, bone: THREE.Bone) {
    const searchScene = this.content || this.scene;

    
    const skinnedMesh = this.findSkinnedMeshForBone(searchScene, bone);
    if (!skinnedMesh || !skinnedMesh.skeleton) return;

    
    const boneIndices = this.collectBoneIndices(bone, skinnedMesh.skeleton);

    
    const vertexIndices = this.findInfluencedVerticesSet(skinnedMesh, boneIndices);
    if (vertexIndices.size === 0) return;

    
    const triangleIndices = this.findInfluencedTriangles(skinnedMesh.geometry, vertexIndices);
    if (triangleIndices.length === 0) return;

    const mapping: BoneWireframeMapping = {
      skinnedMesh,
      triangleIndices,
      vertexIndices,
    };

    
    const edgeCount = triangleIndices.length * 3;
    const positions = new Float32Array(edgeCount * 2 * 3);

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));

    const material = new THREE.LineBasicMaterial({
      color: '#00ffff',
      depthTest: true,
      depthWrite: false,
      transparent: true,
      opacity: 0.8,
    });

    const helper = new THREE.LineSegments(geometry, material);
    helper.frustumCulled = false;

    this.scene.add(helper);
    this.boneWireframeHelpers.set(uuid, { helper, mapping, positions });
  }

  
  private updateBoneWireframeHelpers() {
    const tempVec = new THREE.Vector3();

    this.boneWireframeHelpers.forEach(({ helper, mapping, positions }) => {
      const { skinnedMesh, triangleIndices } = mapping;
      const geometry = skinnedMesh.geometry;
      const index = geometry.getIndex();

      let posIdx = 0;

      for (const triIdx of triangleIndices) {
        let a: number, b: number, c: number;

        if (index) {
          a = index.array[triIdx * 3];
          b = index.array[triIdx * 3 + 1];
          c = index.array[triIdx * 3 + 2];
        } else {
          a = triIdx * 3;
          b = triIdx * 3 + 1;
          c = triIdx * 3 + 2;
        }

        
        skinnedMesh.getVertexPosition(a, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        skinnedMesh.getVertexPosition(b, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        
        skinnedMesh.getVertexPosition(b, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        skinnedMesh.getVertexPosition(c, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        
        skinnedMesh.getVertexPosition(c, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        skinnedMesh.getVertexPosition(a, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;
      }

      
      const positionAttr = helper.geometry.getAttribute('position') as THREE.BufferAttribute;
      positionAttr.needsUpdate = true;
    });
  }

  
  private computeSkinnedBoundingBox(object: THREE.Object3D): THREE.Box3 {
    const box = new THREE.Box3();
    const positionVec = new THREE.Vector3();

    
    object.traverse((child) => {
      if (child instanceof THREE.SkinnedMesh && child.skeleton) {
        child.skeleton.update();
      }
    });

    
    object.traverse((child) => {
      if (child instanceof THREE.SkinnedMesh) {
        
        const geometry = child.geometry;
        const positionAttr = geometry.getAttribute('position');
        if (!positionAttr) return;

        const vertexCount = positionAttr.count;
        for (let i = 0; i < vertexCount; i++) {
          child.getVertexPosition(i, positionVec);
          positionVec.applyMatrix4(child.matrixWorld);
          box.expandByPoint(positionVec);
        }
      } else if (child instanceof THREE.Mesh) {
        
        const meshBox = new THREE.Box3().setFromObject(child);
        box.union(meshBox);
      }
    });

    
    if (box.isEmpty()) {
      box.setFromObject(object);
    }

    return box;
  }

  
  private findSkinnedMeshForBone(scene: THREE.Object3D, bone: THREE.Bone): THREE.SkinnedMesh | null {
    let result: THREE.SkinnedMesh | null = null;
    scene.traverse((obj) => {
      if (result) return;
      if (obj instanceof THREE.SkinnedMesh && obj.skeleton) {
        if (obj.skeleton.bones.includes(bone)) {
          result = obj;
        }
      }
    });
    return result;
  }

  
  private collectBoneIndices(bone: THREE.Bone, skeleton: THREE.Skeleton): Set<number> {
    const indices = new Set<number>();
    const boneIndex = skeleton.bones.indexOf(bone);
    if (boneIndex !== -1) {
      indices.add(boneIndex);
    }
    bone.traverse((child) => {
      if (child instanceof THREE.Bone && child !== bone) {
        const childIndex = skeleton.bones.indexOf(child);
        if (childIndex !== -1) {
          indices.add(childIndex);
        }
      }
    });
    return indices;
  }

  
  private findInfluencedVertices(
    skinnedMesh: THREE.SkinnedMesh,
    boneIndices: Set<number>,
    minWeight: number = 0.1
  ): number[] {
    const geometry = skinnedMesh.geometry;
    const skinIndex = geometry.getAttribute('skinIndex');
    const skinWeight = geometry.getAttribute('skinWeight');

    if (!skinIndex || !skinWeight) return [];

    const vertexIndices: number[] = [];
    const vertexCount = skinIndex.count;

    for (let i = 0; i < vertexCount; i++) {
      for (let j = 0; j < 4; j++) {
        const boneIdx = skinIndex.getComponent(i, j);
        const weight = skinWeight.getComponent(i, j);

        if (boneIndices.has(boneIdx) && weight >= minWeight) {
          vertexIndices.push(i);
          break;
        }
      }
    }

    return vertexIndices;
  }

  
  private findInfluencedVerticesSet(
    skinnedMesh: THREE.SkinnedMesh,
    boneIndices: Set<number>,
    minWeight: number = 0.1
  ): Set<number> {
    const geometry = skinnedMesh.geometry;
    const skinIndex = geometry.getAttribute('skinIndex');
    const skinWeight = geometry.getAttribute('skinWeight');

    if (!skinIndex || !skinWeight) return new Set();

    const vertexIndices = new Set<number>();
    const vertexCount = skinIndex.count;

    for (let i = 0; i < vertexCount; i++) {
      for (let j = 0; j < 4; j++) {
        const boneIdx = skinIndex.getComponent(i, j);
        const weight = skinWeight.getComponent(i, j);

        if (boneIndices.has(boneIdx) && weight >= minWeight) {
          vertexIndices.add(i);
          break;
        }
      }
    }

    return vertexIndices;
  }

  
  private findInfluencedTriangles(
    geometry: THREE.BufferGeometry,
    vertexIndices: Set<number>
  ): number[] {
    const index = geometry.getIndex();
    const triangleIndices: number[] = [];

    if (index) {
      const indexArray = index.array;
      for (let i = 0; i < indexArray.length; i += 3) {
        const a = indexArray[i];
        const b = indexArray[i + 1];
        const c = indexArray[i + 2];

        if (vertexIndices.has(a) || vertexIndices.has(b) || vertexIndices.has(c)) {
          triangleIndices.push(i / 3);
        }
      }
    } else {
      const positionAttr = geometry.getAttribute('position');
      const vertexCount = positionAttr.count;
      for (let i = 0; i < vertexCount; i += 3) {
        if (vertexIndices.has(i) || vertexIndices.has(i + 1) || vertexIndices.has(i + 2)) {
          triangleIndices.push(i / 3);
        }
      }
    }

    return triangleIndices;
  }

  
  private clearHelpers() {
    this.boundingBoxHelpers.forEach((helper) => {
      this.scene.remove(helper);
      helper.geometry.dispose();
      (helper.material as THREE.Material).dispose();
    });
    this.boundingBoxHelpers.clear();

    this.boneBoundingBoxHelpers.forEach(({ helper }) => {
      this.scene.remove(helper);
      helper.geometry.dispose();
      (helper.material as THREE.Material).dispose();
    });
    this.boneBoundingBoxHelpers.clear();

    this.boneWireframeHelpers.forEach(({ helper }) => {
      this.scene.remove(helper);
      helper.geometry.dispose();
      (helper.material as THREE.Material).dispose();
    });
    this.boneWireframeHelpers.clear();
  }

  
  async loadPlatform(url: string): Promise<{ scene: THREE.Group; sceneGraph: SceneNode }> {
    const { scene, animations } = await this.loadModel(url);

    
    if (this.platformScene) {
      this.platformGroup?.remove(this.platformScene);
    }

    
    if (this.platformMixer) {
      this.platformMixer.stopAllAction();
      this.platformMixer = null;
    }
    this.platformActions.clear();

    this.platformScene = scene;
    this.platformGroup?.add(scene);

    
    if (animations.length > 0) {
      this.platformMixer = new THREE.AnimationMixer(scene);

      
      animations.forEach((clip) => {
        const action = this.platformMixer!.clipAction(clip);
        this.platformActions.set(clip.name, action);
        action.play();
      });

      
      this.platformMixer.update(0);
      scene.updateMatrixWorld(true);

      console.log(`[Platform] loaded ${animations.length} animation(s):`, animations.map(a => a.name).join(', '));
    }

    
    const sceneGraph = this.buildSceneGraph(scene);

    return { scene, sceneGraph };
  }

  setPlatformVisible(visible: boolean) {
    if (this.platformGroup) {
      this.platformGroup.visible = visible;
    }
  }

  layoutGamePreview(unitScale: number | null) {
    if (!this.platformGroup || !this.platformScene || !this.content) return;

    
    const scale = unitScale != null && unitScale > 0 ? unitScale : 1;
    this.content.scale.set(scale, scale, scale);
    this.content.updateWorldMatrix(true, true);
    console.log(`[Layout] applied unitScale=${scale} to model="${this.content.name}"`);

    
    const PLATFORM_SCALE = 1.15;
    this.platformGroup.position.set(0, 0, 0);
    this.platformGroup.scale.set(PLATFORM_SCALE, PLATFORM_SCALE, PLATFORM_SCALE);
    this.platformGroup.rotation.set(0, 0, 0);
    this.platformGroup.updateWorldMatrix(true, true);

    
    const platformBox = this.computeSkinnedBoundingBox(this.platformGroup);
    this.platformSize = platformBox.getSize(new THREE.Vector3());

    console.log(`[Platform] size: [${this.platformSize.x.toFixed(4)}, ${this.platformSize.y.toFixed(4)}, ${this.platformSize.z.toFixed(4)}]`);

    this.platformGroup.updateWorldMatrix(true, true);

    
    const rotatedPlatformBox = this.computeSkinnedBoundingBox(this.platformGroup);

    if (!this.platformInitialCenter) {
      this.platformInitialCenter = rotatedPlatformBox.getCenter(new THREE.Vector3());
    }
    const platformCenter = this.platformInitialCenter;

    
    const raycaster = new THREE.Raycaster();
    raycaster.set(
      new THREE.Vector3(platformCenter.x, rotatedPlatformBox.max.y + 1, platformCenter.z),
      new THREE.Vector3(0, -1, 0)
    );
    const intersects = raycaster.intersectObject(this.platformGroup, true);
    const platformSurfaceY = intersects.length > 0 ? intersects[0].point.y : rotatedPlatformBox.max.y;

    
    const DISK_CENTER_OFFSET_X = -0.1;
    const DISK_CENTER_OFFSET_Z = 0.3;
    const offsetY = 0 - platformSurfaceY;
    this.platformGroup.position.set(
      0 - platformCenter.x + DISK_CENTER_OFFSET_X,
      offsetY,
      0 - platformCenter.z + DISK_CENTER_OFFSET_Z
    );
    this.platformGroup.updateWorldMatrix(true, true);

    this.defaultCameraTarget = null;
    this.defaultCameraDistance = 5;
    this.setupCameraForPlatform();

    console.log(`[Layout] model="${this.content.name}" platformSurfaceY=${platformSurfaceY.toFixed(4)} cameraSize=${this.platformSize.length().toFixed(4)}`);
  }

  
  async loadGameEnvironment(url: string): Promise<void> {
    const loader = new THREE.TextureLoader();
    const texture = await loader.loadAsync(url);
    texture.mapping = THREE.EquirectangularReflectionMapping;
    texture.colorSpace = THREE.SRGBColorSpace;

    
    this.gameEnvironment = this.pmremGenerator.fromEquirectangular(texture).texture;
    texture.dispose();
  }

  async loadGameBackground(url: string): Promise<void> {
    const loader = new THREE.TextureLoader();
    this.gameBackground = await loader.loadAsync(url);
    this.gameBackground.colorSpace = THREE.SRGBColorSpace;
    this.gameBackground.offset.y = -0.2; 
  }

  resetCamera() {
    if (this.platformGroup?.visible && this.platformScene && this.platformSize) {
      this.setupCameraForPlatform();
    } else {
      this.setupCameraForModel();
    }
  }

  private setupCameraForModel() {
    if (!this.defaultCameraTarget) return;

    this.controls.reset();
    this.controls.target.copy(this.defaultCameraTarget);
    this.camera.position.set(0, this.defaultCameraTarget.y, this.defaultCameraDistance);
    this.camera.lookAt(this.defaultCameraTarget);
    this.controls.update();
    this.setCameraPreset('isometric');
    this.controls.saveState();

    
    this.updateGridSize();
  }

  private setupCameraForPlatform() {
    if (!this.platformSize) return;

    if (!this.defaultCameraTarget) {
      const size = this.platformSize.length();
      this.controls.maxDistance = size * 10;
      this.camera.near = size / 100;
      this.camera.far = size * 100;
      this.camera.updateProjectionMatrix();

      const targetY = this.platformSize.y / 2;
      const lookAtY = targetY + size / 8.0;
      const distance = size * 0.7;

      this.defaultCameraTarget = new THREE.Vector3(0, lookAtY, 0);
      this.defaultCameraDistance = distance;
    }

    this.controls.reset();
    this.controls.minPolarAngle = 0;
    this.controls.maxPolarAngle = Math.PI;

    this.controls.target.copy(this.defaultCameraTarget);
    this.camera.position.set(0, this.defaultCameraTarget.y, this.defaultCameraDistance);
    this.camera.lookAt(this.defaultCameraTarget);
    this.controls.update();
    this.setCameraPreset('isometric');

    
    const zoomFactor = Math.pow(0.95, 8);
    const dir = this.camera.position.clone().sub(this.controls.target);
    this.camera.position.copy(this.controls.target.clone().add(dir.multiplyScalar(zoomFactor)));
    this.controls.update();

    
    const rotDir = this.camera.position.clone().sub(this.controls.target);
    rotDir.applyAxisAngle(new THREE.Vector3(0, 1, 0), (-15 * Math.PI) / 180);
    this.camera.position.copy(this.controls.target.clone().add(rotDir));
    this.controls.update();

    const currentPolar = this.controls.getPolarAngle();
    this.controls.minPolarAngle = currentPolar;
    this.controls.maxPolarAngle = currentPolar;

    this.controls.saveState();

    
    this.updateGridSize();
  }

  setCameraPreset(preset: CameraPreset) {
    const distance = this.camera.position.distanceTo(this.controls.target);
    const target = this.controls.target.clone();

    let position: THREE.Vector3;

    switch (preset) {
      case 'front':
        position = new THREE.Vector3(0, 0, distance);
        break;
      case 'back':
        position = new THREE.Vector3(0, 0, -distance);
        break;
      case 'left':
        position = new THREE.Vector3(-distance, 0, 0);
        break;
      case 'right':
        position = new THREE.Vector3(distance, 0, 0);
        break;
      case 'top':
        position = new THREE.Vector3(0, distance, 0);
        break;
      case 'bottom':
        position = new THREE.Vector3(0, -distance, 0);
        break;
      case 'isometric':
        
        position = new THREE.Vector3(-distance / 2.0, -distance / 16.0, distance / 2.0)
          .normalize()
          .multiplyScalar(distance);
        break;
      default:
        position = new THREE.Vector3(0, 0, distance);
    }

    position.add(target);
    this.camera.position.copy(position);
    this.controls.update();
  }

  takeScreenshot(): string | null {
    const glCanvas = this.renderer.domElement;
    const width = glCanvas.width;
    const height = glCanvas.height;

    if (!this.skyBackgroundImage) {
      return glCanvas.toDataURL('image/png');
    }

    const offscreen = document.createElement('canvas');
    offscreen.width = width;
    offscreen.height = height;
    const ctx = offscreen.getContext('2d');
    if (!ctx) return glCanvas.toDataURL('image/png');

    
    const img = this.skyBackgroundImage;
    const imgAspect = img.naturalWidth / img.naturalHeight;
    const canvasAspect = width / height;
    let drawW: number, drawH: number;
    if (imgAspect > canvasAspect) {
      drawH = height;
      drawW = height * imgAspect;
    } else {
      drawW = width;
      drawH = width / imgAspect;
    }
    const drawX = (width - drawW) / 2;
    const drawY = this.skyBackgroundPosition.includes('top') ? 0 : (height - drawH) / 2;
    ctx.drawImage(img, drawX, drawY, drawW, drawH);

    
    ctx.drawImage(glCanvas, 0, 0);

    return offscreen.toDataURL('image/png');
  }

  getContent() {
    return this.content;
  }

  getPlatformScene() {
    return this.platformScene;
  }

  dispose() {
    this.disposed = true;

    
    if (this.animationFrameId !== null) {
      cancelAnimationFrame(this.animationFrameId);
    }

    
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;

    
    this.clear();

    
    this.lights.forEach((light) => {
      light.parent?.remove(light);
    });

    
    if (this.gridHelper) {
      this.scene.remove(this.gridHelper);
      this.gridHelper.geometry.dispose();
      (this.gridHelper.material as THREE.Material).dispose();
    }
    if (this.axesHelper) {
      this.scene.remove(this.axesHelper);
      
      this.axesHelper.traverse((child) => {
        if (child instanceof Line2) {
          child.geometry.dispose();
          (child.material as LineMaterial).dispose();
        }
      });
    }
    if (this.skeletonHelper) {
      this.scene.remove(this.skeletonHelper);
      this.skeletonHelper.geometry.dispose();
      (this.skeletonHelper.material as THREE.Material).dispose();
    }

    
    if (this.stats && this.stats.dom.parentElement) {
      this.stats.dom.parentElement.removeChild(this.stats.dom);
    }

    
    this.neutralEnvironment?.dispose();
    this.gameEnvironment?.dispose();
    this.gameBackground?.dispose();

    
    this.pmremGenerator.dispose();

    
    this.controls.dispose();

    
    this.renderer.dispose();

    
    if (this.renderer.domElement.parentElement) {
      this.renderer.domElement.parentElement.removeChild(this.renderer.domElement);
    }
  }
}

const ModelViewer = forwardRef<ModelViewerHandle, ModelViewerProps>(
  function ModelViewer({ glbUrl, onModelLoaded, statsContainer, unitScale, faction, customTextureUrl }, ref) {
    const containerRef = useRef<HTMLDivElement>(null);
    const viewerRef = useRef<ThreeViewer | null>(null);
    const [viewerReady, setViewerReady] = useState(false);
    const [loading, setLoading] = useState(true);
    const [layoutReady, setLayoutReady] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const modelUrlRef = useRef<string | null>(null);
    const { label } = useLabels();

    
    const {
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
      pixelRatioLimit,
      autoRotate,
      autoRotateSpeed,
      nearClip,
      farClip,
      showStats,
      showSkeleton,
      pointSize,
      materials,
      globalWireframe,
      hiddenNodes,
      soloNode,
      boundingBoxNodes,
      wireframeNodes,
      animations: storeAnimations,
      playbackSpeed,
      loopMode,
      setAnimations,
      setMaterials,
      setSceneGraph,
      setPlatformSceneGraph,
      setShowGrid,
      setGlobalWireframe,
    } = useViewerStore();

    
    useImperativeHandle(ref, () => ({
      takeScreenshot: () => viewerRef.current?.takeScreenshot() ?? null,
      resetCamera: () => viewerRef.current?.resetCamera(),
      setCameraPreset: (preset: CameraPreset) => viewerRef.current?.setCameraPreset(preset),
    }), []);

    
    useEffect(() => {
      if (!containerRef.current) return;

      const viewer = new ThreeViewer(containerRef.current, statsContainer);
      viewerRef.current = viewer;
      setViewerReady(true);

      return () => {
        viewer.dispose();
        viewerRef.current = null;
        setViewerReady(false);
      };
    }, [statsContainer]);

    
    useEffect(() => {
      if (!viewerReady) return;
      const viewer = viewerRef.current;
      if (!viewer) return;

      let blobUrl: string | null = null;
      let canceled = false;

      const loadModel = async () => {
        try {
          setLoading(true);
          setError(null);

          
          const response = await fetch(glbUrl);
          if (!response.ok) {
            throw new Error(`Failed to load model: ${response.statusText}`);
          }

          const blob = await response.blob();
          blobUrl = URL.createObjectURL(blob);

          if (canceled) {
            URL.revokeObjectURL(blobUrl);
            return;
          }

          modelUrlRef.current = blobUrl;

          
          const { scene, animations } = await viewer.loadModel(blobUrl);

          if (canceled) return;

          
          const skipCameraSetup = displayMode === 'game-preview';
          const { materials: extractedMaterials, sceneGraph } = viewer.setContent(
            scene,
            animations,
            (animStates) => {
              setAnimations(animStates);
            },
            skipCameraSetup
          );

          setMaterials(extractedMaterials);
          setSceneGraph(sceneGraph);

          setLoading(false);
          setLayoutReady(displayMode !== 'game-preview');
          onModelLoaded?.();
        } catch (err) {
          if (!canceled) {
            setError(err instanceof Error ? err.message : 'Unknown error');
            setLoading(false);
          }
        }
      };

      loadModel();

      return () => {
        canceled = true;
        if (blobUrl) {
          URL.revokeObjectURL(blobUrl);
        }
      };
    }, [viewerReady, glbUrl, displayMode, onModelLoaded, setAnimations, setMaterials, setSceneGraph]);
  
  useEffect(() => {
    const viewer = viewerRef.current;

    if (!viewer || loading) return;

    let canceled = false;

    viewer.setCustomTextureUrl(customTextureUrl ?? null).catch((err) => {
      if (!canceled) {
        console.warn('Failed to apply custom texture:', err);
      }
    });

    return () => {
      canceled = true;
    };
  }, [customTextureUrl, loading]);

    
    useEffect(() => {
      if (displayMode === 'game-preview') {
        setLayoutReady(false);
      } else {
        setLayoutReady(true);
      }
    }, [displayMode]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || loading) return;
      viewer.syncAnimations(storeAnimations, loopMode);
      
      viewer.syncPlatformAnimations(loopMode);
    }, [storeAnimations, loopMode, loading]);

    
    useEffect(() => {
      viewerRef.current?.setPlaybackSpeed(playbackSpeed);
    }, [playbackSpeed]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer) return;
      viewer.setBackground(showBackground, backgroundColor, displayMode);
      viewer.setEnvironment(displayMode);
    }, [showBackground, backgroundColor, displayMode]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || !viewerReady) return;
      viewer.setOrbitMode(displayMode === 'game-preview' ? 'turntable' : 'free');
    }, [viewerReady, displayMode]);

    
    useEffect(() => {
      viewerRef.current?.setToneMapping(toneMapping, exposure);
    }, [toneMapping, exposure]);

    
    useEffect(() => {
      viewerRef.current?.setPixelRatioLimit(pixelRatioLimit);
    }, [pixelRatioLimit]);

    
    useEffect(() => {
      if (!viewerReady) return;
      viewerRef.current?.updateLights(
        usePunctualLights,
        ambientIntensity,
        ambientColor,
        directionalIntensity,
        directionalColor
      );
    }, [viewerReady, usePunctualLights, ambientIntensity, ambientColor, directionalIntensity, directionalColor]);

    
    useEffect(() => {
      viewerRef.current?.setGrid(showGrid);
    }, [showGrid]);

    
    useEffect(() => {
      viewerRef.current?.setSkeleton(showSkeleton);
    }, [showSkeleton]);

    
    useEffect(() => {
      viewerRef.current?.setStats(showStats);
    }, [showStats]);

    
    useEffect(() => {
      viewerRef.current?.setAutoRotate(autoRotate, autoRotateSpeed);
    }, [autoRotate, autoRotateSpeed]);

    
    useEffect(() => {
      viewerRef.current?.setClipPlanes(nearClip, farClip);
    }, [nearClip, farClip]);

    
    useEffect(() => {
      viewerRef.current?.updateMaterials(materials, globalWireframe, pointSize);
    }, [materials, globalWireframe, pointSize]);

    
    useEffect(() => {
      viewerRef.current?.updateVisibility(hiddenNodes, soloNode);
    }, [hiddenNodes, soloNode]);

    
    useEffect(() => {
      viewerRef.current?.syncBoundingBoxHelpers(boundingBoxNodes);
    }, [boundingBoxNodes]);

    
    useEffect(() => {
      viewerRef.current?.syncWireframeHelpers(wireframeNodes);
    }, [wireframeNodes]);

    
    useEffect(() => {
      viewerRef.current?.setPlatformVisible(displayMode === 'game-preview' && showPlatform);
    }, [displayMode, showPlatform]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || !viewerReady || displayMode !== 'game-preview') {
        
        if (displayMode !== 'game-preview') {
          setPlatformSceneGraph(null);
        }
        return;
      }

      let canceled = false;
      let platformBlobUrl: string | null = null;

      const loadPlatform = async () => {
        try {
          const response = await fetch('/api/viewer/platform');
          if (!response.ok || canceled) return;

          const blob = await response.blob();
          platformBlobUrl = URL.createObjectURL(blob);

          if (canceled) {
            URL.revokeObjectURL(platformBlobUrl);
            return;
          }

          const { sceneGraph } = await viewer.loadPlatform(platformBlobUrl);
          if (!canceled) {
            setPlatformSceneGraph(sceneGraph);
            
            viewer.syncPlatformAnimations(loopMode);
            viewer.setPlaybackSpeed(playbackSpeed);
          }
        } catch (err) {
          console.warn('Failed to load platform:', err);
        }
      };

      loadPlatform();

      return () => {
        canceled = true;
        setPlatformSceneGraph(null);
        if (platformBlobUrl) {
          URL.revokeObjectURL(platformBlobUrl);
        }
      };
      
      
    }, [viewerReady, displayMode, setPlatformSceneGraph]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      
      if (!viewer || displayMode !== 'game-preview' || !showPlatform || loading) return;

      let rafId: number;
      let waitingFrames = 0;
      let platformReadyFrames = 0;

      const tryLayout = () => {
        
        const platformReady = viewer.getPlatformScene();
        if (!platformReady) {
          
          waitingFrames++;
          if (waitingFrames === 1) {
            console.log('[Layout] Waiting for platform to load...');
          }
          rafId = requestAnimationFrame(tryLayout);
          return;
        }

        
        platformReadyFrames++;
        if (platformReadyFrames < 3) {
          rafId = requestAnimationFrame(tryLayout);
          return;
        }

        
        viewer.layoutGamePreview(unitScale ?? null);
        setLayoutReady(true);
      };

      rafId = requestAnimationFrame(tryLayout);

      return () => {
        cancelAnimationFrame(rafId);
      };
    }, [displayMode, showPlatform, loading, unitScale]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || displayMode !== 'game-preview') return;

      let canceled = false;
      let environmentBlobUrl: string | null = null;

      const loadGameAssets = async () => {
        try {
          const envResponse = await fetch('/api/viewer/environment');
          if (canceled) return;

          if (envResponse.ok) {
            const blob = await envResponse.blob();
            environmentBlobUrl = URL.createObjectURL(blob);
            if (!canceled) {
              await viewer.loadGameEnvironment(environmentBlobUrl);
              viewer.setEnvironment(displayMode);
            }
          }
        } catch (err) {
          console.warn('Failed to load game-preview environment:', err);
        }
      };

      loadGameAssets();

      return () => {
        canceled = true;
        if (environmentBlobUrl) URL.revokeObjectURL(environmentBlobUrl);
      };
    }, [displayMode]);

    
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer) return;

      if (displayMode !== 'game-preview' || !faction) {
        viewer.setCanvasSkyBackground(null);
        return;
      }

      let canceled = false;
      let bgBlobUrl: string | null = null;

      const loadBackground = async () => {
        try {
          let response = await fetch(`/api/viewer/unit-background/${encodeURIComponent(faction)}`);
          if (canceled) return;

          let position = 'center center';
          if (!response.ok) {
            response = await fetch(`/api/viewer/sky/${encodeURIComponent(faction)}`);
            if (canceled || !response.ok) return;
            position = 'center top';
          }

          const blob = await response.blob();
          bgBlobUrl = URL.createObjectURL(blob);
          if (!canceled) viewer.setCanvasSkyBackground(bgBlobUrl, position);
        } catch (err) {
          console.warn('Failed to load faction background:', err);
        }
      };

      loadBackground();

      return () => {
        canceled = true;
        if (bgBlobUrl) {
          URL.revokeObjectURL(bgBlobUrl);
          viewer.setCanvasSkyBackground(null);
        }
      };
    }, [displayMode, faction]);

    
    useEffect(() => {
      const handleKeyDown = (e: KeyboardEvent) => {
        if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
          return;
        }

        switch (e.key.toLowerCase()) {
          case 'r':
            viewerRef.current?.resetCamera();
            break;
          case 'g':
            setShowGrid(!showGrid);
            break;
          case 'w':
            setGlobalWireframe(!globalWireframe);
            break;
        }
      };

      window.addEventListener('keydown', handleKeyDown);
      return () => window.removeEventListener('keydown', handleKeyDown);
    }, [showGrid, globalWireframe, setShowGrid, setGlobalWireframe]);

    return (
      <div
        ref={containerRef}
        className="w-full h-full bg-[#191919]"
        style={{ position: 'relative' }}
      >
        {}
        {(loading || (displayMode === 'game-preview' && !layoutReady)) && (
          <div className="absolute inset-0 flex items-center justify-center bg-[#191919] text-gray-500 z-10">
            <div className="text-center">
              <div className="w-10 h-10 border-[3px] border-[#333] border-t-[#646cff] rounded-full animate-spin mx-auto mb-4" />
              <p>{label('viewer_model_loading')}</p>
            </div>
          </div>
        )}

        {}
        {error && (
          <div className="absolute inset-0 flex flex-col items-center justify-center bg-[#191919] gap-3 z-10">
            <p className="text-red-500 text-base">{label('viewer_model_failed')}</p>
            <p className="text-gray-600 text-sm">{error}</p>
          </div>
        )}
      </div>
    );
  }
);

export default ModelViewer;
