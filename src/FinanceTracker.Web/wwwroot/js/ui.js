// 复用组件的行为层（iOS UI 重构 · 05 号）：左滑行手势、底部 Sheet 拖拽收起；大标题页的折叠状态（08 号）；搜索框的提交时机（10 号）；
// 记一笔（15 号）：长按动作（退格键长按清空）、横向分页的页码（分类网格）、弹出菜单定位、聚焦备注时让出自定义键盘、需要确认才能关闭的 Sheet；
// 预算页（18 号）：拦下 SVG 元素的过渡事件（进度环弧长的过渡，免得 MASA 的 Transition 脚本读 SVG 的 className 抛 TypeError）；
// 自定义报表（21 号）：把文本存成文件下载（导出 CSV）、系统分享面板 / 复制到剪贴板（分享摘要）；
// 无障碍与降级（23 号）：材质降级的根开关 setSolid、「有 Sheet 升起」的根属性（玻璃层预算）。
//
// 设计：全部走文档级事件委托——组件（Components/Ui/*.razor）只渲染标记，行为由这里统一接管，
// 全局只装一份监听（UiRuntime.razor 在应用根部调用 install），不产生「每个组件一次 JS 互操作」，
// 列表里再多的行、之后新增 / 重绘的行都自动生效，不需要逐个绑定、也不需要在组件销毁时逐个解绑。
//
// 与 Blazor 的分工：脚本只改「脚本自己拥有的」状态（data-open / data-dragging / data-dragged、内联 transform、
// 托盘的 inert），这些属性不在 Blazor 的渲染树里，Blazor 重绘不会把它们冲掉；
// 业务（点了删除按钮、点了遮罩）仍由 Blazor 的 @onclick 处理——Sheet 拖拽收起时脚本动画结束后「点击」遮罩，
// 由 Blazor 完成关闭与卸载，脚本不需要 DotNetObjectReference。

const SWIPE_LOCK_PX = 10; // 水平位移超过多少像素才判定为「在滑动」
const SWIPE_AXIS_RATIO = 1.2; // 并且水平位移须大于垂直位移的这个倍数，否则交给浏览器纵向滚动
const VELOCITY_WINDOW_MS = 100; // 取最近多久的采样估算松手速度
const SWIPE_PROJECT_MS = 120; // 松手时按当前速度再「滑行」多久来判断落点
const CLICK_SUPPRESS_MS = 350; // 滑动结束后吞掉紧随其后的 click，避免松手时误触单元格
const SHEET_DISMISS_RATIO = 0.33; // 向下拖过 Sheet 高度的这个比例就收起
const SHEET_DISMISS_VELOCITY = 0.6; // 或者松手速度超过这个值（px/ms）
const LARGE_TITLE_COLLAPSE_SLACK_PX = 2; // 大标题底边与导航栏底边的距离不超过这个值就算已折叠（容忍亚像素取整）
const SEARCH_DEBOUNCE_MS = 350; // 搜索框停止输入多久后自动提交（不逐字查库；输入法组词期间不提交）
const HOLD_CLICK_MS = 500; // 长按多久触发「长按动作」（记一笔键盘的退格键：长按清空金额）
const HOLD_MOVE_TOLERANCE_PX = 12; // 长按期间手指移动超过这个距离就不算长按
const POPOVER_MARGIN_PX = 12; // 弹出菜单与屏幕边缘的最小距离
const POPOVER_GAP_PX = 6; // 弹出菜单与触发行之间的间距
const TYPING_SCROLL_DELAYS_MS = [60, 360]; // 聚焦备注后把它滚进可视区的时机：布局稳定后一次、系统键盘动画结束后一次
const SHEET_OPEN_ATTR = 'data-ft-sheet-open'; // <html> 上「有 Sheet 升起」的标记（玻璃层预算）
const SOLID_ATTR = 'data-ft-solid'; // <html> 上「材质降级为不透明」的根开关（tokens.css 的覆盖块读它）

let runtime = null;

/**
 * 材质降级的根开关（23 号）：on=true 时 <html data-ft-solid="true">，tokens.css 把所有玻璃令牌换成不透明实色、关掉模糊；
 * false 撤掉标记。由 UiRuntime 按宿主的 IRenderingProfileService 调用（低端设备 / 系统「降低透明度」），页面脚本与 E2E 也可直接调用。
 * 与系统媒体特性 prefers-reduced-transparency 并存：两者任一成立就是不透明（取值相同）。
 */
export function setSolid(on) {
    const root = document.documentElement;
    if (on) {
        root.setAttribute(SOLID_ATTR, 'true');
    } else {
        root.removeAttribute(SOLID_ATTR);
    }
}

