// 沉浸式：安全区注入 + 状态栏图标风格判定（由 ImmersiveCoordinator 通过 JS interop 动态 import）。
//
// 一、安全区
//   原生宿主把测得的安全区交给 setSafeArea → 写入 html 的 --ft-native-safe-*；
//   样式统一读 --ft-safe-*（app.css：优先原生值，回落 env(safe-area-inset-*)，再回落 0）。
//
// 二、状态栏图标风格
//   对屏幕顶部像素（水平居中、y=1）做命中检测，沿命中栈自上而下找到第一个
//   「声明了 data-statusbar-bg」或「背景近乎不透明」的元素：背景深 → 浅色图标，背景浅 → 深色图标。
//   结果写入 <html data-statusbar-icons="light|dark">，并回调 .NET 交给原生状态栏服务。
//   页面可在背景元素上声明 data-statusbar-bg="dark|light" 显式告知（渐变 / 图片等无法采样的背景必须声明）。
//   声明元素必须能被命中检测命中（不要给它设 pointer-events: none）。
//   滚动、页面切换（PageStack 的转场）、DOM / 样式变化都会重新判定，转场期间逐帧判定以便在转场中途跟随切换。

const ROOT = document.documentElement;
const DECLARE_ATTR = 'data-statusbar-bg';
const ICONS_ATTR = 'data-statusbar-icons';
const NATIVE_VARS = ['top', 'right', 'bottom', 'left'].map((side) => `--ft-native-safe-${side}`);

/** 背景有效不透明度达到该值才视为"遮住了下层" */
const OPAQUE_ALPHA = 0.6;
/** 背景相对亮度低于该值视为深色背景（对应浅色图标） */
const DARK_LUMINANCE = 0.3;
/** 转场期间逐帧判定的持续时间（毫秒），略长于 PageStack 转场 */
const TRANSITION_POLL_MS = 600;
const VIEWPORT_DEBOUNCE_MS = 120;

let dotNetRef = null;
let currentIcons = null;
let frameRequested = false;
let pollUntil = 0;
let viewportTimer = 0;
let mutationObserver = null;
let listening = false;
let colorCanvasContext = null;

/**
 * 写入原生安全区（单位 CSS 像素）。
 */
export function setSafeArea(top, right, bottom, left) {
  const values = [top, right, bottom, left];
  NATIVE_VARS.forEach((name, index) => {
    ROOT.style.setProperty(name, `${Math.max(0, Number(values[index]) || 0)}px`);
  });
}

/**
 * 清除原生安全区（回落到 env() / 0）。
 */
export function clearSafeArea() {
  NATIVE_VARS.forEach((name) => ROOT.style.removeProperty(name));
}

/**
 * 开始监听页面并立即判定一次状态栏图标风格。
 */
export function start(ref) {
  stop();
  dotNetRef = ref;
  listening = true;

  document.addEventListener('scroll', scheduleEvaluate, { capture: true, passive: true });
  document.addEventListener('transitionrun', onTransitionStart, true);
  document.addEventListener('animationstart', onTransitionStart, true);
  document.addEventListener('transitionend', scheduleEvaluate, true);
  document.addEventListener('animationend', scheduleEvaluate, true);
  window.addEventListener('resize', onViewportChanged, { passive: true });
  window.addEventListener('orientationchange', onViewportChanged, { passive: true });
  window.visualViewport?.addEventListener('resize', onViewportChanged, { passive: true });

  mutationObserver = new MutationObserver(scheduleEvaluate);
  mutationObserver.observe(document.body, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ['class', 'style', 'hidden', DECLARE_ATTR],
  });

  evaluate();
}

/**
 * 停止监听并清理根属性（不清安全区变量，交由 clearSafeArea 决定）。
 */
export function stop() {
  if (!listening) {
    return;
  }

  listening = false;
  document.removeEventListener('scroll', scheduleEvaluate, { capture: true });
  document.removeEventListener('transitionrun', onTransitionStart, true);
  document.removeEventListener('animationstart', onTransitionStart, true);
  document.removeEventListener('transitionend', scheduleEvaluate, true);
  document.removeEventListener('animationend', scheduleEvaluate, true);
  window.removeEventListener('resize', onViewportChanged);
  window.removeEventListener('orientationchange', onViewportChanged);
  window.visualViewport?.removeEventListener('resize', onViewportChanged);
  mutationObserver?.disconnect();
  mutationObserver = null;
  window.clearTimeout(viewportTimer);
  dotNetRef = null;
  currentIcons = null;
  ROOT.removeAttribute(ICONS_ATTR);
}

