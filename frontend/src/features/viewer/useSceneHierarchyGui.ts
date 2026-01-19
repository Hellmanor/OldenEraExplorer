import { useEffect, useLayoutEffect, useRef, useCallback } from 'react';
import type GUI from 'lil-gui';
import { useViewerStore } from './viewerStore';
import type { SceneNode } from './viewerStore';

const HIERARCHY_STYLES = `
  .scene-hierarchy-tree {
    font-size: 11px;
    line-height: 1.3;
    user-select: none;
    width: 100%;
    box-sizing: border-box;
    overflow-x: auto;
    overflow-y: hidden;
  }

  .scene-hierarchy-tree * {
    box-sizing: border-box;
  }

  .scene-hierarchy-section {
    padding: 4px 0;
  }

  .scene-hierarchy-node-container {
    width: max-content;
    min-width: 100%;
  }

  .scene-hierarchy-section-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 4px 8px;
    font-size: 10px;
    color: var(--text-color);
    opacity: 0.6;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }

  .scene-hierarchy-section-header button {
    background: transparent;
    border: none;
    cursor: pointer;
    padding: 2px;
    color: var(--text-color);
    opacity: 0.6;
    display: flex;
    align-items: center;
  }

  .scene-hierarchy-section-header button:hover {
    opacity: 1;
  }

  .scene-hierarchy-node {
    display: flex;
    align-items: center;
    gap: 3px;
    padding: 3px 8px 3px 0;
    cursor: pointer;
    border-radius: 2px;
    transition: background-color 0.1s;
    width: max-content;
    min-width: 100%;
  }

  .scene-hierarchy-node:hover {
    background-color: var(--hover-color);
  }

  .scene-hierarchy-node.selected {
    background-color: var(--focus-color);
  }

  .scene-hierarchy-node.hidden {
    opacity: 0.4;
  }

  .scene-hierarchy-expand-btn {
    flex: 0 0 14px;
    width: 14px;
    max-width: 14px;
    height: 14px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    background: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    margin: 0;
    color: var(--text-color);
    opacity: 0.5;
    line-height: 1;
    font-size: 0;
  }

  .scene-hierarchy-expand-btn:hover {
    opacity: 1;
  }

  .scene-hierarchy-expand-btn svg {
    transition: transform 0.15s;
  }

  .scene-hierarchy-expand-btn.expanded svg {
    transform: rotate(90deg);
  }

  .scene-hierarchy-spacer {
    flex: 0 0 14px;
    width: 14px;
  }

  .scene-hierarchy-icon {
    flex: 0 0 14px;
    width: 14px;
    height: 14px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    opacity: 0.7;
  }

  .scene-hierarchy-name {
    flex: 0 0 auto;
    white-space: nowrap;
    color: var(--text-color);
  }

  .scene-hierarchy-vertex-count {
    flex: 0 0 auto;
    font-size: 9px;
    opacity: 0.5;
    margin-right: 4px;
  }

  .scene-hierarchy-actions {
    flex: 0 0 auto;
    display: inline-flex;
    gap: 2px;
  }

  .scene-hierarchy-action-btn {
    flex: 0 0 18px;
    width: 18px;
    height: 18px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    background: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    margin: 0;
    border-radius: 2px;
    color: var(--text-color);
    opacity: 0.3;
    transition: opacity 0.1s, color 0.1s;
  }

  .scene-hierarchy-action-btn:hover {
    opacity: 0.8;
  }

  .scene-hierarchy-action-btn.active {
    opacity: 1;
  }

  .scene-hierarchy-action-btn.visibility.active {
    color: var(--text-color);
  }

  .scene-hierarchy-action-btn.bbox.active {
    color: #fbbf24;
  }

  .scene-hierarchy-action-btn.wireframe.active {
    color: #e879f9;
  }

  .scene-hierarchy-children {
    display: none;
  }

  .scene-hierarchy-children.expanded {
    display: block;
  }

  .scene-hierarchy-clear-btn {
    margin: 4px 8px;
    padding: 4px 8px;
    font-size: 10px;
    background: rgba(251, 191, 36, 0.1);
    border: none;
    border-radius: 3px;
    color: #fbbf24;
    cursor: pointer;
    display: none;
  }

  .scene-hierarchy-clear-btn.visible {
    display: inline-block;
  }

  .scene-hierarchy-clear-btn:hover {
    background: rgba(251, 191, 36, 0.2);
  }

  .scene-hierarchy-count {
    font-size: 9px;
    opacity: 0.5;
    margin-left: 4px;
  }
`;

