/* 冷启动设计稿的重复积木：状态栏 / 手势条 / Logo 图标 / 转圈。
   Logo 图标是 MDI wallet-outline 的原始字形——与真实登录页 .ft-login__logo 里的 <i class="mdi mdi-wallet-outline"> 同一个图形。
   加载页用内联 SVG 而不用图标字体：图标字体来自 CDN，离线 / 慢网时还没加载，而加载页恰恰要在这时显示。 */
(function () {
    const GLYPH =
        '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#fff" ' +
        'd="M5,3C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V16.72C21.59,16.37 22,15.74 22,15V9C22,8.26 21.59,7.63 21,7.28V5A2,2 0 0,0 19,3H5M5,5H19V7H13A2,2 0 0,0 11,9V15A2,2 0 0,0 13,17H19V19H5V5M13,9H20V15H13V9M16,10.5A1.5,1.5 0 0,0 14.5,12A1.5,1.5 0 0,0 16,13.5A1.5,1.5 0 0,0 17.5,12A1.5,1.5 0 0,0 16,10.5Z"/></svg>';

    const SIGNAL =
        '<svg width="19" height="13" viewBox="0 0 19 13"><rect x="0" y="8.6" width="3.2" height="4.4" rx="1"/><rect x="5.2" y="5.8" width="3.2" height="7.2" rx="1"/>' +
        '<rect x="10.4" y="2.9" width="3.2" height="10.1" rx="1"/><rect x="15.6" y="0" width="3.2" height="13" rx="1"/></svg>';
    const WIFI =
        '<svg width="18" height="13" viewBox="0 0 18 13" fill="none" stroke="currentColor" stroke-width="2.3" stroke-linecap="round">' +
        '<path d="M1.4 4.5a10.6 10.6 0 0 1 15.2 0"/><path d="M4.2 7.5a6.6 6.6 0 0 1 9.6 0"/><circle cx="9" cy="10.7" r="1.35" fill="currentColor" stroke="none"/></svg>';
    const BATTERY =
        '<svg width="28" height="13" viewBox="0 0 28 13"><rect x=".6" y=".6" width="23" height="11.8" rx="3.7" fill="none" stroke="currentColor" opacity=".42" stroke-width="1.2"/>' +
        '<rect x="2.3" y="2.3" width="19.6" height="8.4" rx="2.3" fill="currentColor"/><path d="M25.2 4.5v4c.85-.35 1.5-1.1 1.5-2s-.65-1.65-1.5-2Z" opacity=".5" fill="currentColor"/></svg>';

    /** data-chrome="light|dark"（状态栏文字色）；data-hi="dark" 手势条用深色；data-nostatus 不画状态栏；data-nohi 不画手势条（底图自带时用） */
    function chrome(el) {
        const dark = el.dataset.chrome === 'dark';
        const hiDark = el.dataset.hi === 'dark';
        const noStatus = el.hasAttribute('data-nostatus');
        const noHi = el.hasAttribute('data-nohi');
        const wrap = document.createElement('div');
        wrap.innerHTML =
            '<div class="island"></div>' +
            (noStatus ? '' : '<div class="statusbar' + (dark ? ' statusbar--dark' : '') + '"><span class="time">9:41</span><span class="icons">' + SIGNAL + WIFI + BATTERY + '</span></div>') +
            (noHi ? '' : '<div class="hindicator' + (hiDark ? ' hindicator--dark' : '') + '"></div>');
        el.replaceWith(...wrap.childNodes);
    }

    function spinner(el) {
        el.classList.add('spinner');
        el.innerHTML = Array.from({ length: 12 }, (_, i) => '<i style="--i:' + i + '"></i>').join('');
    }

    function hydrate(root) {
        (root || document).querySelectorAll('[data-glyph]').forEach((el) => { el.innerHTML = GLYPH; });
        (root || document).querySelectorAll('[data-spinner]').forEach(spinner);
        (root || document).querySelectorAll('[data-chrome]').forEach(chrome);
    }
    window.ColdStart = { GLYPH, SIGNAL, WIFI, BATTERY, hydrate };
    document.addEventListener('DOMContentLoaded', () => hydrate());
})();
