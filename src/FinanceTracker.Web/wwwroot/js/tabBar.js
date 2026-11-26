// 悬浮玻璃 Tab 栏的脚本（GlassTabBar.razor 通过 JS interop 调用）。
//
// 页面栈里每个 Tab 根页面常驻 DOM（切走时仅加 d-none），当前显示的那个是 .p-page-container__item:not(.d-none)。
// 「回到顶部」约定：根页面把自己的滚动容器标记为 data-scroll-root（LargeTitlePage 与首页都带这个标记）；
// 找不到标记时退回到根页面容器本身。
//
// 本模块负责三件事：
//   1. 玻璃高光滑块：弹簧动力学（response / damping 取自设计令牌）驱动位置，水平拉伸随速度增加，按下「抬起」。
//   2. 滚动收缩：向下滚动超过阈值后 Tab 栏收缩成「当前页胶囊」，向上滚动或点按展开。
//   3. 回到顶部：再次点按当前 Tab。
//
// 点按时脚本在客户端立刻开始动画（乐观移动），不等 Blazor Server 往返；Blazor 随后通过 sync(index) 告知真实的激活序号——
// 序号与脚本目标一致就什么也不做，不一致（例如代码触发的页面跳转）才重新定向。
// 状态都写在 Blazor 不渲染的属性 / 内联样式上（data-collapsed、data-pressed、data-pressing、thumb 的 data-state 与内联 translate/scale），
// 所以 Blazor 重绘不会冲掉它们。
//
// 位置的唯一来源：脚本接管之后，高光的位置始终由内联 translate 决定（静止时保留 translate，只清掉 scale）；
// CSS 里由 --ft-tab-x 决定的静态位置只是脚本接管之前（预渲染）/ 脚本加载失败时的回退。
// 这样 Blazor 先于脚本更新 --ft-tab-x（代码触发的跳转：渲染先到、sync 后到）时，高光不会先瞬移到新位置再被拉回旧位置重新滑动。

const ACTIVE_ROOT_SELECTOR = '.p-page-container__item:not(.d-none)';
const SCROLL_ROOT_SELECTOR = '[data-scroll-root]';

// 数值积分的最大步长（秒）：弹簧在 60Hz 一帧里再细分，避免低帧率下数值发散
const MAX_STEP = 1 / 240;
// 按下抬起的平滑时间常数（秒）：约 3τ ≈ 120ms 到位（ease-out 的效果）
const LIFT_TAU = 0.04;
// 位置 / 速度 / 抬起量低于这些值就视为到位
const SETTLE_POSITION = 0.0005;
const SETTLE_SPEED = 0.01;
const SETTLE_LIFT = 0.0005;
// 滚动方向判定：同一方向累计滚动超过这个距离才算「向下 / 向上滚动」，过滤抖动
const SCROLL_RUN_MIN = 6;
// 点按后多久还没等到 Blazor 确认，就当跳转没有发生，高光回到 Blazor 报告的真实 Tab
const OPTIMISTIC_TIMEOUT_MS = 2500;