function createSvg(width: number, height: number, viewBox: string): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('width', String(width));
  svg.setAttribute('height', String(height));
  svg.setAttribute('viewBox', viewBox);
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  return svg;
}

function createChevronIcon(): SVGSVGElement {
  const svg = createSvg(10, 10, '0 0 24 24');
  const polyline = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
  polyline.setAttribute('points', '9 18 15 12 9 6');
  svg.appendChild(polyline);
  return svg;
}

function createMeshIcon(): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
  polygon.setAttribute('points', '12 2 22 8.5 22 15.5 12 22 2 15.5 2 8.5 12 2');
  svg.appendChild(polygon);
  return svg;
}

function createBoneIcon(): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const circle1 = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
  circle1.setAttribute('cx', '6');
  circle1.setAttribute('cy', '6');
  circle1.setAttribute('r', '3');
  const circle2 = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
  circle2.setAttribute('cx', '18');
  circle2.setAttribute('cy', '18');
  circle2.setAttribute('r', '3');
  const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
  line.setAttribute('x1', '8');
  line.setAttribute('y1', '8');
  line.setAttribute('x2', '16');
  line.setAttribute('y2', '16');
  svg.appendChild(circle1);
  svg.appendChild(circle2);
  svg.appendChild(line);
  return svg;
}

function createGroupIcon(): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', 'M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z');
  svg.appendChild(path);
  return svg;
}

function createDefaultIcon(): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
  circle.setAttribute('cx', '12');
  circle.setAttribute('cy', '12');
  circle.setAttribute('r', '10');
  svg.appendChild(circle);
  return svg;
}

function createEyeOpenIcon(): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', 'M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z');
  const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
  circle.setAttribute('cx', '12');
  circle.setAttribute('cy', '12');
  circle.setAttribute('r', '3');
  svg.appendChild(path);
  svg.appendChild(circle);
  return svg;
}

function createEyeClosedIcon(): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', 'M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24');
  const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
  line.setAttribute('x1', '1');
  line.setAttribute('y1', '1');
  line.setAttribute('x2', '23');
  line.setAttribute('y2', '23');
  svg.appendChild(path);
  svg.appendChild(line);
  return svg;
}

function createBboxIcon(dashed: boolean): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
  rect.setAttribute('x', '3');
  rect.setAttribute('y', '3');
  rect.setAttribute('width', '18');
  rect.setAttribute('height', '18');
  rect.setAttribute('rx', '2');
  rect.setAttribute('ry', '2');
  if (dashed) {
    rect.setAttribute('stroke-dasharray', '4 2');
  }
  svg.appendChild(rect);
  return svg;
}

function createWireframeIcon(dashed: boolean): SVGSVGElement {
  const svg = createSvg(12, 12, '0 0 24 24');
  const polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
  polygon.setAttribute('points', '12 2 22 8.5 22 15.5 12 22 2 15.5 2 8.5 12 2');
  const line1 = document.createElementNS('http://www.w3.org/2000/svg', 'line');
  line1.setAttribute('x1', '12');
  line1.setAttribute('y1', '2');
  line1.setAttribute('x2', '12');
  line1.setAttribute('y2', '22');
  const line2 = document.createElementNS('http://www.w3.org/2000/svg', 'line');
  line2.setAttribute('x1', '2');
  line2.setAttribute('y1', '8.5');
  line2.setAttribute('x2', '22');
  line2.setAttribute('y2', '8.5');
  if (dashed) {
    polygon.setAttribute('stroke-dasharray', '3 2');
    line1.setAttribute('stroke-dasharray', '3 2');
    line2.setAttribute('stroke-dasharray', '3 2');
  }
  svg.appendChild(polygon);
  svg.appendChild(line1);
  svg.appendChild(line2);
  return svg;
}

