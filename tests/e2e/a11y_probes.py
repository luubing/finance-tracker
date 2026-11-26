# -*- coding: utf-8 -*-
"""
无障碍与降级加固（iOS UI 重构 · 23 号 ticket）的页面探针：在页面里取数的 JS 片段 + Python 侧的「逐屏扫描」驱动。

本模块不是测试脚本，被 a11y_degradation_test.py 使用（也可以单独 `python a11y_probes.py` 打印一份当前的体检清单，
见文件末尾的 inventory）。探针只读页面状态，不改业务数据；会滚动页面的滚动容器去覆盖整页，扫完滚回顶部。

探针一览（名字 → 返回值）：
  glass      每屏玻璃层：视口内、可见、有尺寸、backdrop-filter 非 none 的元素标识列表
  materials  材质体检：所有玻璃表面的 backdrop-filter / 背景不透明度，以及屏幕上还有多少元素带 backdrop-filter
  motion     动效体检：所有元素（含 ::before / ::after）上超过阈值的 CSS 动画 / 过渡，以及 scroll-behavior: smooth
  hits       点击热区：每个完整可见的可交互元素的「实际可点击区域」宽 × 高（射线法：沿水平 / 垂直中线数连续命中的像素）
  contrast   文字对比度：每个可见文字节点相对其背景（逐层合成祖先背景；渐变取各色标的最不利值）的 WCAG 对比度
  overflow   布局：横向溢出、越出视口的元素、被固定高度裁掉的文字

文字缩放（模拟系统「字体大小」调到 130%）由 TEXT_ZOOM_JS 完成：把排版令牌 --ft-type-* 与样式表里的 px 字号 / 行高整体放大。
Chromium 桌面版没有 Android 那样的「文字缩放」设置，只能用样式改写近似——它与 Android WebView 的系统字号缩放不完全等价
（图标字体、第三方样式表不被缩放），真机上的大字号表现见 ticket 23 的人工验收清单。
"""
from __future__ import annotations

import io
import re
import sys
from typing import Callable, Iterable, Optional

import numpy as np
from PIL import Image
from playwright.sync_api import Page
from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

# ---------------------------------------------------------------------------------------------- 公共 JS 片段

# 元素的人类可读标识（标签名.类名 [data-testid] “aria-label” 或 「文字」）
_LABEL_JS = r"""
const labelOf = (el) => {
  const tid = el.getAttribute('data-testid');
  const aria = el.getAttribute('aria-label');
  const text = (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 14);
  const cls = [...el.classList].filter((c) => c.startsWith('ft-') || c.startsWith('m-')).slice(0, 2).join('.');
  return [el.tagName.toLowerCase() + (cls ? '.' + cls : ''), tid ? `[${tid}]` : '', aria ? `“${aria}”` : (text ? `「${text}」` : '')]
    .filter(Boolean).join(' ');
};
"""

# 颜色解析 / 合成 / 对比度
_COLOR_JS = r"""
let _canvas = null;
const parseColor = (value) => {
  if (!value || value === 'transparent') return null;
  const m = value.match(/^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+%?))?\s*\)$/i);
  if (m) {
    let a = 1;
    if (m[4] !== undefined) a = m[4].endsWith('%') ? parseFloat(m[4]) / 100 : parseFloat(m[4]);
    return { r: +m[1], g: +m[2], b: +m[3], a };
  }
  try {  // color(srgb …) / oklab(…) 等：借 canvas 转成 sRGB
    if (!_canvas) { _canvas = document.createElement('canvas'); _canvas.width = 1; _canvas.height = 1; }
    const ctx = _canvas.getContext('2d', { willReadFrequently: true });
    ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = '#000'; ctx.fillStyle = value; ctx.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
    return { r, g, b, a: a / 255 };
  } catch { return null; }
};
const over = (top, bottom) => {
  const a = top.a + bottom.a * (1 - top.a);
  if (a <= 0) return { r: 0, g: 0, b: 0, a: 0 };
  const mix = (t, b) => (t * top.a + b * bottom.a * (1 - top.a)) / a;
  return { r: mix(top.r, bottom.r), g: mix(top.g, bottom.g), b: mix(top.b, bottom.b), a };
};
const lum = ({ r, g, b }) => {
  const ch = (v) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b);
};
const ratio = (a, b) => { const la = lum(a), lb = lum(b); return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05); };
"""

# 元素是否在视口内可见且没有被别的元素盖住（取矩形中心点命中检测）
_VISIBLE_JS = r"""
const vw = document.documentElement.clientWidth, vh = window.innerHeight;
const inViewport = (r) => r.width >= 1 && r.height >= 1 && r.bottom > 0 && r.top < vh && r.right > 0 && r.left < vw;
const hidden = (el) => {
  for (let a = el; a && a.nodeType === 1; a = a.parentElement) {
    const s = getComputedStyle(a);
    if (s.display === 'none' || s.visibility === 'hidden' || s.visibility === 'collapse') return true;
  }
  return false;
};
"""

GLASS_JS = r"""() => {
  %(label)s
  const out = [];
  for (const el of document.querySelectorAll('*')) {
    const cs = getComputedStyle(el);
    const bf = cs.backdropFilter || cs.webkitBackdropFilter;
    if (!bf || bf === 'none' || cs.visibility === 'hidden' || cs.display === 'none') continue;
    const r = el.getBoundingClientRect();
    if (r.width < 2 || r.height < 2 || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) continue;
    out.push(labelOf(el));
  }
  return out;
}""" % {"label": _LABEL_JS}

