// 启动加载页的行为层（冷启动设计稿 docs/design/cold-start/01–03；样式见 css/boot.css，标记写在两个宿主页里、#app 之外）。
//
// 这是一段经典脚本（不是 ES module）：它要在 Blazor 启动之前就位，由两个宿主页在 </body> 之前同步引入，
// 配置通过自己的 data-* 属性传入：
//   data-css       首屏要用的样式表（图标字体 CSS），空格分隔
//   data-font      要等到下载完成的字体族（图标字体）
//   data-scripts   首屏用不到的脚本（ECharts），空格分隔
//
// 它做四件事：
//   1. 交接：首屏就绪后把加载页收走。就绪由两个信号共同决定——
//        外壳就绪：Blazor 完成了首次交互渲染（StartupHost 调 FtBoot.shellReady()；Blazor Server 的预渲染阶段不算，电路接管才算）；
//        首屏就绪：页面根节点声明 data-ft-ready="home|login"（Index.razor / LoginPage.razor）。
//      外壳就绪之后最多再等 capMs：到点仍没就绪就带着现状交出——不让任何人为了看一段动画多等。
//      启动路径不是 / 与 /login（浏览器深链接）没有「首屏」可等，外壳就绪即交。
//   2. 交接方式随首屏而定：login → Logo 组滑到登录页里 Logo 的位置、面板升起；home → 幕布收拢成 Hero；
//      其它 /「减弱动态效果」→ 整体淡出。
//   3. 转圈：启动超过 spinnerMs 才出现；快启动不闪。
//   4. 兜底：watchdogMs 内外壳还没就绪 → 「启动失败 / 重新加载」；之后外壳又起来了，自动恢复。
//
// 资源：图标字体 CSS 与 ECharts 来自 CDN，原先是 <head> 里的同步资源——挡住首次绘制，也拖住 window load
// （MAUI 的 BlazorWebView 在页面加载完成之后才启动 Blazor）。现在改为页面 load 之后由这里注入：
// 加载页本身不依赖网络；交接前只等图标字体就绪（或到 capMs 上限）；ECharts 只有图表用，不挡交接。
// 代价是用户可能在 ECharts 还没下载完时就进到带图表的页面：图表组件（Ui/EChartsView）因此先等 FtBoot.chartsReady()，
// 脚本到了才初始化图表——ECharts 没到就 echarts.init 会抛 ReferenceError，Blazor 当成未处理异常，整个界面就废了。
//
// 观察接口（E2E / 排错）：<html data-ft-boot="booting|handoff-login|handoff-home|handoff-fade|failed|done">，
// 以及 window.FtBoot.state / window.FtBoot.assetsSettled（CDN 资源是否都已加载完或失败）；
// window.FtBoot.chartsReady() 返回 Promise<boolean>（脚本都加载完或失败之后兑现，值 = 全局 echarts 是否可用）。
// 宿主页可在本脚本之前设置 window.FT_BOOT_TIMINGS = { spinnerMs, capMs, watchdogMs } 覆盖时间常量（E2E 用它缩短看门狗）。
(function () {
  'use strict';

  // 建议值，不是实测数据：真机上量过「点图标 → 外壳就绪」的分布之后再调
  var TIMINGS = { spinnerMs: 600, capMs: 1500, watchdogMs: 12000 };
  /** 动画结束后再多等一会儿才移除节点，防止最后一帧被截掉 */
  var FINISH_SLACK_MS = 60;
  var SPINNER_TICKS = 12;
  var FIRST_SCREEN_PATHS = ['/', '/login'];

  var override = window.FT_BOOT_TIMINGS;
  if (override && typeof override === 'object') {
    Object.keys(TIMINGS).forEach(function (key) {
      if (typeof override[key] === 'number') {
        TIMINGS[key] = override[key];
      }
    });
  }

  var root = document.documentElement;
  var boot = document.getElementById('ft-boot');
  var config = (document.currentScript && document.currentScript.dataset) || {};

  var state = 'booting';
  var shell = false; // Blazor 已完成首次交互渲染
  var capped = false; // 外壳就绪后等待首屏的上限已到
  var cssSettled = true; // 首屏要用的图标字体已就绪（或失败）
  var assetsSettled = true; // 所有 CDN 资源都已加载完（或失败）
  var scriptsSettled = true; // 延后注入的脚本（ECharts）都已加载完（或失败）
  var scriptWaiters = []; // chartsReady() 在等脚本时留下的回调
  var observer = null;
  var failureHost = null;
  var timers = { spinner: 0, watchdog: 0, cap: 0, finish: 0 };

  function setState(next) {
    state = next;
    root.setAttribute('data-ft-boot', next);
    if (boot) {
      boot.setAttribute('data-state', next);
    }
  }

  function prefersReducedMotion() {
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  }

  // ---------- 就绪判定 ----------

  function declaredKind() {
    var element = document.querySelector('[data-ft-ready]');
    return element ? element.getAttribute('data-ft-ready') : null;
  }

  /** 没有声明就绪标记时（到了上限 / 深链接），按页面里已有的东西推断该用哪种交接 */
  function inferKind() {
    if (document.querySelector('.ft-login__logo')) {
      return 'login';
    }

    if (document.querySelector('.ft-home .ft-hero')) {
      return 'home';
    }

    return 'fade';
  }

  function expectsFirstScreen() {
    return FIRST_SCREEN_PATHS.indexOf(window.location.pathname) !== -1;
  }

  function evaluate() {
    if (state !== 'booting' || !shell) {
      return;
    }

    var kind = declaredKind();
    var firstScreenReady = kind !== null || !expectsFirstScreen();
    if ((firstScreenReady && cssSettled) || capped) {
      handoff(kind || inferKind());
    }
  }

  // ---------- 交接 ----------

  /** 元素底边的布局位置：沿 offsetParent 链累加 offsetTop，再加 offsetHeight。
   *  不能用 getBoundingClientRect：页面栈的 Tab 根页面刚挂上时有 0.985 → 1 的入场缩放（--ft-dur-page），
   *  本地库读得快时交接恰好发生在缩放途中，量到的是缩小后的值；而幕布要对齐的是缩放结束后的 Hero */
  function layoutBottom(element) {
    var top = 0;
    for (var node = element; node; node = node.offsetParent) {
      top += node.offsetTop;
    }

    return top + element.offsetHeight;
  }

  /** 首页：量出真实 Hero 的高度，幕布收拢到这个高度。量不到（Hero 不在、高度为 0）返回 false，改用淡出 */
  function prepareHome() {
    var hero = document.querySelector('.ft-home .ft-hero');
    if (!hero) {
      return false;
    }

    var bottom = layoutBottom(hero);
    if (!(bottom > 0)) {
      return false;
    }

    boot.style.setProperty('--ft-boot-hero-h', bottom.toFixed(2) + 'px');
    return true;
  }

  /** 登录页：量出真实 Logo 与加载页 Logo 的中心差，Logo 组整体位移这个差。量不到返回 false，改用淡出 */
  function prepareLogin() {
    var target = document.querySelector('.ft-login__logo');
    var ours = boot.querySelector('.ft-boot__logo');
    if (!target || !ours) {
      return false;
    }

    var to = target.getBoundingClientRect();
    var from = ours.getBoundingClientRect();
    boot.style.setProperty('--ft-boot-dx', (to.left + to.width / 2 - (from.left + from.width / 2)).toFixed(2) + 'px');
    boot.style.setProperty('--ft-boot-dy', (to.top + to.height / 2 - (from.top + from.height / 2)).toFixed(2) + 'px');
    return true;
  }

  function toMs(value) {
    var number = parseFloat(value);
    if (!isFinite(number)) {
      return 0;
    }

    return /ms\s*$/.test(value) ? number : number * 1000;
  }

  /** 元素上 transition 的最长「时长 + 延迟」（毫秒）：交接要等多久，直接读样式表，改时长不用改脚本 */
  function longestTransitionMs(element) {
    var style = window.getComputedStyle(element);
    var durations = style.transitionDuration.split(',');
    var delays = style.transitionDelay.split(',');
    var longest = 0;
    for (var i = 0; i < durations.length; i++) {
      longest = Math.max(longest, toMs(durations[i]) + toMs(delays[i % delays.length]));
    }

    return longest;
  }

  function handoffMs(flavor) {
    var elements = flavor === 'fade' ? [boot] : [boot.querySelector('.ft-boot__curtain'), boot.querySelector('.ft-boot__brand')];
    return Math.max.apply(null, elements.map(function (element) {
      return element ? longestTransitionMs(element) : 0;
    }));
  }

  function handoff(kind) {
    window.clearTimeout(timers.cap);

    var flavor = prefersReducedMotion() ? 'fade' : kind;
    if (flavor === 'home' && !prepareHome()) {
      flavor = 'fade';
    } else if (flavor === 'login' && !prepareLogin()) {
      flavor = 'fade';
    } else if (flavor !== 'home' && flavor !== 'login') {
      flavor = 'fade';
    }

    setState('handoff-' + flavor);
    // 状态已写入，样式表里该状态的 transition 已生效，读得到真实时长
    timers.finish = window.setTimeout(finish, handoffMs(flavor) + FINISH_SLACK_MS);
  }

  function finish() {
    if (state === 'done') {
      return;
    }

    Object.keys(timers).forEach(function (key) {
      window.clearTimeout(timers[key]);
    });
    if (observer) {
      observer.disconnect();
      observer = null;
    }

    setState('done');
    if (boot && boot.parentNode) {
      boot.parentNode.removeChild(boot);
    }
  }

  // ---------- 转圈 ----------

  function showSlowHint() {
    if (state !== 'booting' || !boot) {
      return;
    }

    var spinner = boot.querySelector('.ft-boot__spinner');
    if (spinner && !spinner.firstChild) {
      for (var i = 0; i < SPINNER_TICKS; i++) {
        var tick = document.createElement('i');
        tick.style.setProperty('--i', String(i));
        spinner.appendChild(tick);
      }
    }

    boot.setAttribute('data-slow', 'true');
  }

  // ---------- 失败兜底 ----------

  function showFailure() {
    if (state !== 'booting' || shell || !boot) {
      return;
    }

    setState('failed');
    boot.removeAttribute('data-slow');

    // 标记与 GlassAlert 一致（ui.css 的 ft-alert），只有一个按钮；文案是固定字符串，没有用户内容
    failureHost = document.createElement('div');
    failureHost.className = 'ft-alert-host ft-boot__alert';
    failureHost.setAttribute('tabindex', '-1');
    failureHost.setAttribute('data-testid', 'boot-failed');
    failureHost.innerHTML =
      '<div class="ft-alert" role="alertdialog" aria-modal="true" aria-labelledby="ft-boot-failed-title" aria-describedby="ft-boot-failed-message">' +
      '<div class="ft-alert__body">' +
      '<h2 class="ft-alert__title" id="ft-boot-failed-title">启动失败</h2>' +
      '<p class="ft-alert__message" id="ft-boot-failed-message">没能打开记账本，请重新加载。反复出现时请重启应用。</p>' +
      '</div>' +
      '<div class="ft-alert__actions">' +
      '<button type="button" class="ft-alert__btn ft-alert__btn--strong" data-testid="boot-reload">重新加载</button>' +
      '</div>' +
      '</div>';
    boot.appendChild(failureHost);

    failureHost.querySelector('button').addEventListener('click', function () {
      window.location.reload();
    });
    // 与 GlassAlert 一样把焦点放在容器上（outline 已在 .ft-alert-host 里关掉）：读屏读出 Alert，按钮上不出现焦点环
    failureHost.focus();
  }

  /** 失败之后外壳又起来了（只是慢）：撤掉失败卡片，回到等待首屏 */
  function recover() {
    if (failureHost && failureHost.parentNode) {
      failureHost.parentNode.removeChild(failureHost);
    }

    failureHost = null;
    setState('booting');
  }

  // ---------- 外壳就绪（StartupHost 调用）----------

  function shellReady() {
    if (shell || state === 'done' || !boot) {
      return;
    }

    shell = true;
    window.clearTimeout(timers.watchdog);
    if (state === 'failed') {
      recover();
    }

    timers.cap = window.setTimeout(function () {
      capped = true;
      evaluate();
    }, TIMINGS.capMs);
    evaluate();
  }

  // ---------- 资源 ----------

  function splitList(value) {
    return value ? value.split(/\s+/).filter(Boolean) : [];
  }

  function injectResources(cssUrls, scriptUrls) {
    // 所有样式表算一项（它们一起就绪之后还要等字体），每个脚本各算一项
    var pending = (cssUrls.length > 0 ? 1 : 0) + scriptUrls.length;
    var scriptsPending = scriptUrls.length;

    function oneSettled() {
      pending--;
      if (pending <= 0) {
        assetsSettled = true;
      }
    }

    function oneScriptSettled() {
      oneSettled();
      scriptsPending--;
      if (scriptsPending <= 0) {
        scriptsSettled = true;
        scriptWaiters.splice(0).forEach(function (wake) {
          wake();
        });
      }
    }

    // 样式表加载完 ≠ 字体文件到位：图标字体在第一次用到字形时才下载，先显式把它拉下来，交接时图标才不是空白
    function cssDone() {
      var settle = function () {
        cssSettled = true;
        oneSettled();
        evaluate();
      };

      var family = config.font;
      if (!family || !document.fonts || !document.fonts.load) {
        settle();
        return;
      }

      document.fonts.load('1em "' + family + '"').then(settle, settle);
    }

    var cssPending = cssUrls.length;
    cssUrls.forEach(function (href) {
      var link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = href;
      var settled = false;
      link.onload = link.onerror = function () {
        if (settled) {
          return;
        }

        settled = true;
        cssPending--;
        if (cssPending === 0) {
          cssDone();
        }
      };

      document.head.appendChild(link);
    });

    scriptUrls.forEach(function (src) {
      var element = document.createElement('script');
      element.src = src;
      element.async = true;
      element.onload = element.onerror = oneScriptSettled;
      document.head.appendChild(element);
    });
  }

  function injectAfterLoad() {
    var cssUrls = splitList(config.css);
    var scriptUrls = splitList(config.scripts);
    if (cssUrls.length + scriptUrls.length === 0) {
      return;
    }

    // 先登记「还没就绪」，注入要等 load 之后才发生——load 之前就判定就绪会放过去
    cssSettled = cssUrls.length === 0;
    assetsSettled = false;
    scriptsSettled = scriptUrls.length === 0;

    var inject = function () {
      window.setTimeout(function () {
        injectResources(cssUrls, scriptUrls);
      }, 0);
    };

    if (document.readyState === 'complete') {
      inject();
    } else {
      window.addEventListener('load', inject, { once: true });
    }
  }

  // ---------- 启动 ----------

  /** 图表库（ECharts）就绪了吗：延后注入的脚本都加载完或失败之后兑现，值 = 全局 echarts 是否可用。
   *  图表组件（EChartsView）在初始化图表之前等它——ECharts 还没到就 echarts.init 会抛 ReferenceError，
   *  Blazor 把它当成未处理异常，整个界面就废了。函数体不用 this：Blazor 互操作可能不绑定 FtBoot。 */
  function chartsReady() {
    return new Promise(function (resolve) {
      var answer = function () {
        resolve(typeof window.echarts !== 'undefined');
      };

      if (typeof window.echarts !== 'undefined' || scriptsSettled) {
        answer();
      } else {
        scriptWaiters.push(answer);
      }
    });
  }

  window.FtBoot = {
    shellReady: shellReady,
    chartsReady: chartsReady,
    get state() {
      return state;
    },
    get assetsSettled() {
      return assetsSettled;
    },
  };

  injectAfterLoad();

  if (boot) {
    setState('booting');
    timers.spinner = window.setTimeout(showSlowHint, TIMINGS.spinnerMs);
    timers.watchdog = window.setTimeout(showFailure, TIMINGS.watchdogMs);
    // 页面根节点声明 / 撤销 data-ft-ready、页面换了（未登录从首页 Replace 到登录页）都要重新判定
    observer = new MutationObserver(evaluate);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-ft-ready'] });
  }
})();