/** 安装行为层（幂等）。Blazor 电路接管后由 UiRuntime 组件调用。 */
export function install() {
    if (runtime) {
        return;
    }

    runtime = new UiRuntime(document);
    runtime.start();
}

/** 卸载行为层（电路销毁时）。 */
export function uninstall() {
    runtime?.stop();
    runtime = null;
}

/** 收起当前展开的左滑行（供页面在需要时调用）。 */
export function closeSwipeRows() {
    runtime?.closeOpenRow();
}

/**
 * 把 Sheet 内容区里的某一块滚到可视区顶部（筛选 Sheet 从条件条上的标签进入时，定位到对应分组）。
 * 只滚 Sheet 自己的内容区（.ft-sheet__body）：不用 scrollIntoView，那会顺带滚动页面栈里的其它滚动容器。
 * 内容没有超出内容区时无事发生（scrollTop 夹在 0）。
 */
export function scrollSheetTo(element) {
    const body = element?.closest('.ft-sheet__body');
    if (!body) {
        return;
    }

    // 弹出动画期间 Sheet 带着 translateY：元素与内容区同时位移，两者的相对距离不受影响
    const offset = element.getBoundingClientRect().top - body.getBoundingClientRect().top;
    body.scrollTop = Math.max(0, body.scrollTop + offset - 4);
}

/**
 * 横向分页容器（记一笔的分类网格，标记 data-pager）滚到第 page 页（从 0 起）。
 * smooth=false 时立即就位（切换账单类型后回到第一页）；翻页后的页码由滚动事件维护在 data-page 上。
 */
export function scrollPagerTo(pager, page, smooth) {
    if (!pager || pager.clientWidth <= 0) {
        return;
    }

    const target = Math.max(0, page) * pager.clientWidth;
    if (Math.abs(pager.scrollLeft - target) < 1) {
        return;
    }

    pager.scrollTo({ left: target, behavior: smooth ? 'smooth' : 'auto' });
    if (!smooth) {
        pager.dataset.page = String(Math.max(0, page));
    }
}

/**
 * 弹出菜单（PopoverMenu）定位：优先放在触发行下方、右缘对齐触发行（iOS 上下文菜单的位置）；下方放不下就放上方；
 * 两边都放不下就选空间大的一侧，并限制最大高度、菜单内部滚动。位置用 fixed 坐标写在菜单上，菜单因此不受 Sheet 内容区
 * 的滚动裁剪（Sheet 的 body 是 overflow: auto）。写完后标记 data-positioned，样式据此显示菜单（之前是 visibility: hidden，
 * 避免在原点闪一下）。
 */
export function positionPopover(menu, anchor) {
    if (!menu || !anchor) {
        return;
    }

    const viewport = window.visualViewport;
    const viewWidth = viewport ? viewport.width : document.documentElement.clientWidth;
    const viewHeight = viewport ? viewport.height : window.innerHeight;
    const rect = anchor.getBoundingClientRect();

    menu.style.maxHeight = '';
    const width = menu.offsetWidth;
    const naturalHeight = menu.offsetHeight;

    const below = viewHeight - POPOVER_MARGIN_PX - (rect.bottom + POPOVER_GAP_PX);
    const above = rect.top - POPOVER_GAP_PX - POPOVER_MARGIN_PX;
    const placeBelow = naturalHeight <= below || below >= above;
    const room = Math.max(120, placeBelow ? below : above);
    const height = Math.min(naturalHeight, room);

    const top = placeBelow ? rect.bottom + POPOVER_GAP_PX : rect.top - POPOVER_GAP_PX - height;
    // 右缘对齐触发行（留出行的内边距），再夹进屏幕
    const left = Math.min(
        Math.max(POPOVER_MARGIN_PX, rect.right - POPOVER_MARGIN_PX - width),
        Math.max(POPOVER_MARGIN_PX, viewWidth - POPOVER_MARGIN_PX - width),
    );

    menu.style.top = `${Math.max(POPOVER_MARGIN_PX, top)}px`;
    menu.style.left = `${left}px`;
    menu.style.maxHeight = naturalHeight > room ? `${room}px` : '';
    menu.style.transformOrigin = `${Math.max(0, rect.right - POPOVER_MARGIN_PX - left)}px ${placeBelow ? '0' : '100%'}`;
    menu.dataset.placement = placeBelow ? 'below' : 'above';
    menu.dataset.positioned = 'true';
}

/**
 * 把一段文本存成文件让用户下载（自定义报表的「导出 CSV」）。文件开头加 UTF-8 BOM——Excel 打开没有 BOM 的中文 CSV 会乱码。
 * 用 Blob + 隐藏的 a[download] 触发：不经过 eval，也没有 data URI 的长度上限与转义问题；对象 URL 稍后释放（下载由浏览器异步接管）。
 */