MATERIALS_JS = r"""() => {
  %(label)s
  %(color)s
  const SURFACES = '.ft-glass, .ft-glass-dark, .ft-glass-alert, .ft-alert, .ft-voice, .m-enqueued-snackbars .m-snack__wrapper';
  const rootStyle = getComputedStyle(document.documentElement);
  const tokens = {};
  for (const name of ['--ft-glass-bg', '--ft-glass-filter', '--ft-glass-dark-bg', '--ft-glass-dark-filter', '--ft-glass-alert-bg', '--ft-voice-bg', '--ft-voice-filter']) {
    tokens[name] = rootStyle.getPropertyValue(name).trim();
  }
  const filtered = [];
  const surfaces = [];
  for (const el of document.querySelectorAll('*')) {
    const cs = getComputedStyle(el);
    if (cs.display === 'none') continue;
    const bf = cs.backdropFilter || cs.webkitBackdropFilter;
    if (bf && bf !== 'none') filtered.push(labelOf(el));
    if (el.matches(SURFACES)) {
      const bg = parseColor(cs.backgroundColor);
      let alpha = bg ? bg.a : 0;
      // 状态改成实色渐变（例如录音中的麦克风钮换成品牌渐变）时，背景色是透明的、但图像层把表面铺满了：按渐变色标的最小不透明度算
      const image = cs.backgroundImage;
      if (image && image !== 'none' && !image.includes('url(')) {
        const stops = [...image.matchAll(/rgba?\([^)]*\)/g)].map((m) => parseColor(m[0])).filter(Boolean);
        if (stops.length && stops.every((c) => c.a >= 0.99)) alpha = 1;
      }
      surfaces.push({ label: labelOf(el), alpha, filter: bf || 'none', color: cs.backgroundColor + (image && image !== 'none' ? ' + ' + image.slice(0, 40) : '') });
    }
  }
  return { tokens, filtered, surfaces, solid: document.documentElement.getAttribute('data-ft-solid') };
}""" % {"label": _LABEL_JS, "color": _COLOR_JS}

# 动效体检：threshold 毫秒以内的过渡 / 动画视为「短淡入」放行；无限循环或更长的列出来。
# allow 是放行的选择器（加载转圈、光标闪烁——它们是状态指示而不是装饰性运动）
MOTION_JS = r"""([threshold, allow]) => {
  %(label)s
  const secs = (list) => list.split(',').map((s) => { s = s.trim(); return s.endsWith('ms') ? parseFloat(s) : parseFloat(s) * 1000; });
  const out = [];
  const seen = new Set();
  const check = (el, pseudo) => {
    const cs = getComputedStyle(el, pseudo);
    if (cs.display === 'none') return;
    const where = labelOf(el) + (pseudo || '');
    const names = cs.animationName.split(',').map((s) => s.trim());
    if (names.some((n) => n !== 'none')) {
      const durations = secs(cs.animationDuration), iterations = cs.animationIterationCount.split(',').map((s) => s.trim());
      const delays = secs(cs.animationDelay);
      names.forEach((name, i) => {
        if (name === 'none') return;
        const duration = durations[i %% durations.length], iteration = iterations[i %% iterations.length], delay = delays[i %% delays.length];
        if (duration + Math.max(0, delay) > threshold || iteration === 'infinite') {
          const key = `a|${where}|${name}`; if (!seen.has(key)) { seen.add(key); out.push({ kind: 'animation', where, name, duration, iteration }); }
        }
      });
    }
    const props = cs.transitionProperty.split(',').map((s) => s.trim());
    const tdur = secs(cs.transitionDuration), tdelay = secs(cs.transitionDelay);
    props.forEach((prop, i) => {
      const duration = tdur[i %% tdur.length], delay = tdelay[i %% tdelay.length];
      if (prop === 'none' || duration + Math.max(0, delay) <= threshold) return;
      const key = `t|${where}|${prop}`; if (!seen.has(key)) { seen.add(key); out.push({ kind: 'transition', where, name: prop, duration }); }
    });
    if (!pseudo && cs.scrollBehavior === 'smooth') {
      const key = `s|${where}`; if (!seen.has(key)) { seen.add(key); out.push({ kind: 'scroll-behavior', where, name: 'smooth', duration: 0 }); }
    }
  };
  for (const el of document.querySelectorAll('*')) {
    const allowed = allow.length && el.closest(allow.join(','));
    if (allowed) continue;
    check(el, null); check(el, '::before'); check(el, '::after');
  }
  return out;
}""" % {"label": _LABEL_JS}

