import { useEffect, useRef } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

export function useHighlightText(containerRef?: React.RefObject<HTMLElement>) {
  const location = useLocation();
  const navigate = useNavigate();
  const hasHighlighted = useRef(false);
  const retryCount = useRef(0);
  const highlightMarkRef = useRef<HTMLElement | null>(null);
  const lastHighlightTextRef = useRef<string | null>(null);

  useEffect(() => {
    const state = location.state as {
      highlightText?: string;
      matchLocation?: 'Sidebar' | 'Detail';
      entityId?: string;
    } | null;
    const highlightText = state?.highlightText;
    const matchLocation = state?.matchLocation || 'Sidebar';
    const entityId = state?.entityId;

    if (!highlightText) {
      return;
    }

    if (highlightText !== lastHighlightTextRef.current) {
      if (highlightMarkRef.current) {
        const parent = highlightMarkRef.current.parentNode;
        if (parent) {
          const textNode = document.createTextNode(highlightMarkRef.current.textContent || '');
          parent.replaceChild(textNode, highlightMarkRef.current);
          parent.normalize();
        }
        highlightMarkRef.current = null;
      }
      hasHighlighted.current = false;
      lastHighlightTextRef.current = highlightText;
    }

    if (hasHighlighted.current) {
      return;
    }

    const maxRetries = 10;
    const retryDelay = 200;

    const attemptHighlight = () => {
      const container = containerRef?.current || document.body;
      const mark = findAndHighlightText(container, highlightText, matchLocation, entityId);

      if (mark) {
        hasHighlighted.current = true;
        highlightMarkRef.current = mark;
        retryCount.current = 0;

        setTimeout(() => {
          const newState = { ...(location.state as object) } as Record<string, unknown>;
          delete newState.highlightText;
          delete newState.matchLocation;
          delete newState.entityId;
          navigate(location.pathname + location.search, { replace: true, state: newState });
        }, 2000);
      } else if (retryCount.current < maxRetries) {
        retryCount.current++;
        setTimeout(attemptHighlight, retryDelay);
      }
    };

    const timer = setTimeout(attemptHighlight, 300);

    return () => {
      clearTimeout(timer);
      retryCount.current = 0;
    };
  }, [location.state, location.pathname, location.search, containerRef, navigate]);

  useEffect(() => {
    if (highlightMarkRef.current) {
      const parent = highlightMarkRef.current.parentNode;
      if (parent) {
        const textNode = document.createTextNode(highlightMarkRef.current.textContent || '');
        parent.replaceChild(textNode, highlightMarkRef.current);
        parent.normalize();
      }
      highlightMarkRef.current = null;
    }

    hasHighlighted.current = false;
    retryCount.current = 0;
    lastHighlightTextRef.current = null;
  }, [location.pathname]);
}