export function downloadText(fileName, text, mimeType) {
    const blob = new Blob(['\uFEFF', text], { type: `${mimeType};charset=utf-8` });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

/**
 * 分享一段文字（自定义报表的「分享摘要」）：优先调系统分享面板（navigator.share）；没有它、或面板调不起来，就复制到剪贴板。
 * 返回结果由页面决定提示：'shared'（分享完成）｜'copied'（已复制到剪贴板）｜'cancelled'（用户关掉了分享面板——什么也不做）｜
 * 'unsupported'（两条路都走不通）。用户主动关掉分享面板不是失败，也不该退到剪贴板（会在他不想要的时候改掉他的剪贴板）。
 */
export async function shareText(title, text) {
    if (navigator.share) {
        try {
            await navigator.share({ title, text });
            return 'shared';
        } catch (error) {
            if (error && error.name === 'AbortError') {
                return 'cancelled';
            }
        }
    }

    if (navigator.clipboard && navigator.clipboard.writeText) {
        try {
            await navigator.clipboard.writeText(text);
            return 'copied';
        } catch {
            // 没有剪贴板权限（非安全上下文、被拒绝）：落到下面的 unsupported
        }
    }

    return 'unsupported';
}

function velocity(samples, axis) {
    if (samples.length < 2) {
        return 0;
    }

    const last = samples[samples.length - 1];
    let first = samples[0];
    for (const sample of samples) {
        if (last.t - sample.t <= VELOCITY_WINDOW_MS) {
            first = sample;
            break;
        }
    }

    const dt = last.t - first.t;
    return dt > 0 ? (last[axis] - first[axis]) / dt : 0;
}

/** 事件目标是不是搜索框的输入框（SearchField 渲染时带 data-live-search 标记）。 */
function searchInput(target) {
    return target instanceof HTMLInputElement && target.hasAttribute('data-live-search') ? target : null;
}

/** 越界阻尼：向右拉 / 向左拉过头时只跟随一小部分，手感接近 iOS 的橡皮筋。 */
function rubber(raw, width) {
    if (raw > 0) {
        return Math.min(24, raw * 0.18);
    }

    if (raw < -width) {
        return -width + (raw + width) * 0.22;
    }

    return raw;
}

class UiRuntime {
    constructor(doc) {
        this.doc = doc;
        this.swipe = null; // 正在跟踪的左滑手势
        this.sheetDrag = null; // 正在跟踪的 Sheet 拖拽
        this.openRow = null; // 当前展开的行（同一时刻最多一行）
        this.suppressClickUntil = 0;
        this.searchTimer = null; // 搜索框「停顿后提交」的定时器（同一时刻只有一个输入框在输入）
        this.searchValues = new WeakMap(); // 搜索框 → 最近一次提交（或浏览器自己派发 change）时的内容，内容没变就不重复提交
        this.hold = null; // 正在计时的长按（data-hold-click）
        this.holdFired = null; // 刚触发过长按动作的元素：它随后那次松手产生的 click 要吞掉
        this.typingTimers = []; // 聚焦备注后「滚进可视区」的定时器
        this.sheetObserver = null; // 观察 Sheet 的出现 / 收起，维护 <html data-ft-sheet-open>

        this.onDown = (e) => this.handleDown(e);
        this.onMove = (e) => this.handleMove(e);
        this.onUp = (e) => this.handleUp(e, false);
        this.onCancel = (e) => this.handleUp(e, true);
        this.onClick = (e) => this.handleClick(e);
        this.onScroll = (e) => this.handleScroll(e);
        this.onInput = (e) => this.handleInput(e);
        this.onChange = (e) => this.handleChange(e);
        this.onCompositionEnd = (e) => this.handleCompositionEnd(e);
        this.onKeyDown = (e) => this.handleKeyDown(e);
        this.onFocusIn = (e) => this.handleFocusIn(e);
        this.onFocusOut = (e) => this.handleFocusOut(e);
        this.onSvgTransition = (e) => this.handleSvgTransition(e);
    }

    start() {
        const d = this.doc;
        // 过渡事件会冒泡：MASA Blazor 的 Transition 脚本在祖先上监听 transitionend / transitioncancel，并读 e.target.className.split(' ')；
        // SVG 元素的 className 是 SVGAnimatedString（不是字符串），会抛 TypeError（功能无影响，但是页面错误、污染控制台）。
        // 进度环的弧（SVG）有弧长过渡，所以在文档最外层（捕获阶段）把 SVG 自己的过渡事件拦下。
        d.addEventListener('transitionend', this.onSvgTransition, true);
        d.addEventListener('transitioncancel', this.onSvgTransition, true);
        d.addEventListener('pointerdown', this.onDown, true);
        d.addEventListener('pointermove', this.onMove, true);
        d.addEventListener('pointerup', this.onUp, true);
        d.addEventListener('pointercancel', this.onCancel, true);
        d.addEventListener('click', this.onClick, true);
        d.addEventListener('scroll', this.onScroll, { capture: true, passive: true });
        d.addEventListener('input', this.onInput, true);
        d.addEventListener('change', this.onChange, true);
        d.addEventListener('compositionend', this.onCompositionEnd, true);
        d.addEventListener('keydown', this.onKeyDown, true);
        d.addEventListener('focusin', this.onFocusIn, true);
        d.addEventListener('focusout', this.onFocusOut, true);

        // 「有 Sheet 升起」的根属性（玻璃层预算，见 ui.css）：Sheet 可能渲染在页面栈之外，也可能在某个页面里面，样式表达不了
        // 「页面里有 Sheet」，所以由这里观察 DOM 写在 <html> 上。只做一次 querySelector，开销与 DOM 变更的频率无关地小
        this.sheetObserver = new MutationObserver(() => this.syncSheetOpen());
        this.sheetObserver.observe(d.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
        this.syncSheetOpen();
    }

    /** 屏幕上是否有（没有开始收起的）Sheet：有就在 <html> 上写 data-ft-sheet-open，收起开始的瞬间撤掉。 */
    syncSheetOpen() {
        const root = this.doc.documentElement;
        const open = this.doc.querySelector('.ft-sheet-host:not(.ft-sheet-host--closing)') !== null;
        if (open && root.getAttribute(SHEET_OPEN_ATTR) !== 'true') {
            root.setAttribute(SHEET_OPEN_ATTR, 'true');
        } else if (!open && root.hasAttribute(SHEET_OPEN_ATTR)) {
            root.removeAttribute(SHEET_OPEN_ATTR);
        }
    }

    stop() {
        const d = this.doc;
        this.sheetObserver?.disconnect();
        this.sheetObserver = null;
        d.documentElement.removeAttribute(SHEET_OPEN_ATTR);
        d.removeEventListener('transitionend', this.onSvgTransition, true);
        d.removeEventListener('transitioncancel', this.onSvgTransition, true);
        d.removeEventListener('pointerdown', this.onDown, true);
        d.removeEventListener('pointermove', this.onMove, true);
        d.removeEventListener('pointerup', this.onUp, true);
        d.removeEventListener('pointercancel', this.onCancel, true);
        d.removeEventListener('click', this.onClick, true);
        d.removeEventListener('scroll', this.onScroll, { capture: true });
        d.removeEventListener('input', this.onInput, true);
        d.removeEventListener('change', this.onChange, true);
        d.removeEventListener('compositionend', this.onCompositionEnd, true);
        d.removeEventListener('keydown', this.onKeyDown, true);
        d.removeEventListener('focusin', this.onFocusIn, true);
        d.removeEventListener('focusout', this.onFocusOut, true);
        this.cancelSearch();
        this.cancelHold();
        this.clearTypingTimers();
        this.closeOpenRow();
        this.swipe = null;
        this.sheetDrag = null;
    }

    // ---------------------------------------------------------------- SVG 过渡事件

    /** SVG 元素自己的过渡（进度环的弧）没有人需要处理：拦在最外层，MASA 的 Transition 脚本就读不到它（见 start 里的说明）。 */
    handleSvgTransition(e) {
        if (e.target instanceof SVGElement) {
            e.stopPropagation();
        }
    }

    // ---------------------------------------------------------------- 指针

    handleDown(e) {
        if (e.pointerType === 'mouse' && e.button !== 0) {
            return;
        }

        const target = e.target instanceof Element ? e.target : null;
        if (!target) {
            return;
        }

        // 备注输入框正在输入时，按在别处（不是这一行）：先让输入框失焦，系统键盘收起、自定义键盘回来；这次点按照常生效
        const active = this.doc.activeElement;
        if (active instanceof HTMLElement && active.hasAttribute('data-keypad-hide') && !target.closest('[data-typing-row]')) {
            active.blur();
        }

        // 长按动作（记一笔键盘的退格键）：开始计时，到点触发另一个元素的点击
        const hold = target.closest('[data-hold-click]');
        this.holdFired = null;
        if (hold) {
            this.beginHold(e, hold);
        }

        // Sheet：按住把手 / 标题栏（标题栏里的按钮除外）向下拖拽收起
        const handle = target.closest('[data-sheet-handle]');
        if (handle) {
            this.beginSheetDrag(e, handle, target);
            return;
        }

        // 左滑行
        const content = target.closest('.ft-swipe__content');
        const row = content ? content.parentElement : null;

        // 按在别处：先收起已展开的行（不吞掉这次点按，让它照常生效）
        if (this.openRow && !this.openRow.contains(target)) {
            this.setOpen(this.openRow, false);
        }

        if (!content || !row || !row.classList.contains('ft-swipe') || row.dataset.disabled === 'true') {
            return;
        }

        const tray = row.querySelector(':scope > .ft-swipe__tray');
        const width = tray ? tray.getBoundingClientRect().width : 0;
        if (width <= 0) {
            return;
        }

        this.swipe = {
            row,
            content,
            width,
            pointerId: e.pointerId,
            startX: e.clientX,
            startY: e.clientY,
            baseOffset: row.dataset.open === 'true' ? -width : 0,
            offset: row.dataset.open === 'true' ? -width : 0,
            dragging: false,
            samples: [{ t: e.timeStamp, x: e.clientX, y: e.clientY }],
        };
    }

    handleMove(e) {
        const hold = this.hold;
        if (hold && e.pointerId === hold.pointerId
            && Math.hypot(e.clientX - hold.startX, e.clientY - hold.startY) > HOLD_MOVE_TOLERANCE_PX) {
            this.cancelHold();
        }

        const swipe = this.swipe;
        if (swipe && e.pointerId === swipe.pointerId) {
            this.moveSwipe(swipe, e);
        }

        const drag = this.sheetDrag;
        if (drag && e.pointerId === drag.pointerId) {
            this.moveSheet(drag, e);
        }
    }

    handleUp(e, cancelled) {
        if (this.hold && e.pointerId === this.hold.pointerId) {
            this.cancelHold();
        }

        const swipe = this.swipe;
        if (swipe && e.pointerId === swipe.pointerId) {
            this.swipe = null;
            if (swipe.dragging) {
                this.finishSwipe(swipe, cancelled);
            }
        }

        const drag = this.sheetDrag;
        if (drag && e.pointerId === drag.pointerId) {
            this.sheetDrag = null;
            if (drag.dragging) {
                this.finishSheet(drag, cancelled);
            }
        }
    }

    // ---------------------------------------------------------------- 左滑行

    moveSwipe(swipe, e) {
        const dx = e.clientX - swipe.startX;
        const dy = e.clientY - swipe.startY;

        if (!swipe.dragging) {
            if (Math.abs(dy) > SWIPE_LOCK_PX && Math.abs(dy) > Math.abs(dx)) {
                // 纵向滚动：放弃跟踪，浏览器会自己滚动
                this.swipe = null;
                return;
            }

            if (Math.abs(dx) > SWIPE_LOCK_PX && Math.abs(dx) > Math.abs(dy) * SWIPE_AXIS_RATIO) {
                swipe.dragging = true;
                swipe.startX = e.clientX; // 从锁定点开始跟手，内容不会瞬移一截
                try {
                    swipe.content.setPointerCapture(e.pointerId);
                } catch {
                    // 指针已不存在时忽略
                }

                swipe.row.dataset.dragging = 'true';
                if (this.openRow && this.openRow !== swipe.row) {
                    this.setOpen(this.openRow, false);
                }
            } else {
                return;
            }
        }

        swipe.offset = rubber(swipe.baseOffset + (e.clientX - swipe.startX), swipe.width);
        swipe.content.style.transform = `translateX(${swipe.offset}px)`;

        swipe.samples.push({ t: e.timeStamp, x: e.clientX, y: e.clientY });
        while (swipe.samples.length > 2 && e.timeStamp - swipe.samples[0].t > VELOCITY_WINDOW_MS * 3) {
            swipe.samples.shift();
        }
    }

    finishSwipe(swipe, cancelled) {
        try {
            swipe.content.releasePointerCapture(swipe.pointerId);
        } catch {
            // 已释放
        }

        swipe.row.removeAttribute('data-dragging');

        // 按松手速度再滑行一小段后的位置来判断落在「展开」还是「收起」
        const projected = cancelled
            ? swipe.offset
            : swipe.offset + velocity(swipe.samples, 'x') * SWIPE_PROJECT_MS;

        this.suppressClickUntil = performance.now() + CLICK_SUPPRESS_MS;
        this.setOpen(swipe.row, projected < -swipe.width / 2);
    }

    /** 展开 / 收起一行：状态落在 data-open 与托盘的 inert 上，位移交给 CSS 过渡动画。 */
    setOpen(row, open) {
        const content = row.querySelector(':scope > .ft-swipe__content');
        const tray = row.querySelector(':scope > .ft-swipe__tray');
        if (!content) {
            return;
        }

        const width = tray ? tray.getBoundingClientRect().width : 0;
        row.dataset.open = open ? 'true' : 'false';
        content.style.transform = open ? `translateX(${-width}px)` : '';

        if (tray) {
            tray.inert = !open;
            tray.setAttribute('aria-hidden', open ? 'false' : 'true');
        }

        if (open) {
            this.openRow = row;
        } else if (this.openRow === row) {
            this.openRow = null;
        }
    }

    closeOpenRow() {
        if (this.openRow) {
            this.setOpen(this.openRow, false);
        }
    }

    handleScroll(e) {
        // 列表滚动时收起展开的行（iOS 行为）；正在拖动的那一行不受影响
        if (this.openRow && !this.swipe?.dragging) {
            this.setOpen(this.openRow, false);
        }

        // 大标题页（LargeTitlePage）：滚动的就是页面自己的滚动容器时，更新大标题是否已折叠
        const scroller = e && e.target instanceof Element ? e.target : null;
        if (scroller && scroller.hasAttribute('data-large-title')) {
            this.updateLargeTitle(scroller);
        }

        // 横向分页容器（记一笔的分类网格）：把当前页码写在 data-page 上，分页点的高亮由样式据此决定
        if (scroller && scroller.hasAttribute('data-pager')) {
            this.updatePager(scroller);
        }
    }

    // ---------------------------------------------------------------- 横向分页

    /** 当前页 = 滚动位置 ÷ 容器宽度四舍五入；只在变化时写 data-page（脚本自有属性，Blazor 重绘不会冲掉）。 */
    updatePager(pager) {
        const width = pager.clientWidth;
        if (width <= 0) {
            return;
        }

        const page = String(Math.max(0, Math.round(pager.scrollLeft / width)));
        if (pager.dataset.page !== page) {
            pager.dataset.page = page;
        }
    }

    // ---------------------------------------------------------------- 长按动作

    /** 在带 data-hold-click 的元素上按下：计时，到点后点击它指向的元素（选择器在最近的 [data-hold-scope] 里找，没有就在整个文档里找）。 */
    beginHold(e, element) {
        this.cancelHold();
        this.hold = {
            element,
            pointerId: e.pointerId,
            startX: e.clientX,
            startY: e.clientY,
            timer: setTimeout(() => this.fireHold(), HOLD_CLICK_MS),
        };
    }

    cancelHold() {
        if (this.hold) {
            clearTimeout(this.hold.timer);
            this.hold = null;
        }
    }

    fireHold() {
        const hold = this.hold;
        this.hold = null;
        if (!hold || !hold.element.isConnected) {
            return;
        }

        const scope = hold.element.closest('[data-hold-scope]') ?? this.doc;
        const action = scope.querySelector(hold.element.getAttribute('data-hold-click'));
        if (!action) {
            return;
        }

        // 记下这个元素：松手时浏览器会再产生一次 click，那是「点按」而不是「长按」，要吞掉
        this.holdFired = hold.element;
        action.click();
    }

    // ---------------------------------------------------------------- 键盘 / 焦点

    /** 聚焦声明了 data-keypad-hide 的输入框（备注）：Sheet 进入「输入中」，自定义键盘让位（样式据 data-typing 隐藏它），并把这一行滚进可视区。 */
    handleFocusIn(e) {
        const input = e.target instanceof Element && e.target.hasAttribute('data-keypad-hide') ? e.target : null;
        const sheet = input?.closest('.ft-sheet');
        if (!input || !sheet) {
            return;
        }

        sheet.dataset.typing = 'true';
        this.clearTypingTimers();
        const row = input.closest('[data-typing-row]') ?? input;
        this.typingTimers = TYPING_SCROLL_DELAYS_MS.map((delay) => setTimeout(() => scrollSheetTo(row), delay));
    }

    handleFocusOut(e) {
        const input = e.target instanceof Element && e.target.hasAttribute('data-keypad-hide') ? e.target : null;
        const sheet = input?.closest('.ft-sheet');
        if (!input || !sheet) {
            return;
        }

        // 焦点只是移到了同一 Sheet 里另一个声明了 data-keypad-hide 的输入框时，下一次 focusin 会再置回 true
        delete sheet.dataset.typing;
        this.clearTypingTimers();
    }

    clearTypingTimers() {
        for (const timer of this.typingTimers) {
            clearTimeout(timer);
        }

        this.typingTimers = [];
    }

    // ---------------------------------------------------------------- 大标题折叠

    /**
     * 大标题页的折叠状态：大标题的底边滚到导航栏底边之上（被导航栏盖住）就是「已折叠」。
     * 状态写在滚动容器的 data-title-collapsed 上（"true" / "false"，只在变化时写），样式据此显示小标题、给导航栏铺上页面背景色；
     * 这个属性不在 Blazor 的渲染树里，重绘不会冲掉它。
     */
    updateLargeTitle(scroller) {
        const title = scroller.querySelector(':scope > .ft-large-title');
        const bar = scroller.querySelector(':scope > .ft-lnav');
        if (!title || !bar) {
            return;
        }

        const collapsed = title.getBoundingClientRect().bottom <= bar.getBoundingClientRect().bottom + LARGE_TITLE_COLLAPSE_SLACK_PX;
        const next = collapsed ? 'true' : 'false';
        if (scroller.dataset.titleCollapsed !== next) {
            scroller.dataset.titleCollapsed = next;
        }
    }

    handleClick(e) {
        const target = e.target instanceof Element ? e.target : null;
        if (!target) {
            return;
        }

        // 刚触发过长按动作的元素，松手产生的那次 click：吞掉（否则长按退格清空之后，松手又会多退一格）
        const held = target.closest('[data-hold-click]');
        if (held && held === this.holdFired) {
            this.holdFired = null;
            e.preventDefault();
            e.stopPropagation();
            return;
        }

        // 搜索框的清除按钮
        const clear = target.closest('[data-live-search-clear]');
        if (clear) {
            this.clearSearch(clear);
            return;
        }

        const content = target.closest('.ft-swipe__content');

        // 刚滑动完松手产生的那次 click：吞掉
        if (content && performance.now() < this.suppressClickUntil) {
            e.preventDefault();
            e.stopPropagation();
            return;
        }

        const open = this.openRow;
        if (!open) {
            return;
        }

        if (content && content.parentElement === open) {
            // 行已展开时点按内容：只负责收起，不触发单元格本身的点击
            e.preventDefault();
            e.stopPropagation();
            this.setOpen(open, false);
            return;
        }

        if (target.closest('.ft-swipe__tray') && open.contains(target)) {
            // 点了操作按钮：它自己的 click 照常处理，随后收起这一行
            setTimeout(() => this.setOpen(open, false), 0);
        }
    }

    // ---------------------------------------------------------------- 搜索框

    // 「提交」= 在输入框上派发一次 change：Blazor 的 @onchange 据此把内容交给页面。浏览器自己只在失焦（部分浏览器还有回车）时派发 change，
    // 光靠它，用户敲完字要点别处才会出结果；这里补上「停顿后」「回车 / 搜索键」「清除」三个时机，并避开输入法组词。

    /** 输入框内容变了：组词中（拼音还没选字）什么也不提交；否则停顿一会儿再提交，连续敲字不会逐字查库。 */
    handleInput(e) {
        const input = searchInput(e.target);
        if (!input) {
            return;
        }

        if (e.isComposing) {
            this.cancelSearch();
            return;
        }

        this.scheduleSearch(input);
    }

    /** 组词结束（选定了汉字）：和普通输入一样，停顿后提交。Safari 的 compositionend 在最后一次 input 之前触发，所以两处都要排。 */
    handleCompositionEnd(e) {
        const input = searchInput(e.target);
        if (input) {
            this.scheduleSearch(input);
        }
    }

    /** 记下输入框此刻已经提交的内容——无论是脚本派发的，还是浏览器在失焦 / 回车时自己派发的 change。 */
    handleChange(e) {
        const input = searchInput(e.target);
        if (input) {
            this.searchValues.set(input, input.value);
        }
    }

    /**
     * 回车（软键盘上的「搜索」「完成」键）：搜索框立即提交并收起软键盘；声明了 data-enter-blur 的输入框（记一笔的备注）只收起软键盘
     * （输入框失焦时浏览器会派发 change，Blazor 的 @onchange 据此拿到内容）。组词期间的回车是在确认候选字，不算。
     */
    handleKeyDown(e) {
        if (e.target instanceof HTMLInputElement && e.target.hasAttribute('data-enter-blur')) {
            if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) {
                e.preventDefault();
                e.target.blur();
            }

            return;
        }

        const input = searchInput(e.target);
        if (!input || e.key !== 'Enter' || e.isComposing || e.keyCode === 229) {
            return;
        }

        e.preventDefault();
        this.cancelSearch();
        this.commitSearch(input);
        input.blur();
    }

    /** 清除按钮：清空、保持焦点（软键盘不收起，可以接着输入新的关键字）、立即提交。 */
    clearSearch(button) {
        const input = button.closest('[data-live-search-field]')?.querySelector('[data-live-search]');
        if (!input) {
            return;
        }

        this.cancelSearch();
        input.value = '';
        input.focus();
        this.commitSearch(input);
    }

    scheduleSearch(input) {
        this.cancelSearch();
        this.searchTimer = setTimeout(() => {
            this.searchTimer = null;
            this.commitSearch(input);
        }, SEARCH_DEBOUNCE_MS);
    }

    cancelSearch() {
        if (this.searchTimer !== null) {
            clearTimeout(this.searchTimer);
            this.searchTimer = null;
        }
    }

    /** 提交输入框当前的内容；与上一次提交的相同（含「一直是空」）时什么也不做。 */
    commitSearch(input) {
        if (!input.isConnected || input.value === (this.searchValues.get(input) ?? '')) {
            return;
        }

        // 派发出去的 change 会先经过本类的 handleChange，由它更新「最近提交的内容」
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // ---------------------------------------------------------------- Sheet 拖拽收起

    beginSheetDrag(e, handle, target) {
        const sheet = handle.closest('.ft-sheet');
        if (!sheet || sheet.dataset.dismissible !== 'true') {
            return;
        }

        // 标题栏里的按钮（关闭 / 操作）就是普通点按，不发起拖拽
        if (target.closest('button, a, input, textarea, select')) {
            return;
        }

        const host = sheet.closest('.ft-sheet-host');
        if (!host) {
            return;
        }

        this.sheetDrag = {
            sheet,
            host,
            handle,
            pointerId: e.pointerId,
            startY: e.clientY,
            height: sheet.getBoundingClientRect().height,
            offset: 0,
            dragging: false,
            samples: [{ t: e.timeStamp, x: e.clientX, y: e.clientY }],
        };
    }

    moveSheet(drag, e) {
        const dy = e.clientY - drag.startY;

        if (!drag.dragging) {
            if (Math.abs(dy) < 4) {
                return;
            }

            drag.dragging = true;
            try {
                drag.handle.setPointerCapture(e.pointerId);
            } catch {
                // 指针已不存在时忽略
            }

            // 拖动期间位移由脚本全权决定：关掉入场动画（动画会盖过内联 transform）与过渡
            drag.sheet.style.animation = 'none';
            drag.sheet.style.transition = 'none';
        }

        drag.offset = dy >= 0 ? dy : dy * 0.12; // 向上拖有阻尼
        drag.sheet.style.transform = `translateY(${drag.offset}px)`;

        const progress = Math.max(0, 1 - Math.max(0, drag.offset) / drag.height);
        drag.host.style.setProperty('--ft-sheet-scrim-opacity', String(progress));

        drag.samples.push({ t: e.timeStamp, x: e.clientX, y: e.clientY });
        while (drag.samples.length > 2 && e.timeStamp - drag.samples[0].t > VELOCITY_WINDOW_MS * 3) {
            drag.samples.shift();
        }
    }

    finishSheet(drag, cancelled) {
        try {
            drag.handle.releasePointerCapture(drag.pointerId);
        } catch {
            // 已释放
        }

        const v = velocity(drag.samples, 'y');
        const dismiss = !cancelled
            && drag.offset > 8
            && (drag.offset > drag.height * SHEET_DISMISS_RATIO || v > SHEET_DISMISS_VELOCITY);

        const { sheet, host } = drag;

        if (dismiss && sheet.dataset.guarded === 'true') {
            // 此刻关闭需要确认（记一笔已经输入了内容）：不把 Sheet 滑出屏幕——弹回原位，再「点击」遮罩，
            // 让 Blazor 的关闭拦截（BeforeClose）弹出「放弃本次记账？」；确认放弃后由 Blazor 完成卸载
            this.snapSheetBack(sheet, host);
            host.querySelector('.ft-scrim')?.click();
            return;
        }

        if (dismiss) {
            // 先把 Sheet 滑出屏幕，再「点击」遮罩：由 Blazor 的遮罩点击处理完成关闭与卸载。
            // data-dragged 让样式跳过退出动画（Sheet 已在屏幕外，动画会让它先跳回原位再滑出）
            host.dataset.dragged = 'true';
            sheet.style.transition = 'transform 220ms cubic-bezier(.4, 0, 1, 1)';
            sheet.style.transform = 'translateY(100%)';
            host.style.setProperty('--ft-sheet-scrim-opacity', '0');
            setTimeout(() => host.querySelector('.ft-scrim')?.click(), 200);
            return;
        }

        // 没拖够：弹回原位
        this.snapSheetBack(sheet, host);
    }

    /** Sheet 弹回原位（没拖够、或需要确认才能关闭时）。 */
    snapSheetBack(sheet, host) {
        sheet.style.transition = 'transform 260ms cubic-bezier(.22, .61, .36, 1)';
        sheet.style.transform = '';
        host.style.removeProperty('--ft-sheet-scrim-opacity');
        setTimeout(() => {
            sheet.style.transition = '';
        }, 280);
    }
}