# 点击热区（射线法）。只测「完整可见」的元素：矩形内 5 × 5 采样点至少 80% 命中自身（被 Tab 栏 / 遮罩盖住、被横向滚动裁掉的跳过）。
HITS_JS = r"""() => {
  %(label)s
  %(visible)s
  const SELECTOR = [
    'a[href]', 'button', 'input:not([type="hidden"])', 'select', 'textarea', 'summary',
    '[role="button"]', '[role="tab"]', '[role="switch"]', '[role="checkbox"]', '[role="radio"]', '[role="menuitem"]',
    '[role="option"]', '[role="link"]', '[data-hold-click]', '[tabindex]:not([tabindex="-1"])',
  ].join(',');
  const owns = (el, t) => !!t && (t === el || el.contains(t));
  const clear = (el, r) => {
    let hit = 0, total = 0;
    for (let i = 0; i < 5; i++) for (let j = 0; j < 5; j++) {
      const x = r.left + r.width * (i + 0.5) / 5, y = r.top + r.height * (j + 0.5) / 5;
      if (x < 0 || y < 0 || x >= vw || y >= vh) continue;
      total++;
      if (owns(el, document.elementFromPoint(x, y))) hit++;
    }
    return total >= 15 && hit / total >= 0.8;
  };
  const run = (el, cx, cy, dx, dy) => {
    let a = 0, b = 0;
    while (a < 160 && owns(el, document.elementFromPoint(cx - dx * (a + 1), cy - dy * (a + 1)))) a++;
    while (b < 160 && owns(el, document.elementFromPoint(cx + dx * (b + 1), cy + dy * (b + 1)))) b++;
    return a + b + 1;
  };
  // 元素有一部分被视口边缘 / 滚动容器的折线裁掉时，射线只能量到露出来的那一段（露出八成以上的行会被量成 36–44）；
  // 这种方向上改用元素自己的边框盒尺寸——裁掉的部分不是「点不到」，只是还没滚进来。
  // （注意：本字符串要经过 Python 的字符串格式化，JS 与注释里都不能直接写百分号）
  const cutOf = (el, r) => {
    let v = r.top < -0.5 || r.bottom > vh + 0.5, h = r.left < -0.5 || r.right > vw + 0.5;
    for (let p = el.parentElement; p && p !== document.documentElement; p = p.parentElement) {
      const s = getComputedStyle(p), clipX = s.overflowX !== 'visible', clipY = s.overflowY !== 'visible';
      if (!clipX && !clipY) continue;
      const pr = p.getBoundingClientRect();
      if (clipY && (r.top < pr.top - 0.5 || r.bottom > pr.bottom + 0.5)) v = true;
      if (clipX && (r.left < pr.left - 0.5 || r.right > pr.right + 0.5)) h = true;
    }
    return { v, h };
  };
  const measureAt = (el) => {
    const r = el.getBoundingClientRect();
    if (!inViewport(r) || !clear(el, r)) return null;
    const cx = r.left + r.width / 2, cy = r.top + r.height / 2, cut = cutOf(el, r);
    let w = run(el, cx, cy, 1, 0), h = run(el, cx, cy, 0, 1);
    if (cut.h) w = Math.max(w, Math.round(r.width));
    if (cut.v) h = Math.max(h, Math.round(r.height));
    return { w, h, rectW: Math.round(r.width * 10) / 10, rectH: Math.round(r.height * 10) / 10 };
  };
  // 吸底的数字键盘 / 吸顶的栏会盖住滚动容器的一头，露出来的区域比容器自己小，按固定步长采样的滚动位置之间会留下缝：
  // 有的行在每个采样位置都只露出八九成。所以量出来不达标的元素，当场滚到容器的顶端、底端、正中再各量一次取最大，量完把滚动位置还原——
  // 在最好的位置也点不全（被永久盖住 / 本身就小）的才是真问题。
  const scrollState = (el) => {
    const list = [];
    for (let p = el.parentElement; p; p = p.parentElement) {
      if (p.scrollHeight > p.clientHeight || p.scrollWidth > p.clientWidth) list.push([p, p.scrollTop, p.scrollLeft]);
    }
    return { list, x: window.scrollX, y: window.scrollY };
  };
  const restore = (state) => {
    for (const [p, top, left] of state.list) { p.scrollTop = top; p.scrollLeft = left; }
    window.scrollTo(state.x, state.y);
  };
  const out = [];
  const seen = new Set();
  for (const el of [...document.querySelectorAll(SELECTOR)]) {
    if (seen.has(el)) continue;
    seen.add(el);
    if (el.matches(':disabled') || el.closest('[aria-disabled="true"], [inert]')) continue;
    if (el.tagName === 'INPUT' && ['checkbox', 'radio'].includes(el.type) && el.closest('label')) continue; // 点它所在的 label
    if (hidden(el) || getComputedStyle(el).pointerEvents === 'none') continue;
    const m = measureAt(el);
    if (!m) continue;
    if (m.w < 44 || m.h < 44) {
      const state = scrollState(el);
      for (const block of ['start', 'end', 'center']) {
        el.scrollIntoView({ block, inline: 'nearest', behavior: 'instant' });
        const again = measureAt(el);
        if (again) { m.w = Math.max(m.w, again.w); m.h = Math.max(m.h, again.h); }
        if (m.w >= 44 && m.h >= 44) break;
      }
      restore(state);
    }
    out.push({ label: labelOf(el), w: m.w, h: m.h, rectW: m.rectW, rectH: m.rectH });
  }
  return out;
}""" % {"label": _LABEL_JS, "visible": _VISIBLE_JS}

