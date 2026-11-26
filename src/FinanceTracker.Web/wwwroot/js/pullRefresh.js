// 下拉刷新（PullToRefresh.razor 通过 JS interop 调用）。
//
// 约定：下拉刷新「区」放在可滚动页面内容的最上方（首页放在 Hero 里面，列表页放在列表上方），
// 本模块在它最近的 [data-scroll-root] 祖先上监听触摸：
//   · 只在该滚动容器停在顶部（scrollTop ≤ 0）时才开始跟手；向上滑、横向滑、多指手势都交还给浏览器；
//   · 手指向下拉时，区的高度（--ft-ptr-offset）随「阻尼后的位移」增长，把下面的内容推下去；
//     区在 Hero 内部时 Hero 的渐变背景跟着延伸，顶部不会露出页面底色（状态栏背景判定也不受影响）；
//   · 松手时位移达到阈值就触发刷新：区保持 --ft-ptr-hold 的高度转圈，等 .NET 一侧的刷新完成再收起；没到阈值就直接回弹。
// 只刷新当前页数据——本模块不知道刷新的内容，只调用 .NET 的 OnPulled；是否联网由页面决定（首页 / 账单页都不触发云端同步）。
//
// 状态写在 Blazor 不渲染的属性 / 内联样式上（data-state、--ft-ptr-offset、--ft-ptr-progress），Blazor 重绘不会冲掉它们：
//   data-state = idle | pulling | ready（已过阈值，松手即刷新）| refreshing | settling（收起中）
//
// 动效参数（阈值、上限、保持高度、时长）取自 tokens.css 的 --ft-ptr-*；「减弱动态效果」下 CSS 不做高度过渡、不转圈。

const SCROLL_ROOT_SELECTOR = '[data-scroll-root]';
/** 开始判定为下拉之前的死区（像素）：轻微抖动不算下拉 */
const DEAD_ZONE = 6;
/** 横向位移超过纵向位移且大于该值，视为横向滑动（交还给浏览器 / 页面里的横向手势） */
const HORIZONTAL_SLOP = 8;
/** 刷新进行中，转圈至少显示这么久（毫秒）：本地数据一瞬间就加载完时，不让转圈一闪而过 */
const MIN_SPIN_MS = 500;
/** 阻尼：位移趋近上限的快慢，数值越大越「紧」 */
const DAMPING_RANGE = 1.2;

const NOOP_CONTROLLER = { dispose() {} };

function readTokens() {
    const style = getComputedStyle(document.documentElement);
    const read = (name, fallback) => {
        const value = parseFloat(style.getPropertyValue(name));
        return Number.isFinite(value) ? value : fallback;
    };

    return {
        threshold: read('--ft-ptr-threshold', 72),
        max: read('--ft-ptr-max', 132),
        hold: read('--ft-ptr-hold', 48),
        duration: read('--ft-dur-ptr', 280),
    };
}

/**
 * 接管一个下拉刷新区（.ft-ptr）。
 * @param zone 下拉刷新区元素
 * @param dotNetRef PullToRefresh 组件的 DotNetObjectReference（调用其 OnPulled，等它完成表示刷新结束）
 * @returns 控制器：dispose() 释放监听
 */