function createExpandAllIcon(): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('width', '14');
  svg.setAttribute('height', '14');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'currentColor');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', 'M18 3a3 3 0 0 1 2.995 2.824l.005 .176v12a3 3 0 0 1 -2.824 2.995l-.176 .005h-12a3 3 0 0 1 -2.995 -2.824l-.005 -.176v-12a3 3 0 0 1 2.824 -2.995l.176 -.005h12zm1 6h-14v9a1 1 0 0 0 .883 .993l.117 .007h12a1 1 0 0 0 .993 -.883l.007 -.117v-9zm-8.387 3.21l.094 .083l1.293 1.292l1.293 -1.292a1 1 0 0 1 1.32 -.083l.094 .083a1 1 0 0 1 .083 1.32l-.083 .094l-2 2a1 1 0 0 1 -1.32 .083l-.094 -.083l-2 -2a1 1 0 0 1 1.32 -1.497z');
  svg.appendChild(path);
  return svg;
}

function createCollapseAllIcon(): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('width', '14');
  svg.setAttribute('height', '14');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'currentColor');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', 'M18 3a3 3 0 0 1 2.995 2.824l.005 .176v12a3 3 0 0 1 -2.824 2.995l-.176 .005h-12a3 3 0 0 1 -2.995 -2.824l-.005 -.176v-12a3 3 0 0 1 2.824 -2.995l.176 -.005h12zm1 6h-14v9a1 1 0 0 0 .883 .993l.117 .007h12a1 1 0 0 0 .993 -.883l.007 -.117v-9zm-6.387 3.21l.094 .083l2 2a1 1 0 0 1 -1.32 1.497l-.094 -.083l-1.293 -1.292l-1.293 1.292a1 1 0 0 1 -1.32 .083l-.094 -.083a1 1 0 0 1 -.083 -1.32l.083 -.094l2 -2a1 1 0 0 1 1.32 -.083z');
  svg.appendChild(path);
  return svg;
}

function getTypeIcon(type: string): SVGSVGElement {
  switch (type) {
    case 'Mesh':
    case 'SkinnedMesh':
      return createMeshIcon();
    case 'Bone':
      return createBoneIcon();
    case 'Group':
    case 'Object3D':
      return createGroupIcon();
    default:
      return createDefaultIcon();
  }
}

// Count total nodes in a tree (pure helper function)
function countNodes(node: SceneNode | null): number {
  if (!node) return 0;
  let count = 1;
  node.children?.forEach((child) => {
    count += countNodes(child);
  });
  return count;
}

interface UseSceneHierarchyGuiOptions {
  gui: GUI | null;
}