# 文字对比度（解析法）。
# - 背景：从文字所在元素逐级向上收集背景色 / 渐变，合成到遇到不透明背景为止；到顶仍透明时垫页面底色（--ft-bg）。
#   玻璃元素（backdrop-filter）下面的真实背景取不到，沿 DOM 祖先链垫页面底色——与既有约定一致（玻璃之上的文字按「最暗的玻璃底 = 页面底色」核对）。
# - 渐变：每个色标都当作一种可能的底，取最不利的一个——偏保守（文字实际只压在渐变的一小块上），所以标记 gradient: true，
#   由人工 / 采样复核后再放行。
# - 占位文字（::placeholder）按输入框自身的背景核对。
CONTRAST_JS = r"""() => {
  %(label)s
  %(color)s
  %(visible)s
  const pageBg = parseColor(getComputedStyle(document.documentElement).getPropertyValue('--ft-bg').trim()) || { r: 242, g: 242, b: 247, a: 1 };
  const splitTop = (text) => {  // 按顶层逗号切分（跳过括号里的逗号）
    const parts = []; let depth = 0, start = 0;
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (c === '(') depth++; else if (c === ')') depth--;
      else if (c === ',' && depth === 0) { parts.push(text.slice(start, i).trim()); start = i + 1; }
    }
    parts.push(text.slice(start).trim());
    return parts;
  };
  const stopsOf = (image) => {  // 一张渐变里的所有色标
    const colors = [];
    const re = /rgba?\([^)]*\)/g; let m;
    while ((m = re.exec(image))) { const c = parseColor(m[0]); if (c) colors.push(c); }
    return colors;
  };
  const chainOf = (el) => {
    const nodes = []; for (let a = el; a && a.nodeType === 1; a = a.parentElement) nodes.push(a);
    const styles = nodes.map((n) => getComputedStyle(n));
    const opacity = styles.map((s) => { const o = parseFloat(s.opacity); return Number.isFinite(o) ? o : 1; });
    const suffix = new Array(nodes.length).fill(1);
    for (let i = nodes.length - 1; i >= 0; i--) suffix[i] = opacity[i] * (i + 1 < nodes.length ? suffix[i + 1] : 1);
    const layers = []; let kind = 'flat'; let covered = false;
    for (let i = 0; i < nodes.length && !covered; i++) {
      const s = styles[i];
      const bf = s.backdropFilter || s.webkitBackdropFilter;
      if (bf && bf !== 'none') kind = kind === 'flat' ? 'glass' : kind;
      const image = s.backgroundImage;
      if (image && image !== 'none') {
        if (image.includes('url(')) { kind = 'image'; layers.push({ type: 'image' }); }
        else {
          const images = splitTop(image).map((one) => stopsOf(one).map((c) => ({ ...c, a: c.a * suffix[i] })));
          layers.push({ type: 'gradient', images });
          kind = 'gradient';
          const bottom = images[images.length - 1];
          if (bottom.length && bottom.every((c) => c.a >= 0.99)) covered = true;
        }
      }
      const bg = parseColor(s.backgroundColor);
      if (bg && bg.a > 0) {
        const c = { ...bg, a: bg.a * suffix[i] };
        layers.push({ type: 'color', color: c });
        if (c.a >= 0.99) covered = true;
      }
    }
    return { layers, kind, textOpacity: suffix[0] };
  };
  // 把各层从下往上合成，得到所有「可能的底色」
  const backdrops = (layers) => {
    let candidates = [{ ...pageBg, a: 1 }];
    for (let i = layers.length - 1; i >= 0; i--) {
      const layer = layers[i];
      if (layer.type === 'color') candidates = candidates.map((c) => over(layer.color, c));
      else if (layer.type === 'gradient') {
        for (let k = layer.images.length - 1; k >= 0; k--) {
          const stops = layer.images[k];
          if (!stops.length) continue;
          const next = [];
          for (const c of candidates) for (const stop of stops) next.push(stop.a <= 0 ? c : over(stop, c));
          candidates = next.length > 64 ? next.filter((_, n) => n %% Math.ceil(next.length / 64) === 0) : next;
        }
      }
    }
    return candidates;
  };
  const evaluate = (label, text, fgColor, kind, layers, textOpacity, size, weight, rect) => {
    const fg0 = { ...fgColor, a: fgColor.a * textOpacity };
    let worst = Infinity, worstBg = null;
    for (const bg of backdrops(layers)) {
      const fg = fg0.a >= 0.999 ? fg0 : over(fg0, bg);
      const r = ratio(fg, bg);
      if (r < worst) { worst = r; worstBg = bg; }
    }
    const large = size >= 24 || (size >= 18.66 && weight >= 700);
    return { label, text: text.slice(0, 24), ratio: Math.round(worst * 100) / 100, required: large ? 3 : 4.5, kind, size, weight,
      fg: `rgba(${Math.round(fg0.r)}, ${Math.round(fg0.g)}, ${Math.round(fg0.b)}, ${Math.round(fg0.a * 100) / 100})`,
      bg: worstBg ? `rgb(${Math.round(worstBg.r)}, ${Math.round(worstBg.g)}, ${Math.round(worstBg.b)})` : null,
      rect: [Math.round(rect.left), Math.round(rect.top), Math.round(rect.width), Math.round(rect.height)] };
  };
  const out = [];
  const seen = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    const text = node.nodeValue.replace(/\s+/g, ' ').trim();
    if (!text) continue;
    const el = node.parentElement;
    if (!el || el.closest('script, style, noscript, [aria-hidden="true"], [hidden], .ft-sr-only, [inert]')) continue;
    if (hidden(el)) continue;
    const range = document.createRange(); range.selectNodeContents(node);
    const r = range.getBoundingClientRect();
    if (!inViewport(r)) continue;
    const top = document.elementFromPoint(Math.min(vw - 1, Math.max(0, r.left + r.width / 2)), Math.min(vh - 1, Math.max(0, r.top + r.height / 2)));
    if (!top || !(top === el || el.contains(top) || top.contains(el))) continue;  // 被别的元素盖住
    if (el.closest(':disabled, [aria-disabled="true"], [data-disabled="true"]')) continue;
    const cs = getComputedStyle(el);
    if (cs.webkitTextFillColor === 'rgba(0, 0, 0, 0)' || cs.color === 'rgba(0, 0, 0, 0)') continue;
    const fg = parseColor(cs.color);
    if (!fg) continue;
    const { layers, kind, textOpacity } = chainOf(el);
    if (kind === 'image') continue;
    const key = labelOf(el) + '|' + text;
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(evaluate(labelOf(el), text, fg, kind, layers, textOpacity, parseFloat(cs.fontSize), parseInt(cs.fontWeight, 10) || 400, r));
  }
  for (const el of document.querySelectorAll('input[placeholder], textarea[placeholder]')) {
    if (el.value || hidden(el)) continue;
    const r = el.getBoundingClientRect();
    if (!inViewport(r)) continue;
    const cs = getComputedStyle(el, '::placeholder');
    const fg = parseColor(cs.color);
    if (!fg) continue;
    const { layers, kind, textOpacity } = chainOf(el);
    const key = labelOf(el) + '|placeholder';
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(evaluate(labelOf(el) + '::placeholder', el.getAttribute('placeholder'), fg, kind, layers, textOpacity, parseFloat(cs.fontSize), parseInt(cs.fontWeight, 10) || 400, r));
  }
  return out;
}""" % {"label": _LABEL_JS, "color": _COLOR_JS, "visible": _VISIBLE_JS}