function onTransitionStart() {
  pollUntil = performance.now() + TRANSITION_POLL_MS;
  scheduleEvaluate();
}

function onViewportChanged() {
  scheduleEvaluate();
  window.clearTimeout(viewportTimer);
  viewportTimer = window.setTimeout(() => {
    dotNetRef?.invokeMethodAsync('OnViewportChanged').catch(() => {});
  }, VIEWPORT_DEBOUNCE_MS);
}

function scheduleEvaluate() {
  if (frameRequested || !listening) {
    return;
  }

  frameRequested = true;
  window.requestAnimationFrame(() => {
    frameRequested = false;
    if (!listening) {
      return;
    }

    evaluate();
    if (performance.now() < pollUntil) {
      scheduleEvaluate();
    }
  });
}

function evaluate() {
  applyIcons(resolveIcons());
}

function applyIcons(icons) {
  if (icons === currentIcons) {
    return;
  }

  currentIcons = icons;
  ROOT.setAttribute(ICONS_ATTR, icons);
  dotNetRef?.invokeMethodAsync('OnStatusBarIconsChanged', icons).catch(() => {});
}

/**
 * 沿命中栈（自上而下）找到状态栏下方的背景，返回应使用的图标风格。
 * 找不到任何不透明背景时按浅色页面处理（深色图标）。
 */
function resolveIcons() {
  const x = Math.max(0, Math.floor(window.innerWidth / 2));
  const stack = document.elementsFromPoint(x, 1);
  for (const element of stack) {
    if (element === ROOT) {
      continue;
    }

    const declared = element.getAttribute(DECLARE_ATTR);
    if (declared === 'dark') {
      return 'light';
    }

    if (declared === 'light') {
      return 'dark';
    }

    const luminance = opaqueBackgroundLuminance(element);
    if (luminance !== null) {
      return luminance < DARK_LUMINANCE ? 'light' : 'dark';
    }
  }

  return 'dark';
}

/**
 * 元素自身背景色足够不透明时返回其相对亮度（0..1），否则返回 null（继续看下层）。
 * 渐变 / 图片背景无法采样，需要页面用 data-statusbar-bg 声明。
 */
function opaqueBackgroundLuminance(element) {
  const style = window.getComputedStyle(element);
  const opacity = Number.parseFloat(style.opacity);
  if (Number.isFinite(opacity) && opacity < 0.05) {
    return null;
  }

  const color = parseColor(style.backgroundColor);
  if (!color) {
    return null;
  }

  const effectiveAlpha = color.a * (Number.isFinite(opacity) ? opacity : 1);
  if (effectiveAlpha < OPAQUE_ALPHA) {
    return null;
  }

  return relativeLuminance(color);
}

function parseColor(value) {
  if (!value || value === 'transparent') {
    return null;
  }

  const match = value.match(/^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+%?))?\s*\)$/i);
  if (match) {
    const alphaText = match[4];
    let alpha = 1;
    if (alphaText !== undefined) {
      alpha = alphaText.endsWith('%') ? Number.parseFloat(alphaText) / 100 : Number.parseFloat(alphaText);
    }

    return { r: Number(match[1]), g: Number(match[2]), b: Number(match[3]), a: alpha };
  }

  // color(srgb …) / oklab(…) 等现代色彩空间：借 canvas 转成 sRGB 取值
  return parseColorViaCanvas(value);
}

function parseColorViaCanvas(value) {
  try {
    if (!colorCanvasContext) {
      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      colorCanvasContext = canvas.getContext('2d', { willReadFrequently: true });
    }

    if (!colorCanvasContext) {
      return null;
    }

    colorCanvasContext.clearRect(0, 0, 1, 1);
    colorCanvasContext.fillStyle = '#000';
    colorCanvasContext.fillStyle = value;
    colorCanvasContext.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = colorCanvasContext.getImageData(0, 0, 1, 1).data;
    return { r, g, b, a: a / 255 };
  } catch {
    return null;
  }
}

function relativeLuminance({ r, g, b }) {
  const channel = (v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };

  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}