export function useSceneHierarchyGui({ gui }: UseSceneHierarchyGuiOptions) {
  const hierarchyFolderRef = useRef<GUI | null>(null);
  const treeContainerRef = useRef<HTMLDivElement | null>(null);
  const expandedNodesRef = useRef<Set<string>>(new Set());
  const modelExpandedRef = useRef(false);
  const platformExpandedRef = useRef(false);
  const updateTreeFnRef = useRef<(() => void) | null>(null);

  const {
    sceneGraph,
    platformSceneGraph,
    selectedNodeUuid,
    setSelectedNodeUuid,
    boundingBoxNodes,
    toggleBoundingBox,
    clearBoundingBoxes,
    wireframeNodes,
    toggleWireframe,
    hiddenNodes,
    toggleNodeVisibility,
  } = useViewerStore();

  useEffect(() => {
    expandedNodesRef.current.clear();
    modelExpandedRef.current = false;
  }, [sceneGraph]);

  useEffect(() => {
    platformExpandedRef.current = false;
  }, [platformSceneGraph]);

  useEffect(() => {
    const styleId = 'scene-hierarchy-gui-styles';
    if (!document.getElementById(styleId)) {
      const style = document.createElement('style');
      style.id = styleId;
      style.textContent = HIERARCHY_STYLES;
      document.head.appendChild(style);
    }
  }, []);

  const collectExpandableUuids = useCallback((node: SceneNode): string[] => {
    const uuids: string[] = [];
    const collect = (n: SceneNode) => {
      if (n.children && n.children.length > 0) {
        uuids.push(n.uuid);
        n.children.forEach(collect);
      }
    };
    collect(node);
    return uuids;
  }, []);

  const renderNodeRef = useRef<(node: SceneNode, depth: number) => HTMLElement>(undefined);

  const renderNode = useCallback(
    (node: SceneNode, depth: number): HTMLElement => {
      const container = document.createElement('div');
      container.className = 'scene-hierarchy-node-container';
      container.dataset.uuid = node.uuid;

      const hasChildren = node.children && node.children.length > 0;
      const isExpanded = expandedNodesRef.current.has(node.uuid);
      const isSelected = selectedNodeUuid === node.uuid;
      const isHidden = hiddenNodes.has(node.uuid);
      const hasBoundingBox = boundingBoxNodes.has(node.uuid);
      const hasWireframe = wireframeNodes.has(node.uuid);
      const isBone = node.type === 'Bone';

      const row = document.createElement('div');
      row.className = `scene-hierarchy-node${isSelected ? ' selected' : ''}${isHidden ? ' hidden' : ''}`;
      row.style.paddingLeft = `${depth * 8 + 4}px`;

      if (hasChildren) {
        const expandBtn = document.createElement('button');
        expandBtn.className = `scene-hierarchy-expand-btn${isExpanded ? ' expanded' : ''}`;
        expandBtn.appendChild(createChevronIcon());
        expandBtn.title = isExpanded ? 'Collapse' : 'Expand';
        expandBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          if (expandedNodesRef.current.has(node.uuid)) {
            expandedNodesRef.current.delete(node.uuid);
          } else {
            expandedNodesRef.current.add(node.uuid);
          }
          updateTreeFnRef.current?.();
        });
        row.appendChild(expandBtn);
      } else {
        const spacer = document.createElement('span');
        spacer.className = 'scene-hierarchy-spacer';
        row.appendChild(spacer);
      }

      const iconSpan = document.createElement('span');
      iconSpan.className = 'scene-hierarchy-icon';
      iconSpan.appendChild(getTypeIcon(node.type));
      row.appendChild(iconSpan);

      const nameSpan = document.createElement('span');
      nameSpan.className = 'scene-hierarchy-name';
      nameSpan.textContent = node.name || 'Unnamed';
      nameSpan.title = node.name || 'Unnamed';
      row.appendChild(nameSpan);

      if ((node.type === 'Mesh' || node.type === 'SkinnedMesh') && node.vertexCount) {
        const vertexSpan = document.createElement('span');
        vertexSpan.className = 'scene-hierarchy-vertex-count';
        vertexSpan.textContent = `${node.vertexCount.toLocaleString()}v`;
        row.appendChild(vertexSpan);
      }

      const actionsDiv = document.createElement('div');
      actionsDiv.className = 'scene-hierarchy-actions';

      if (!isBone) {
        const visBtn = document.createElement('button');
        visBtn.className = `scene-hierarchy-action-btn visibility${!isHidden ? ' active' : ''}`;
        visBtn.appendChild(isHidden ? createEyeClosedIcon() : createEyeOpenIcon());
        visBtn.title = isHidden ? 'Show' : 'Hide';
        visBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          toggleNodeVisibility(node.uuid);
        });
        actionsDiv.appendChild(visBtn);
      }

      const bboxBtn = document.createElement('button');
      bboxBtn.className = `scene-hierarchy-action-btn bbox${hasBoundingBox ? ' active' : ''}`;
      bboxBtn.appendChild(createBboxIcon(!hasBoundingBox));
      bboxBtn.title = hasBoundingBox ? 'Hide bounding box' : 'Show bounding box';
      bboxBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleBoundingBox(node.uuid);
      });
      actionsDiv.appendChild(bboxBtn);

      if (isBone) {
        const wireBtn = document.createElement('button');
        wireBtn.className = `scene-hierarchy-action-btn wireframe${hasWireframe ? ' active' : ''}`;
        wireBtn.appendChild(createWireframeIcon(!hasWireframe));
        wireBtn.title = hasWireframe ? 'Hide wireframe' : 'Show wireframe';
        wireBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          toggleWireframe(node.uuid);
        });
        actionsDiv.appendChild(wireBtn);
      }

      row.appendChild(actionsDiv);

      row.addEventListener('click', () => {
        setSelectedNodeUuid(selectedNodeUuid === node.uuid ? null : node.uuid);
      });

      container.appendChild(row);

      if (hasChildren) {
        const childrenDiv = document.createElement('div');
        childrenDiv.className = `scene-hierarchy-children${isExpanded ? ' expanded' : ''}`;
        node.children!.forEach((child) => {
          childrenDiv.appendChild(renderNodeRef.current!(child, depth + 1));
        });
        container.appendChild(childrenDiv);
      }

      return container;
    },
    [
      selectedNodeUuid,
      setSelectedNodeUuid,
      boundingBoxNodes,
      toggleBoundingBox,
      wireframeNodes,
      toggleWireframe,
      hiddenNodes,
      toggleNodeVisibility,
    ]
  );

  useLayoutEffect(() => {
    renderNodeRef.current = renderNode;
  }, [renderNode]);

  const updateTree = useCallback(() => {
    const container = treeContainerRef.current;
    if (!container) return;

    while (container.firstChild) {
      container.removeChild(container.firstChild);
    }

    const totalNodes = countNodes(sceneGraph) + countNodes(platformSceneGraph);

    const clearBtn = document.createElement('button');
    clearBtn.className = `scene-hierarchy-clear-btn${boundingBoxNodes.size > 0 ? ' visible' : ''}`;
    clearBtn.textContent = `Clear ${boundingBoxNodes.size} bbox`;
    clearBtn.addEventListener('click', clearBoundingBoxes);
    container.appendChild(clearBtn);

    if (sceneGraph) {
      const modelSection = document.createElement('div');
      modelSection.className = 'scene-hierarchy-section';

      const modelHeader = document.createElement('div');
      modelHeader.className = 'scene-hierarchy-section-header';

      const modelLabel = document.createElement('span');
      modelLabel.textContent = 'Model';
      modelHeader.appendChild(modelLabel);

      const modelCount = document.createElement('span');
      modelCount.className = 'scene-hierarchy-count';
      modelCount.textContent = `(${countNodes(sceneGraph)})`;
      modelHeader.appendChild(modelCount);

      const modelExpandBtn = document.createElement('button');
      modelExpandBtn.appendChild(modelExpandedRef.current ? createCollapseAllIcon() : createExpandAllIcon());
      modelExpandBtn.title = modelExpandedRef.current ? 'Collapse all' : 'Expand all';
      modelExpandBtn.addEventListener('click', () => {
        const uuids = collectExpandableUuids(sceneGraph);
        if (modelExpandedRef.current) {
          uuids.forEach((uuid) => expandedNodesRef.current.delete(uuid));
        } else {
          uuids.forEach((uuid) => expandedNodesRef.current.add(uuid));
        }
        modelExpandedRef.current = !modelExpandedRef.current;
        updateTreeFnRef.current?.();
      });
      modelHeader.appendChild(modelExpandBtn);

      modelSection.appendChild(modelHeader);
      modelSection.appendChild(renderNode(sceneGraph, 0));
      container.appendChild(modelSection);
    }

    if (platformSceneGraph) {
      const platformSection = document.createElement('div');
      platformSection.className = 'scene-hierarchy-section';

      const platformHeader = document.createElement('div');
      platformHeader.className = 'scene-hierarchy-section-header';
      platformHeader.style.borderTop = '1px solid var(--widget-color)';
      platformHeader.style.marginTop = '4px';
      platformHeader.style.paddingTop = '8px';

      const platformLabel = document.createElement('span');
      platformLabel.textContent = 'Platform';
      platformHeader.appendChild(platformLabel);

      const platformCount = document.createElement('span');
      platformCount.className = 'scene-hierarchy-count';
      platformCount.textContent = `(${countNodes(platformSceneGraph)})`;
      platformHeader.appendChild(platformCount);

      const platformExpandBtn = document.createElement('button');
      platformExpandBtn.appendChild(platformExpandedRef.current ? createCollapseAllIcon() : createExpandAllIcon());
      platformExpandBtn.title = platformExpandedRef.current ? 'Collapse all' : 'Expand all';
      platformExpandBtn.addEventListener('click', () => {
        const uuids = collectExpandableUuids(platformSceneGraph);
        if (platformExpandedRef.current) {
          uuids.forEach((uuid) => expandedNodesRef.current.delete(uuid));
        } else {
          uuids.forEach((uuid) => expandedNodesRef.current.add(uuid));
        }
        platformExpandedRef.current = !platformExpandedRef.current;
        updateTreeFnRef.current?.();
      });
      platformHeader.appendChild(platformExpandBtn);

      platformSection.appendChild(platformHeader);
      platformSection.appendChild(renderNode(platformSceneGraph, 0));
      container.appendChild(platformSection);
    }

    if (hierarchyFolderRef.current) {
      const titleEl = hierarchyFolderRef.current.domElement.querySelector('.title');
      if (titleEl) {
        titleEl.textContent = `Scene Hierarchy (${totalNodes})`;
      }
    }
  }, [
    sceneGraph,
    platformSceneGraph,
    boundingBoxNodes,
    clearBoundingBoxes,
    collectExpandableUuids,
    renderNode,
  ]);

  useEffect(() => {
    updateTreeFnRef.current = updateTree;
  }, [updateTree]);

  useEffect(() => {
    if (!gui) return;

    const hierarchyFolder = gui.addFolder('Scene Hierarchy');
    hierarchyFolderRef.current = hierarchyFolder;

    const treeContainer = document.createElement('div');
    treeContainer.className = 'scene-hierarchy-tree';
    treeContainerRef.current = treeContainer;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const folderChildren = (hierarchyFolder as any).$children as HTMLElement;
    if (folderChildren) {
      folderChildren.appendChild(treeContainer);
    }

    const guiChildren = gui.domElement.querySelector('.children');
    if (guiChildren && hierarchyFolder.domElement.parentElement) {
      guiChildren.insertBefore(hierarchyFolder.domElement, guiChildren.firstChild);
    }

    hierarchyFolder.close();

    return () => {
      hierarchyFolder.destroy();
      hierarchyFolderRef.current = null;
      treeContainerRef.current = null;
    };
  }, [gui]);

  useEffect(() => {
    if (!treeContainerRef.current) return;
    updateTree();
  }, [
    sceneGraph,
    platformSceneGraph,
    selectedNodeUuid,
    boundingBoxNodes,
    wireframeNodes,
    hiddenNodes,
    updateTree,
  ]);

  return hierarchyFolderRef;
}