# 布局体检：整页横向溢出、越出视口左右边的元素、被固定高度裁掉的文字。
# - 横向滚动容器（overflow-x: auto / scroll）里的内容不算越界；单行省略（text-overflow: ellipsis）与行数截断（line-clamp）是有意的截断，不算裁字。
# - 页面栈根节点（.m-page-stack）在二级页压入时整体左移 40px（Masa 的「底层页面滑动」视差，被二级页盖住），是转场位移，不是内容越界。
# - 裁字看文字本身：容器是 overflow: hidden / clip 且内容高过容器时，逐个文字节点量它的行盒——行盒上沿 / 下沿越出容器超过 0.25em 才算被裁
#   （行高小于字体自然高度时，行盒外沿的几个像素只是留白，裁掉不伤字形；装饰性的绝对定位圆球撑高的 scrollHeight 也不是文字被裁）。
#   文字自己的祖先里有滚动容器 / 绝对定位 / 行数截断的，不归这个容器管。
OVERFLOW_JS = r"""() => {
  %(label)s
  %(visible)s
  const doc = document.documentElement;
  const out = { overflowX: doc.scrollWidth - doc.clientWidth, outside: [], clipped: [] };
  const inHScroller = (el) => {
    for (let a = el.parentElement; a && a !== document.body; a = a.parentElement) {
      const s = getComputedStyle(a);
      if (['auto', 'scroll'].includes(s.overflowX) && a.scrollWidth > a.clientWidth + 1) return true;
    }
    return false;
  };
  const cutText = (box) => {
    const clip = box.getBoundingClientRect();
    const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const content = n.nodeValue.trim();
      const host = n.parentElement;
      if (!content || !host || hidden(host) || host.closest('.ft-sr-only, [aria-hidden="true"], svg')) continue;
      let owned = true;
      for (let a = host; a && a !== box; a = a.parentElement) {
        const s = getComputedStyle(a);
        if (['auto', 'scroll'].includes(s.overflowY) || s.position === 'absolute' || s.position === 'fixed' || (s.webkitLineClamp && s.webkitLineClamp !== 'none')) { owned = false; break; }
      }
      if (!owned) continue;
      const tolerance = Math.max(2, parseFloat(getComputedStyle(host).fontSize) * 0.25);
      const range = document.createRange();
      range.selectNodeContents(n);
      for (const rect of range.getClientRects()) {
        if (rect.height < 1) continue;
        if (rect.bottom > clip.bottom + tolerance || rect.top < clip.top - tolerance) return content.slice(0, 16);
      }
    }
    return null;
  };
  for (const el of document.querySelectorAll('body *')) {
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden' || cs.position === 'fixed' && el.getBoundingClientRect().width === 0) continue;
    const r = el.getBoundingClientRect();
    if (!inViewport(r) || hidden(el)) continue;
    if (el.closest('svg, canvas, .ft-sr-only, [aria-hidden="true"]') && !el.matches('svg, canvas')) continue;
    if (r.right > vw + 1 || r.left < -1) {
      if (!el.matches('.m-page-stack') && !inHScroller(el)) out.outside.push({ label: labelOf(el), left: Math.round(r.left), right: Math.round(r.right) });
    }
    const clipsY = ['hidden', 'clip'].includes(cs.overflowY);
    if (clipsY && el.scrollHeight > el.clientHeight + 2 && el.clientHeight > 0 && !(cs.webkitLineClamp && cs.webkitLineClamp !== 'none')) {
      const text = cutText(el);
      if (text) out.clipped.push({ label: labelOf(el), scrollH: el.scrollHeight, clientH: el.clientHeight, text });
    }
  }
  return out;
}""" % {"label": _LABEL_JS, "visible": _VISIBLE_JS}

# 模拟系统字号放大 factor 倍：排版令牌 + 样式表里的 px 字号 / 行高 + 根字号（rem）
TEXT_ZOOM_JS = r"""(factor) => {
  const scale = (value) => value.replace(/(-?\d*\.?\d+)px/g, (m, n) => `${Math.round(parseFloat(n) * factor * 1000) / 1000}px`);
  const root = document.documentElement;
  const computed = getComputedStyle(root);
  let tokens = 0, rules = 0;
  const walk = (list) => {
    for (const rule of list) {
      if (rule.cssRules && rule.constructor.name !== 'CSSKeyframesRule') walk(rule.cssRules);
      if (!rule.style) continue;
      if (rule.selectorText === ':root') {
        for (const name of rule.style) {
          if (name.startsWith('--ft-type-') && !root.style.getPropertyValue(name)) {
            root.style.setProperty(name, scale(computed.getPropertyValue(name).trim()));
            tokens++;
          }
        }
      }
      for (const prop of ['font-size', 'line-height', 'font']) {
        const value = rule.style.getPropertyValue(prop);
        if (!value) continue;
        if (value.includes('var(')) {
          // 字号写成 min() / calc() 套 var()（例如 Hero 金额按版心收缩）的：整体乘 factor——WebView 的文字缩放就是对最终字号再乘一次。
          // 行高与 font 简写里的 var() 引用的是 --ft-type-* 令牌，上面已经放大过
          if (prop === 'font-size') { rule.style.setProperty(prop, `calc((${value}) * ${factor})`, rule.style.getPropertyPriority(prop)); rules++; }
        } else if (value.includes('px')) {
          rule.style.setProperty(prop, scale(value), rule.style.getPropertyPriority(prop)); rules++;
        }
      }
    }
  };
  for (const sheet of document.styleSheets) {
    let list; try { list = sheet.cssRules; } catch { continue; }  // 跨域样式表（CDN 的图标字体）读不到
    walk(list);
  }
  const base = parseFloat(computed.fontSize) || 16;
  root.style.fontSize = `${base * factor}px`;
  window.__ftTextZoom = factor;
  return { tokens, rules };
}"""