function prefersReducedMotion() {
    return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** 找到当前可见 Tab 根页面的滚动容器；找不到返回 null。 */
export function findActiveScrollRoot() {
    const root = document.querySelector(ACTIVE_ROOT_SELECTOR);
    if (!root) return null;
    return root.querySelector(SCROLL_ROOT_SELECTOR) ?? root;
}

/** 把当前 Tab 根页面滚回顶部；返回是否找到了可滚动的容器。「减弱动态效果」下不做平滑滚动。 */
export function scrollActiveRootToTop() {
    const target = findActiveScrollRoot();
    if (!target) return false;
    target.scrollTo({ top: 0, behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
    return true;
}

function readNumberToken(name, fallback) {
    const value = parseFloat(getComputedStyle(document.documentElement).getPropertyValue(name));
    return Number.isFinite(value) ? value : fallback;
}

/** 动效参数：全部取自 tokens.css，读不到时用设计稿的默认值。 */
function readMotionTokens() {
    return {
        response: readNumberToken('--ft-spring-response', 0.42),
        damping: readNumberToken('--ft-spring-damping', 0.62),
        stretchMax: readNumberToken('--ft-stretch-max', 0.7),
        squashMax: readNumberToken('--ft-squash-max', 0.07),
        liftScale: readNumberToken('--ft-lift-scale', 1.08),
        collapseThreshold: readNumberToken('--ft-collapse-threshold', 24),
        reducedFadeMs: readNumberToken('--ft-dur-reduced', 100),
    };
}

/**
 * 单位阶跃（0 → 1）下弹簧的峰值速度（Tab/秒）。拉伸倍数 = 1 + stretchMax × 当前速度 / 这次移动的峰值速度，
 * 所以无论移动一格还是三格，最大拉伸都是 1.7×；用同一套积分数值求得，参数改了不用手算。
 */
function unitStepPeakSpeed(omega, zeta) {
    let x = 0;
    let v = 0;
    let peak = 0;
    const dt = 1 / 1000;
    for (let t = 0; t < 1.5; t += dt) {
        v += (-omega * omega * (x - 1) - 2 * zeta * omega * v) * dt;
        x += v * dt;
        peak = Math.max(peak, Math.abs(v));
    }
    return peak;
}

const NOOP_CONTROLLER = { sync() {}, dispose() {} };

/**
 * 接管一个 Tab 栏（.ft-tabwrap）的滑块动效、按下反馈与滚动收缩。
 * @param wrap  Tab 栏容器元素
 * @param activeIndex 初始激活序号（不播放动画）
 * @returns 控制器：sync(index) 告知 Blazor 的激活序号；dispose() 释放监听
 */
export function attachTabBar(wrap, activeIndex) {
    const nav = wrap?.querySelector('[role="tablist"]');
    const thumb = wrap?.querySelector('[data-testid="tab-bar-thumb"]');
    if (!nav || !thumb) return NOOP_CONTROLLER;

    const capsule = wrap.querySelector('.ft-capsule');
    const tokens = readMotionTokens();
    const omega = (2 * Math.PI) / tokens.response;
    const zeta = tokens.damping;
    const unitPeak = unitStepPeakSpeed(omega, zeta);

    let x = activeIndex; // 高光当前位置，单位：Tab 宽度（0 = 第一个 Tab）
    let v = 0; // 速度（Tab/秒）
    let target = activeIndex; // 弹簧目标
    let synced = activeIndex; // Blazor 最近一次告知的激活序号
    let peakRef = unitPeak; // 这次移动的参考峰值速度
    let lift = 1; // 高光当前的「抬起」倍数
    let liftTarget = 1;
    let raf = 0;
    let lastTime = 0;
    let disposed = false;
    // 最近一次点按的乐观目标，等 Blazor 确认（sync 到同一序号）；连续点按时更早的迟到确认要被忽略，以最后一次点按为准
    let optimistic = null;
    let optimisticTimer = 0;

    const tabs = () => Array.from(nav.querySelectorAll('[role="tab"]'));

    // ---------- 高光滑块 ----------

    function setState(state) {
        if (thumb.dataset.state !== state) thumb.dataset.state = state;
    }

    /** 把当前位置、拉伸与抬起写成内联 translate / scale。 */
    function render() {
        const ratio = peakRef > 0 ? Math.min(1, Math.abs(v) / peakRef) : 0;
        const stretch = ratio * tokens.stretchMax; // 水平 0 ~ +0.7
        const squash = ratio * tokens.squashMax; // 垂直 0 ~ -0.07
        thumb.style.translate = `calc(4px + ${(x * 100).toFixed(2)}%) 0`;
        thumb.style.scale = `${((1 + stretch) * lift).toFixed(4)} ${((1 - squash) * lift).toFixed(4)}`;
    }

    /** 静止、没有按住时清掉内联 scale（没有拉伸也没有抬起）；translate 保留，位置始终由它决定。 */
    function clearRestScale() {
        if (raf || lift !== 1 || liftTarget !== 1) return;
        thumb.style.removeProperty('scale');
    }

    function frame(now) {
        raf = 0;
        if (disposed) return;
        // 限制单帧时长：标签页从后台回来时不会一步飞出去
        const dt = lastTime ? Math.min((now - lastTime) / 1000, 1 / 20) : 0;
        lastTime = now;

        if (dt > 0) {
            const steps = Math.max(1, Math.ceil(dt / MAX_STEP));
            const h = dt / steps;
            for (let i = 0; i < steps; i++) {
                v += (-omega * omega * (x - target) - 2 * zeta * omega * v) * h;
                x += v * h;
            }
            lift += (liftTarget - lift) * (1 - Math.exp(-dt / LIFT_TAU));
        }

        const positionSettled = Math.abs(x - target) < SETTLE_POSITION && Math.abs(v) < SETTLE_SPEED;
        if (positionSettled) {
            x = target;
            v = 0;
        }
        if (Math.abs(lift - liftTarget) < SETTLE_LIFT) lift = liftTarget;

        render();
        setState(positionSettled ? 'idle' : 'moving');

        if (positionSettled && lift === liftTarget) {
            thumb.style.willChange = '';
            clearRestScale();
            return;
        }
        raf = requestAnimationFrame(frame);
    }

    function startLoop() {
        if (raf || disposed) return;
        lastTime = 0;
        thumb.style.willChange = 'translate, scale';
        raf = requestAnimationFrame(frame);
    }

    function stopLoop() {
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        thumb.style.willChange = '';
    }

    function moveTo(index) {
        if (disposed) return;
        target = index;
        expand(); // 切换 Tab 时 Tab 栏总是展开的

        if (prefersReducedMotion()) {
            // 减弱动态效果：没有弹簧、回弹与拉伸，高光直接就位并做约 100ms 的淡入
            stopLoop();
            x = target;
            v = 0;
            lift = 1;
            liftTarget = 1;
            render();
            setState('idle');
            thumb.animate({ opacity: [0, 1] }, { duration: tokens.reducedFadeMs, easing: 'linear' });
            clearRestScale();
            return;
        }

        if (Math.abs(x - target) < SETTLE_POSITION && !raf) {
            clearRestScale();
            return;
        }
        // 保留当前位置与速度（被打断时从当前状态继续），只换目标；拉伸的参考峰值按剩余距离重新计算
        peakRef = unitPeak * Math.max(Math.abs(target - x), 0.35);
        setState('moving');
        startLoop();
    }

    // ---------- 按下反馈 ----------

    function onPointerDown(event) {
        if (event.pointerType === 'mouse' && event.button !== 0) return;
        const button = event.target instanceof Element ? event.target.closest('button') : null;
        if (!button || !wrap.contains(button)) return;

        button.setAttribute('data-pressing', 'true');
        if (button.classList.contains('ft-tab')) {
            wrap.setAttribute('data-pressed', 'true');
            if (!prefersReducedMotion()) {
                liftTarget = tokens.liftScale; // 高光「抬起」：放大，投影加深（投影在 CSS）
                startLoop();
            }
        }
    }

    function clearPressed() {
        const pressed = wrap.querySelectorAll('[data-pressing]');
        pressed.forEach((el) => el.removeAttribute('data-pressing'));
        wrap.removeAttribute('data-pressed');
        if (liftTarget !== 1) {
            liftTarget = 1;
            startLoop();
        }
    }

    function onClick(event) {
        const source = event.target instanceof Element ? event.target : null;
        if (!source) return;

        if (source.closest('.ft-capsule')) {
            expand();
            return;
        }

        const tab = source.closest('[role="tab"]');
        if (!tab || !nav.contains(tab)) return;
        const index = tabs().indexOf(tab);
        if (index < 0 || index === target) return;

        // 乐观移动：不等 Blazor Server 往返，立刻开始动画。Blazor 的确认（sync）可能迟到，也可能被后面的点按取代；
        // 超过 OPTIMISTIC_TIMEOUT 仍没有确认（跳转没有发生），回到 Blazor 报告的真实序号
        optimistic = index;
        clearTimeout(optimisticTimer);
        optimisticTimer = setTimeout(() => {
            optimistic = null;
            if (!disposed && synced !== target) moveTo(synced);
        }, OPTIMISTIC_TIMEOUT_MS);
        moveTo(index);
    }

    // ---------- 滚动收缩 ----------

    const scrollTops = new WeakMap(); // 每个滚动容器上一次的 scrollTop（切走再切回时位置保留，不重置）
    let scrollDirection = 0;
    let scrollRun = 0;

    function setCollapsed(collapsed) {
        const next = collapsed ? 'true' : 'false';
        if (wrap.dataset.collapsed === next) return;
        wrap.dataset.collapsed = next;
        // 收缩时 Tab 不可聚焦 / 不可点，胶囊接管；展开时反之
        tabs().forEach((tab) => {
            tab.inert = collapsed;
        });
        if (capsule) capsule.inert = !collapsed;
    }

    function expand() {
        setCollapsed(false);
        scrollDirection = 0;
        scrollRun = 0;
    }

    function onScroll(event) {
        const scroller = event.target;
        if (!(scroller instanceof Element)) return;
        if (scroller !== findActiveScrollRoot()) return; // 只认当前根页面自己的滚动，忽略内部嵌套的滚动区域

        const top = scroller.scrollTop;
        const delta = top - (scrollTops.get(scroller) ?? 0); // 没见过的容器从 0 起算（脚本接管前它没滚动过）
        scrollTops.set(scroller, top);

        if (top <= tokens.collapseThreshold) {
            expand();
            return;
        }
        if (delta === 0) return;

        const direction = delta > 0 ? 1 : -1;
        if (direction !== scrollDirection) {
            scrollDirection = direction;
            scrollRun = 0;
        }
        scrollRun += Math.abs(delta);
        if (scrollRun < SCROLL_RUN_MIN) return;

        setCollapsed(direction > 0);
    }

    // ---------- 生命周期 ----------

    wrap.addEventListener('pointerdown', onPointerDown);
    wrap.addEventListener('click', onClick);
    window.addEventListener('pointerup', clearPressed, true);
    window.addEventListener('pointercancel', clearPressed, true);
    window.addEventListener('blur', clearPressed);
    document.addEventListener('scroll', onScroll, { capture: true, passive: true });

    wrap.dataset.collapsed = 'false';
    if (capsule) capsule.inert = true;
    setState('idle');
    render(); // 接管位置：与 CSS 静态位置完全相同，视觉上没有变化
    clearRestScale();

    return {
        /** Blazor 告知真实的激活序号（每次渲染后调用）。 */
        sync(index) {
            if (disposed) return;
            const changed = index !== synced;
            synced = index;
            if (changed) expand(); // 换了 Tab：展开，从新根页面的滚动位置重新判断方向

            if (optimistic !== null) {
                // 点按触发的切换：与最后一次点按一致才算确认；否则是更早那次点按的迟到确认，忽略，不让高光折回去
                if (index === optimistic) {
                    optimistic = null;
                    clearTimeout(optimisticTimer);
                }
            } else if (index !== target) {
                moveTo(index); // 非点按触发的切换（代码跳转）也要播放动画
            }
        },

        dispose() {
            if (disposed) return;
            disposed = true;
            clearTimeout(optimisticTimer);
            stopLoop();
            wrap.removeEventListener('pointerdown', onPointerDown);
            wrap.removeEventListener('click', onClick);
            window.removeEventListener('pointerup', clearPressed, true);
            window.removeEventListener('pointercancel', clearPressed, true);
            window.removeEventListener('blur', clearPressed);
            document.removeEventListener('scroll', onScroll, { capture: true });
            thumb.style.removeProperty('translate');
            thumb.style.removeProperty('scale');
            wrap.removeAttribute('data-pressed');
            tabs().forEach((tab) => {
                tab.inert = false;
            });
        },
    };
}