function findAndHighlightText(
  container: HTMLElement,
  searchText: string,
  matchLocation: 'Sidebar' | 'Detail',
  entityId?: string
): HTMLElement | null {
  const lowerSearchText = searchText.toLowerCase();
  let searchContainer: HTMLElement | null = null;

  if (matchLocation === 'Sidebar' && entityId) {
    const sidebarElement = container.querySelector('aside');
    if (sidebarElement) {
      const entityElement = sidebarElement.querySelector(`[id$="-${entityId}"]`);
      searchContainer = entityElement as HTMLElement || sidebarElement;
    }
  } else if (matchLocation === 'Sidebar') {
    searchContainer = container.querySelector('aside');
  } else {
    searchContainer = container.querySelector('main');
  }

  if (!searchContainer) {
    searchContainer = container;
  }

  const fullText = searchContainer.textContent || '';
  const lowerFullText = fullText.toLowerCase();
  const matchIndex = lowerFullText.indexOf(lowerSearchText);

  if (matchIndex === -1) {
    return null;
  }

  let currentPosition = 0;
  let targetNode: Text | null = null;
  let targetOffset = 0;

  const walker = document.createTreeWalker(
    searchContainer,
    NodeFilter.SHOW_TEXT,
    {
      acceptNode: (node) => {
        const parent = node.parentElement;
        if (!parent) return NodeFilter.FILTER_REJECT;

        const tagName = parent.tagName.toLowerCase();
        if (tagName === 'script' || tagName === 'style' || tagName === 'mark') {
          return NodeFilter.FILTER_REJECT;
        }

        return NodeFilter.FILTER_ACCEPT;
      }
    }
  );

  let node: Text | null;
  while ((node = walker.nextNode() as Text | null)) {
    const nodeLength = node.textContent?.length || 0;

    if (currentPosition <= matchIndex && matchIndex < currentPosition + nodeLength) {
      targetNode = node;
      targetOffset = matchIndex - currentPosition;
      break;
    }

    currentPosition += nodeLength;
  }

  if (!targetNode || !targetNode.parentElement) {
    return null;
  }

  try {
    const range = document.createRange();
    range.setStart(targetNode, targetOffset);

    const matchEndIndex = matchIndex + searchText.length;
    let endPosition = 0;
    let endNode: Text | null = null;
    let endOffset = 0;

    const endWalker = document.createTreeWalker(
      searchContainer,
      NodeFilter.SHOW_TEXT,
      {
        acceptNode: (node) => {
          const parent = node.parentElement;
          if (!parent) return NodeFilter.FILTER_REJECT;
          const tagName = parent.tagName.toLowerCase();
          if (tagName === 'script' || tagName === 'style' || tagName === 'mark') {
            return NodeFilter.FILTER_REJECT;
          }
          return NodeFilter.FILTER_ACCEPT;
        }
      }
    );

    while ((node = endWalker.nextNode() as Text | null)) {
      const nodeLength = node.textContent?.length || 0;

      if (endPosition + nodeLength >= matchEndIndex) {
        endNode = node;
        endOffset = matchEndIndex - endPosition;
        break;
      }

      endPosition += nodeLength;
    }

    if (!endNode) {
      endNode = targetNode;
      endOffset = targetNode.textContent?.length || 0;
    }

    range.setEnd(endNode, endOffset);

    const mark = document.createElement('mark');
    mark.className = 'search-highlight';
    range.surroundContents(mark);

    const scrollContainer = findScrollableParent(mark);

    if (scrollContainer) {
      const containerRect = scrollContainer.getBoundingClientRect();
      const markRect = mark.getBoundingClientRect();

      const scrollTop = scrollContainer.scrollTop +
        (markRect.top - containerRect.top) -
        (containerRect.height / 2) +
        (markRect.height / 2);

      scrollContainer.scrollTo({
        top: Math.max(0, scrollTop),
        behavior: 'smooth'
      });
    } else {
      mark.scrollIntoView({
        behavior: 'smooth',
        block: 'center',
      });
    }

    return mark;
  } catch (error) {
    console.warn('Failed to highlight text:', error);
    return null;
  }
}

function findScrollableParent(element: HTMLElement): HTMLElement | null {
  let parent = element.parentElement;

  while (parent) {
    const style = window.getComputedStyle(parent);
    const overflowY = style.overflowY;

    if (overflowY === 'auto' || overflowY === 'scroll') {
      if (parent.scrollHeight > parent.clientHeight) {
        return parent;
      }
    }

    parent = parent.parentElement;
  }

  return null;
}

if (typeof document !== 'undefined') {
  const styleId = 'search-highlight-styles';
  if (!document.getElementById(styleId)) {
    const style = document.createElement('style');
    style.id = styleId;
    style.textContent = `
      mark.search-highlight {
        background-color: #ffeb3b;
        color: inherit;
        padding: 0;
        border-radius: 2px;
        animation: search-highlight-fade 2s ease-out forwards;
      }

      @keyframes search-highlight-fade {
        0% {
          background-color: #ffeb3b;
        }
        70% {
          background-color: #ffeb3b;
        }
        100% {
          background-color: transparent;
        }
      }
    `;
    document.head.appendChild(style);
  }
}