# 当前最上层（升起的 Sheet 优先，其次压栈的二级页面，最后是可见的 Tab 根页面）里最大的纵向滚动容器；缓存到 window.__a11yScroller
SCROLLER_JS = r"""() => {
  const shown = (el) => getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().height > 0;
  const sheets = [...document.querySelectorAll('.ft-sheet')].filter(shown);
  const stackItems = [...document.querySelectorAll('.m-page-stack-item')].filter((el) => getComputedStyle(el).display !== 'none');
  const layer = sheets.length ? sheets[sheets.length - 1] : stackItems.length ? stackItems[stackItems.length - 1] : document.querySelector('.p-page-container__item:not(.d-none)');
  window.__a11yScroller = null;
  if (!layer) return null;
  let best = null, bestArea = 0;
  for (const el of [layer, ...layer.querySelectorAll('*')]) {
    const s = getComputedStyle(el);
    if (!['auto', 'scroll'].includes(s.overflowY) || el.scrollHeight <= el.clientHeight + 4) continue;
    const area = el.clientHeight * el.clientWidth;
    if (area > bestArea) { best = el; bestArea = area; }
  }
  window.__a11yScroller = best;
  return best ? { max: best.scrollHeight - best.clientHeight, client: best.clientHeight, top: best.scrollTop } : null;
}"""

PROBES: "dict[str, str]" = {
    "glass": GLASS_JS,
    "materials": MATERIALS_JS,
    "hits": HITS_JS,
    "contrast": CONTRAST_JS,
    "overflow": OVERFLOW_JS,
}

#: 动效体检放行的选择器：加载转圈与文字光标闪烁是状态指示，不是装饰性运动。
#: 转圈在减弱动态效果下已经放慢到 2.4 s 一圈、而不是去掉——它是「正在刷新 / 同步」的唯一视觉反馈（各页 reduce 规则里有注释）
MOTION_ALLOW = [
    ".m-progress-circular",
    ".ft-spinner",
    ".ft-spin",
    ".ft-ptr__spinner",
    ".ft-import__sync--busy .ft-tile > i",
    ".ft-amount__caret",
    ".ft-caret",
    "[aria-busy='true']",
]

# ---------------------------------------------------------------------------------------------- Python 侧驱动


def settle(page: Page, *, frames: int = 2) -> None:
    """等几帧：滚动 / 样式改写之后让布局与 IntersectionObserver 之类的回调落定。"""
    page.evaluate(
        "(n) => new Promise((resolve) => { const tick = (i) => i <= 0 ? resolve() : requestAnimationFrame(() => tick(i - 1)); tick(n); })",
        frames,
    )


def wait_page_ready(page: Page, *, settle_ms: int = 350, timeout: int = 20000) -> None:
    """页面读完数据：没有骨架屏 / 忙碌标记 / 转圈，整页高度连续 settle_ms 不再变化，有限时长的动画都结束。"""
    page.evaluate("() => { delete window.__a11yHeight; }")
    page.wait_for_function(
        """(settle) => {
            if (document.querySelector('.ft-skeleton, [aria-busy="true"], .m-progress-circular, .m-progress-linear')) return false;
            const animating = document.getAnimations().some((a) => {
                const t = a.effect && a.effect.getComputedTiming ? a.effect.getComputedTiming() : null;
                return t && t.iterations !== Infinity && a.playState === 'running';
            });
            if (animating) return false;
            const height = document.documentElement.scrollHeight + '|' + document.body.scrollHeight;
            const now = performance.now();
            const last = window.__a11yHeight;
            if (!last || last.height !== height) { window.__a11yHeight = { height, since: now }; return false; }
            return now - last.since >= settle;
        }""",
        arg=settle_ms,
        polling=100,
        timeout=timeout,
    )


def scroll_positions(page: Page, *, limit: int = 8) -> "list[Optional[int]]":
    """当前最上层页面纵向滚动容器的采样位置（每次移动约 0.75 屏）；页面不滚动时只有 [None]。"""
    info = page.evaluate(SCROLLER_JS)
    if not info or info["max"] <= 4:
        return [None]
    step = max(120, int(info["client"] * 0.75))
    positions = list(range(0, int(info["max"]) + 1, step))
    if positions[-1] != int(info["max"]):
        positions.append(int(info["max"]))
    if len(positions) > limit:  # 太长的页面等距抽 limit 个，首尾必取
        stride = (len(positions) - 1) / (limit - 1)
        positions = [positions[round(i * stride)] for i in range(limit)]
    return positions


_ANIMATIONS_IDLE_JS = """() => !document.getAnimations().some((a) => {
    const t = a.effect && a.effect.getComputedTiming ? a.effect.getComputedTiming() : null;
    return t && t.iterations !== Infinity && a.playState === 'running';
})"""


def settle_animations(page: Page, *, timeout: int = 3000) -> None:
    """等所有有限时长的动画 / 过渡跑完（无限循环的转圈、光标闪烁不算）：滚动后 Tab 栏收缩 / 展开、大标题折叠都有 ≥ 180 ms 的过渡，
    不等它们跑完就取数，量到的是淡入淡出到一半的颜色。超时不抛异常（页面里有永不结束的有限动画时按现状取数）。"""
    try:
        page.wait_for_function(_ANIMATIONS_IDLE_JS, timeout=timeout, polling=50)
    except PlaywrightTimeoutError:
        pass