export function attach(zone, dotNetRef) {
    const root = zone?.closest(SCROLL_ROOT_SELECTOR);
    if (!root) return NOOP_CONTROLLER;

    const tokens = readTokens();
    const status = zone.querySelector('[data-ptr-status]');

    let state = 'idle';
    let tracking = false; // 一次触摸序列是否在跟踪
    let pulling = false; // 已确认是下拉手势（过了死区）
    let startX = 0;
    let startY = 0;
    let offset = 0;
    let settleTimer = 0;
    let disposed = false;

    function setState(next) {
        state = next;
        zone.dataset.state = next;
    }

    function announce(text) {
        if (status) status.textContent = text;
    }

    function setOffset(px) {
        offset = px;
        zone.style.setProperty('--ft-ptr-offset', `${px.toFixed(1)}px`);
        zone.style.setProperty('--ft-ptr-progress', Math.min(1, px / tokens.threshold).toFixed(3));
    }

    function clearOffset() {
        offset = 0;
        zone.style.removeProperty('--ft-ptr-offset');
        zone.style.removeProperty('--ft-ptr-progress');
    }

    /** 收起：高度过渡到 0，过渡结束（或兜底计时）后回到 idle。 */
    function settle() {
        window.clearTimeout(settleTimer);
        setState('settling');
        setOffset(0);
        settleTimer = window.setTimeout(() => {
            if (disposed) return;
            clearOffset();
            setState('idle');
        }, tokens.duration + 80);
    }

    function damp(distance) {
        return tokens.max * (1 - Math.exp(-distance / (tokens.max * DAMPING_RANGE)));
    }

    function abortTracking() {
        tracking = false;
        pulling = false;
    }

    function onTouchStart(event) {
        if (disposed || state !== 'idle') return;
        if (event.touches.length !== 1 || root.scrollTop > 0) return;
        startX = event.touches[0].clientX;
        startY = event.touches[0].clientY;
        tracking = true;
        pulling = false;
    }

    function onTouchMove(event) {
        if (!tracking) return;
        if (event.touches.length !== 1) {
            // 第二根手指落下：放弃本次下拉（可能是缩放 / 其它手势）
            const wasPulling = pulling;
            abortTracking();
            if (wasPulling) settle();
            return;
        }

        const dy = event.touches[0].clientY - startY;
        const dx = event.touches[0].clientX - startX;

        if (!pulling) {
            // 尚未确认是下拉：向上滑、横向为主、或容器已经不在顶部，都不是下拉，交还给浏览器
            if (dy < 0 || root.scrollTop > 0) {
                abortTracking();
                return;
            }
            if (Math.abs(dx) > Math.abs(dy) && Math.abs(dx) > HORIZONTAL_SLOP) {
                abortTracking();
                return;
            }
            if (dy < DEAD_ZONE) return;

            pulling = true;
            window.clearTimeout(settleTimer);
            setState('pulling');
        }

        if (dy <= 0) {
            // 拉下去之后又推回起点：取消本次下拉
            abortTracking();
            settle();
            return;
        }

        // 接管手势：阻止浏览器自带的回弹 / 原生下拉刷新，内容只跟着我们的位移走
        if (event.cancelable) event.preventDefault();

        setOffset(damp(dy - DEAD_ZONE));
        const next = offset >= tokens.threshold ? 'ready' : 'pulling';
        if (state !== next) setState(next);
    }

    function onTouchEnd() {
        if (!tracking) return;
        const wasPulling = pulling;
        abortTracking();
        if (!wasPulling) return;

        if (state === 'ready') {
            void refresh();
        } else {
            settle();
        }
    }

    /** 手势被系统打断（来电、手势导航等）：不触发刷新，直接回弹。 */
    function onTouchCancel() {
        if (!tracking) return;
        const wasPulling = pulling;
        abortTracking();
        if (wasPulling) settle();
    }

    async function refresh() {
        setState('refreshing');
        setOffset(tokens.hold);
        announce('正在刷新');

        const started = performance.now();
        try {
            // .NET 一侧执行页面的刷新并等它完成
            await dotNetRef.invokeMethodAsync('OnPulled');
        } catch {
            // 电路断开 / 组件已销毁：没有可通知的对象，直接收起
        }

        const rest = MIN_SPIN_MS - (performance.now() - started);
        if (rest > 0) await new Promise((resolve) => window.setTimeout(resolve, rest));
        if (disposed) return;

        announce('刷新完成');
        settle();
    }

    root.addEventListener('touchstart', onTouchStart, { passive: true });
    root.addEventListener('touchmove', onTouchMove, { passive: false });
    root.addEventListener('touchend', onTouchEnd, { passive: true });
    root.addEventListener('touchcancel', onTouchCancel, { passive: true });

    setState('idle');

    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            window.clearTimeout(settleTimer);
            root.removeEventListener('touchstart', onTouchStart);
            root.removeEventListener('touchmove', onTouchMove);
            root.removeEventListener('touchend', onTouchEnd);
            root.removeEventListener('touchcancel', onTouchCancel);
            clearOffset();
            zone.removeAttribute('data-state');
        },
    };
}
