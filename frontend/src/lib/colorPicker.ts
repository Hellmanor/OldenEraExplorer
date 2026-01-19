import Picker from 'vanilla-picker';
import type { Controller } from 'lil-gui';

interface ColorController extends Controller {
  $display: HTMLElement;
  $input: HTMLInputElement;
}

interface PickerColor {
  hex: string;
  rgbaString: string;
}

/**
 * Attaches a vanilla-picker to a lil-gui color controller,
 * replacing the native browser color picker with a modern one.
 */
export function attachColorPicker(controller: Controller): void {
  const colorCtrl = controller as ColorController;
  const display = colorCtrl.$display;
  const nativeInput = colorCtrl.$input;

  if (!display || !nativeInput) {
    console.warn('Invalid color controller - missing $display or $input');
    return;
  }

  // Native input interferes with vanilla-picker, must remove completely
  nativeInput.remove();

  let currentColor = nativeInput.value || '#ffffff';

  // Overlay blocks OrbitControls camera rotation while picker is open
  const overlay = document.createElement('div');
  overlay.style.cssText = `
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    z-index: 9999;
    display: none;
  `;
  document.body.appendChild(overlay);

  const popupWrapper = document.createElement('div');
  popupWrapper.style.position = 'fixed';
  popupWrapper.style.zIndex = '10000';
  document.body.appendChild(popupWrapper);

  let isOpen = false;

  const openPicker = () => {
    const rect = display.getBoundingClientRect();
    popupWrapper.style.left = `${rect.left - 220}px`;
    popupWrapper.style.top = `${rect.top}px`;
    overlay.style.display = 'block';
    popupWrapper.style.display = 'block';
    isOpen = true;
  };

  const closePicker = () => {
    overlay.style.display = 'none';
    popupWrapper.style.display = 'none';
    isOpen = false;
  };

  new Picker({
    parent: popupWrapper,
    popup: false,
    color: currentColor,
    alpha: false,
    editor: true,
    editorFormat: 'hex',
    onChange: (color: PickerColor) => {
      const hex = color.hex.substring(0, 7);
      currentColor = hex;
      display.style.backgroundColor = hex;
      colorCtrl.setValue(hex);
    },
  });

  popupWrapper.style.display = 'none';

  display.addEventListener('click', (e) => {
    e.stopPropagation();
    if (isOpen) {
      closePicker();
      return;
    }
    openPicker();
  });

  overlay.addEventListener('click', () => {
    closePicker();
  });

  const originalUpdateDisplay = controller.updateDisplay.bind(controller);
  controller.updateDisplay = function () {
    const result = originalUpdateDisplay();
    const value = colorCtrl.getValue();
    if (typeof value === 'string' && value !== currentColor) {
      currentColor = value;
    }
    return result;
  };
}