def scroll_to(page: Page, top: Optional[int]) -> None:
    if top is None:
        return
    page.evaluate("(top) => { if (window.__a11yScroller) window.__a11yScroller.scrollTop = top; }", top)
    settle(page, frames=3)
    settle_animations(page)


def scan(page: Page, probes: "Iterable[str]", *, scroll: bool = True, refine: bool = True) -> "dict[str, list]":
    """对当前屏幕跑一组探针；scroll=True 时沿滚动容器多个位置各跑一遍并合并（按条目内容去重），最后滚回顶部。

    glass / materials / overflow 这类「整屏状态」探针在每个位置都跑，返回的是各位置结果的并集（glass 取层数最多的那一屏，见 glass_peak）。
    refine=True 时 contrast 探针里解析法判为不达标的文字立即用像素复核（见 refine_contrast）——必须在同一个滚动位置做，
    所以放在扫描循环里而不是事后。
    """
    probes = list(probes)
    results: "dict[str, list]" = {name: [] for name in probes}
    positions = scroll_positions(page) if scroll else [None]
    for top in positions:
        scroll_to(page, top)
        for name in probes:
            data = page.evaluate(PROBES[name])
            if name == "glass":
                results[name].append(data)  # 每个位置一个列表，调用方取最大
            elif name in ("materials", "overflow"):
                results[name].append(data)
            else:
                if name == "contrast" and refine:
                    data = refine_contrast(page, data)
                results[name].extend(data)
    if positions != [None]:
        scroll_to(page, 0)
    return results


# ---------------------------------------------------------------------------------------------- 像素复核

#: 把页面上所有文字（含图标字体、占位文字）变透明、冻结过渡与动画：截图只剩背景，用来量文字真正压在什么颜色上
HIDE_TEXT_CSS = """
*, *::before, *::after {
  color: transparent !important;
  -webkit-text-fill-color: transparent !important;
  text-shadow: none !important;
  caret-color: transparent !important;
  transition: none !important;
  animation-play-state: paused !important;
}
::placeholder { color: transparent !important; -webkit-text-fill-color: transparent !important; }
"""


def _parse_rgba(text: str) -> "tuple[float, float, float, float]":
    nums = [float(n) for n in re.findall(r"[\d.]+", text)]
    r, g, b = nums[0], nums[1], nums[2]
    a = nums[3] if len(nums) > 3 else 1.0
    return r, g, b, a


def _luminance(rgb):
    """rgb 是 0–255 的 numpy 数组（最后一维是通道）。"""
    c = rgb / 255.0
    c = np.where(c <= 0.03928, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return 0.2126 * c[..., 0] + 0.7152 * c[..., 1] + 0.0722 * c[..., 2]


def pixel_contrast(png: bytes, fg: str) -> "tuple[float, str]":
    """文字色 fg（'rgba(r, g, b, a)'，半透明时按像素合成）相对截图每个像素的最低对比度，及最不利像素的颜色。"""
    pixels = np.asarray(Image.open(io.BytesIO(png)).convert("RGB"), dtype=np.float64)
    r, g, b, a = _parse_rgba(fg)
    color = np.array([r, g, b], dtype=np.float64)
    text = color * a + pixels * (1 - a)
    lt, lb = _luminance(text), _luminance(pixels)
    ratios = (np.maximum(lt, lb) + 0.05) / (np.minimum(lt, lb) + 0.05)
    flat = int(np.argmin(ratios))
    worst = pixels.reshape(-1, 3)[flat]
    return float(ratios.reshape(-1)[flat]), f"rgb({round(worst[0])}, {round(worst[1])}, {round(worst[2])})"


def refine_contrast(page: Page, items: "list[dict]") -> "list[dict]":
    """用截图的真实像素复核解析法判为不达标的文字，返回更新后的条目。

    解析法只会看到「祖先元素的背景」，看不到兄弟元素垫在下面的背景（分段控件选中项的白色滑块）、渐变上文字真正压着的那一小块、
    玻璃后面真实模糊出来的颜色，所以对这些情况偏保守。复核：隐藏全部文字后截取该文字的矩形区域，逐像素算对比度，取最低值——
    严格（最不利的那个像素），同时是「文字真正压着的底」，不是「整条渐变里最浅的一格」。
    复核后的条目 kind 标为 pixel，ratio / bg 换成实测值。
    """
    suspects = [c for c in items if c["ratio"] < c["required"] and c["kind"] != "image" and c["rect"][2] >= 1 and c["rect"][3] >= 1]
    if not suspects:
        return items
    page.evaluate(
        """(css) => { const s = document.createElement('style'); s.id = '__a11y_hide_text'; s.textContent = css; document.head.appendChild(s); }""",
        HIDE_TEXT_CSS,
    )
    try:
        settle(page, frames=3)
        for c in suspects:
            x, y, w, h = c["rect"]
            clip = {"x": max(0, x), "y": max(0, y), "width": max(1, w), "height": max(1, h)}
            try:
                png = page.screenshot(clip=clip, type="png")
                measured, worst = pixel_contrast(png, c["fg"])
            except Exception:  # noqa: BLE001 - 截图失败（元素刚好移出视口）时保留解析法的结论
                continue
            c["analytic"] = c["ratio"]
            c["ratio"] = round(measured, 2)
            c["bg"] = worst
            c["kind"] = "pixel"
    finally:
        page.evaluate("() => document.getElementById('__a11y_hide_text')?.remove()")
        settle(page, frames=2)
    return items


def glass_peak(per_position: "list[list[str]]") -> "list[str]":
    """各滚动位置里玻璃层最多的那一屏。"""
    return max(per_position, key=len) if per_position else []


def dedupe(items: "list[dict]", key: Callable[[dict], str]) -> "list[dict]":
    seen: "dict[str, dict]" = {}
    for item in items:
        seen.setdefault(key(item), item)
    return list(seen.values())


def hit_violations(hits: "list[dict]", *, minimum: float = 43.0, whitelist: "Iterable[str]" = ()) -> "list[dict]":
    """实际可点击区域宽或高不足 minimum（44 减 1 像素采样误差）的元素；whitelist 里的子串命中标识的跳过。

    同一个元素在多个滚动位置各量一次：贴着屏幕边缘（或被吸顶 / 吸底的栏盖住一部分）的那一次只量到露出的部分，
    所以每个元素取各次里最大的一次——滚到屏幕中间就能完整点到的元素不算不达标，在任何位置都点不全的才算。"""
    skip = list(whitelist)
    best: "dict[str, dict]" = {}
    for h in hits:
        current = best.get(h["label"])
        if current is None or min(h["w"], h["h"]) > min(current["w"], current["h"]):
            best[h["label"]] = h
    return [h for h in best.values() if (h["w"] < minimum or h["h"] < minimum) and not any(s in h["label"] for s in skip)]


def contrast_violations(items: "list[dict]", *, whitelist: "Iterable[str]" = ()) -> "list[dict]":
    """对比度低于要求（正文 4.5 / 大号文字 3）的文字。scan 已经把解析法不达标的条目用像素复核过（kind == 'pixel'），这里看的是复核后的值。"""
    skip = list(whitelist)
    bad = [c for c in items if c["ratio"] < c["required"] and not any(s in c["label"] or s in c["text"] for s in skip)]
    return dedupe(bad, lambda c: c["label"] + "|" + c["text"])


def pixel_checked(items: "list[dict]") -> "list[dict]":
    """被像素复核过的条目（解析法不达标、复核后可能达标也可能仍不达标）：用于盘点输出。"""
    return dedupe([c for c in items if c["kind"] == "pixel"], lambda c: c["label"] + "|" + c["text"])


def apply_text_zoom(page: Page, factor: float) -> dict:
    info = page.evaluate(TEXT_ZOOM_JS, factor)
    settle(page, frames=3)
    return info


def start_motion_recorder(page: Page, allow: "Optional[Iterable[str]]" = None) -> None:
    """记录之后发生的所有 CSS 动画 / 过渡（逐帧轮询 document.getAnimations()，一个动画只记一次）。

    allow 是一组 CSS 选择器（默认 MOTION_ALLOW）：目标元素自己或祖先命中的动画记为 allowed=true，long_motions 会放行——
    选择器要在页面里用 closest 判断，不能靠 className 子串（`.ft-spin` 这种带点的写法在子串里永远对不上）。"""
    selectors = ", ".join(MOTION_ALLOW if allow is None else list(allow))
    page.evaluate(
        """(selectors) => {
            if (window.__motionRecorder) { window.__motionRecorder.log.length = 0; return; }
            const log = [], seen = new WeakSet();
            const tick = () => {
                for (const a of document.getAnimations()) {
                    if (seen.has(a)) continue;
                    seen.add(a);
                    const t = a.effect && a.effect.getComputedTiming ? a.effect.getComputedTiming() : null;
                    const target = a.effect && a.effect.target;
                    log.push({
                        kind: a.transitionProperty ? 'transition' : 'animation',
                        name: a.transitionProperty || a.animationName || '?',
                        duration: t ? t.duration : 0,
                        iterations: t ? t.iterations : 1,
                        delay: t ? t.delay : 0,
                        allowed: !!(selectors && target && target.closest && target.closest(selectors)),
                        target: target ? (target.getAttribute && target.getAttribute('data-testid')) || target.className || target.tagName : '?',
                    });
                }
                window.__motionRecorder.raf = requestAnimationFrame(tick);
            };
            window.__motionRecorder = { log, raf: requestAnimationFrame(tick) };
        }""",
        selectors,
    )


def stop_motion_recorder(page: Page) -> "list[dict]":
    return page.evaluate(
        """() => {
            const rec = window.__motionRecorder;
            if (!rec) return [];
            cancelAnimationFrame(rec.raf);
            const log = rec.log.slice();
            delete window.__motionRecorder;
            return log;
        }"""
    )


def long_motions(log: "list[dict]", *, threshold_ms: float = 150, allow: "Iterable[str]" = ()) -> "list[dict]":
    """记录里时长（含延迟）超过阈值或无限循环的动画 / 过渡。

    放行两种：记录时已按选择器判为 allowed 的（见 start_motion_recorder，MOTION_ALLOW）；allow 里的子串命中目标标识 / 动画名的。"""
    skip = list(allow)
    out = []
    for entry in log:
        total = (entry["duration"] if isinstance(entry["duration"], (int, float)) else 0) + max(0, entry["delay"] or 0)
        infinite = entry["iterations"] in ("Infinity", float("inf")) or entry["iterations"] is None
        target = str(entry["target"])
        if entry.get("allowed"):
            continue
        if (total > threshold_ms or infinite) and not any(s in target or s in str(entry["name"]) for s in skip):
            out.append(entry)
    return out


def summarize(items: "list[dict]", fields: "Iterable[str]", limit: int = 12) -> str:
    """把一组问题条目压成一行日志（失败信息用）。"""
    fields = list(fields)
    rows = ["  ".join(f"{f}={item.get(f)}" for f in fields) for item in items[:limit]]
    more = f"\n  …另有 {len(items) - limit} 项" if len(items) > limit else ""
    return "\n    " + "\n    ".join(rows) + more if rows else ""


if __name__ == "__main__":  # pragma: no cover
    print(__doc__)
    sys.exit(0)
