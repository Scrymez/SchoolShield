/*
 * SchoolShield — размытие рекламы на сайтах (встроенный браузер).
 *
 * Запускается во всех кадрах каждой страницы до выполнения скриптов сайта. Находит рекламу:
 *   - по явным признакам (ins.adsbygoogle, iframe рекламных сетей, id/class вида adfox, yandex_rtb, advert, ad-slot…);
 *   - по меткам «Реклама», «Sponsored», «erid:» (в РФ обязательная маркировка) и контейнеру вокруг них;
 *   - по стандартным размерам рекламных блоков IAB (300x250, 728x90 …) в сочетании с другими признаками;
 *   - по площадкам: YouTube, VK, Яндекс, Google, Bing, Mail.ru.
 * и размывает её: фильтр blur, прозрачность, без кликов. Снять блюр на странице нельзя:
 * правило дублируется атрибутом, стилем и наблюдателем, который возвращает всё обратно, а инструментов
 * разработчика, расширений и javascript: во встроенном браузере нет.
 *
 * Не размывается: реклама ВНУТРИ видеоплеера YouTube, RuTube и VK Видео. Остальная реклама на этих сайтах размывается.
 * Конфиг приходит от программы: window.__ssCfg = { videoHosts: [...], embedOk: [...] }.
 */
(function () {
  'use strict';
  if (window.__ssAdBlur) return;
  window.__ssAdBlur = true;

  var CFG = window.__ssCfg || {};
  var VIDEO_HOSTS = CFG.videoHosts || ['youtube.com', 'youtu.be', 'rutube.ru', 'vkvideo.ru', 'vk.com', 'vk.ru'];
  var EMBED_OK = CFG.embedOk || ['youtube.com', 'youtube-nocookie.com', 'youtu.be', 'rutube.ru', 'vkvideo.ru', 'vk.com', 'vk.ru',
    'player.vimeo.com', 'dzen.ru', 'ok.ru', 'yandex.ru/maps', 'google.com/maps', 'openstreetmap.org', 'docs.google.com', 'forms.gle',
    'recaptcha.net', 'hcaptcha.com', 'challenges.cloudflare.com', 'disk.yandex.ru', 'mc.yandex.ru'];
  var AD_HOSTS = CFG.adHosts || [];

  var host = (location.hostname || '').toLowerCase();
  var isTop = window.top === window;
  function hostIn(h, list) {
    for (var i = 0; i < list.length; i++) {
      var x = list[i];
      if (x.indexOf('/') >= 0) continue;
      if (h === x || h.length > x.length && h.charAt(h.length - x.length - 1) === '.' && h.slice(-x.length) === x) return true;
    }
    return false;
  }
  var VIDEO_PAGE = hostIn(host, VIDEO_HOSTS);

  // ---------------------------------------------------------------- 1. сам кадр — реклама

  function frameIsAd() {
    if (isTop) return false;
    if (hostIn(host, AD_HOSTS)) return true;
    if (/^(ads?|adserver|adservice|banners?|adfox|rtb|an|pagead\d*)\./.test(host)) return true;
    if (/\/(pagead|adframe|adserver|ads\/|adview|adsbygoogle|doubleclick)/i.test(location.pathname + location.search)) return true;
    return false;
  }

  var STYLE_CSS =
    '[data-ss-blur]{filter:blur(22px) grayscale(1) brightness(.85)!important;opacity:.45!important;pointer-events:none!important;' +
    'user-select:none!important;-webkit-user-select:none!important;}';

  function installStyle() {
    var root = document.head || document.documentElement;
    if (!root) return false;
    var st = document.getElementById('__ss_blur_css');
    if (!st) {
      st = document.createElement('style');
      st.id = '__ss_blur_css';
      st.textContent = STYLE_CSS;
      root.appendChild(st);
    }
    return true;
  }

  if (frameIsAd()) {
    // Рекламный кадр размывается целиком, независимо от того, что решит родитель.
    var killCss = 'html{filter:blur(24px) grayscale(1)!important;opacity:.4!important;pointer-events:none!important;}';
    (function force() {
      var root = document.head || document.documentElement;
      if (root) {
        var s = document.createElement('style');
        s.textContent = killCss;
        root.appendChild(s);
        new MutationObserver(function () { if (!s.isConnected) root.appendChild(s); }).observe(root, { childList: true });
      } else setTimeout(force, 5);
    })();
    return;
  }

  // ---------------------------------------------------------------- 2. признаки рекламы

  // Размеры стандартных рекламных блоков IAB.
  var IAB = [[300, 250], [336, 280], [728, 90], [970, 90], [970, 250], [468, 60], [320, 50], [320, 100], [300, 600], [160, 600],
    [120, 600], [300, 50], [250, 250], [240, 400], [980, 120], [930, 180], [750, 200], [750, 300], [300, 1050], [580, 400], [640, 100]];
  function iabSize(w, h) {
    for (var i = 0; i < IAB.length; i++)
      if (Math.abs(w - IAB[i][0]) <= 4 && Math.abs(h - IAB[i][1]) <= 4) return true;
    return false;
  }

  // Сильные признаки в id/class/name: реклама почти наверняка.
  var STRONG = /(^|[^a-z0-9])(adsbygoogle|google[_-]?ads?|googlead|doubleclick|dfp[_-]|gpt[_-]?ad|adfox|yandex[_-]?rtb|yandex[_-]?adv|adv[_-]?(?:banner|block|slot|wrap|container|place|box)|advert(?:isement|ising|isment)?|ad[_-](?:slot|unit|banner|block|container|wrapper|wrap|frame|iframe|placement|zone|label|box|space|holder)|banner[_-]?ad|ads?[_-](?:slot|unit|banner|block|container|wrapper|wrap|frame|zone|box|holder|top|bottom|left|right|sidebar|content|inline|item)|(?:top|bottom|side|sidebar|inline|header|footer|content)[_-]ads?|sponsored|adriver|marketgid|mgid|taboola|outbrain|begun|directadvert|admixer|relap|24smi|tizer|teasernet|rtb[_-]|promoted[_-]?(?:post|content|video|link)|native[_-]?ad)([^a-z0-9]|$)/i;
  // Короткое слово «ad/ads» как отдельный токен.
  var AD_TOKEN = /(^|[\s_-])ads?([\s_-]|$)/i;
  var WEAK = /(banner|promo|sponsor|teaser|partner)/i;

  // Метки рекламы (в РФ обязательны: «Реклама», «erid»).
  var LABEL = /^(?:реклама|рекламное объявление|рекламный блок|рекламная публикация|на правах рекламы|партн[её]рский материал|партн[её]рская реклама|рекламодатель|спонсорский материал|спонсорская реклама|промо|ad|ads|advertisement|advertising|advertorial|sponsored|sponsored content|sponsored post|sponsored link|sponsored by.{0,40}|promoted|promoted by.{0,40}|ad choices|adchoices|paid content|paid post|paid partnership|partner content|реклама\.?\s*[«"“].{0,80}|erid\s*[:=]\s*[a-z0-9]{6,})\.?$/i;
  var ERID = /erid\s*[:=]\s*[a-z0-9]{6,}/i;

  // Явно взрослые слова в тексте страницы (заголовки и описания в выдаче поиска, спам на взломанных сайтах):
  // блок с таким текстом размывается так же, как реклама.
  var ADULT = /(porn|порн|(^|[^a-z])xxx([^a-z]|$)|xnxx|xvideos|xhamster|hentai|хентай|blowjob|bukkake|blue\s*film|эротическ|sex\s*video|секс[\s-]?видео)/i;

  var TRACK = /[?&](utm_|yclid|gclid|fbclid|clickid|erid|ref=)/i;

  // Явные контейнеры рекламы на крупных площадках и в поиске.
  var SELECTORS = [
    'ins.adsbygoogle', '.adsbygoogle', '[data-ad-slot]', '[data-ad-client]', '[data-ad-unit]', '[data-adunit]',
    'iframe[id^="google_ads_iframe"]', 'iframe[name^="google_ads_iframe"]', 'iframe[id*="aswift"]', 'div[id^="div-gpt-ad"]',
    '[id^="google_ads"]', '[id^="adfox_"]', '[id^="yandex_rtb_"]', '[class*="yandex-adv" i]', 'div[id*="_RTB" i]',
    'iframe[src*="doubleclick.net"]', 'iframe[src*="googlesyndication.com"]', 'iframe[src*="adfox.ru"]', 'iframe[src*="an.yandex.ru"]',
    'iframe[src*="yandexadexchange.net"]', 'iframe[src*="adriver.ru"]', 'iframe[src*="mgid.com"]', 'iframe[src*="taboola.com"]',
    'iframe[src*="outbrain.com"]', 'iframe[src*="marketgid.com"]', 'iframe[src*="begun.ru"]', 'iframe[src*="ad.mail.ru"]',
    'iframe[src*="mytarget"]', 'iframe[src*="criteo."]', 'iframe[src*="exoclick."]', 'iframe[src*="trafficjunky."]', 'iframe[src*="juicyads."]',
    // поиск
    '#tads', '#tadsb', '#bottomads', '#tvcap .commercial', '.commercial', '[data-text-ad]', '.uEierd', '[aria-label="Ads"]', '[aria-label="Реклама"]',
    '.b_ad', 'li.b_ad', '.sb_adsWrapper', '.serp-adv', '.serp-item_card_adv', 'li.serp-item[data-fast-name="companies_adv"]',
    // YouTube (площадка: баннеры, ленты, боковые блоки; реклама внутри плеера исключена отдельно)
    'ytd-ad-slot-renderer', 'ytd-display-ad-renderer', 'ytd-promoted-sparkles-web-renderer', 'ytd-promoted-video-renderer',
    'ytd-in-feed-ad-layout-renderer', 'ytd-banner-promo-renderer', 'ytd-statement-banner-renderer', 'ytd-primetime-promo-renderer',
    'ytd-companion-slot-renderer', 'ytd-action-companion-ad-renderer', '#masthead-ad', '#player-ads', 'ytd-merch-shelf-renderer',
    'ytm-promoted-sparkles-web-renderer', 'ytm-companion-ad-renderer',
    // VK
    '.ads_ad_box', '#ads_left', '.ads_ads_news_wrap', '[data-ad-view]', '.wall_marked_as_ads', '.post_marked_as_ads', '._ads_block_data_w', '.ads_ad_unit',
    '.AdsBanner', '[class*="AdsBlock" i]',
    // Mail.ru / Дзен / новостные сайты РФ
    '[class*="mailru-ad" i]', '[data-ad]', '[data-banner-id]', '[class*="adfox" i]', '[id*="adfox" i]', '[class*="teaser-block" i]',
    '[id*="tizer" i]', '[class*="tizer" i]', '[class*="_advert" i]', '[class*="advertisement" i]'
  ].join(',');

  // Что нельзя размывать, чтобы не сломать страницу и видео.
  var PLAYER_SEL = 'video, #movie_player, .html5-video-player, ytd-player, #player-container, #player, .ytp-ad-module, .video-ads,' +
    '[class*="video-player" i], [class*="videoplayer" i], [class*="player-container" i], [id*="video_player" i], [class*="VideoPage" i],' +
    '.videoplayer, .video_box_wrap, [class*="rutube-player" i], [class*="PlayerWrapper" i]';

  function isEmbedOk(src) {
    if (!src) return false;
    try {
      var u = new URL(src, location.href);
      var h = u.hostname.toLowerCase();
      var path = h + u.pathname;
      for (var i = 0; i < EMBED_OK.length; i++) {
        var x = EMBED_OK[i];
        if (x.indexOf('/') >= 0) { if (path.indexOf(x) === 0) return true; }
        else if (h === x || h.slice(-x.length - 1) === '.' + x) return true;
      }
    } catch (e) { }
    return false;
  }

  function srcHost(src) {
    try { return new URL(src, location.href).hostname.toLowerCase(); } catch (e) { return ''; }
  }

  function attrText(el) {
    var cls = typeof el.className === 'string' ? el.className : (el.getAttribute && el.getAttribute('class')) || '';
    return (el.id || '') + ' ' + cls + ' ' + ((el.getAttribute && el.getAttribute('name')) || '');
  }

  function inVideoPlayer(el) {
    if (!VIDEO_PAGE) return false;
    try { return !!(el.closest(PLAYER_SEL) || el.querySelector('video')); } catch (e) { return false; }
  }

  // Оценка элемента: чем выше, тем вероятнее реклама. Порог — 5.
  function score(el) {
    var tag = el.tagName;
    if (!tag) return 0;
    tag = tag.toLowerCase();
    var s = 0;
    var at = attrText(el);
    if (STRONG.test(at)) s += 5;
    else if (AD_TOKEN.test(at)) s += 3;
    else if (WEAK.test(at)) s += 1;
    if (el.hasAttribute && (el.hasAttribute('data-ad') || el.hasAttribute('data-ad-slot') || el.hasAttribute('data-adunit'))) s += 5;

    var media = tag === 'iframe' || tag === 'img' || tag === 'picture' || tag === 'canvas' || tag === 'ins';
    if (!media && s === 0) return 0;   // дешёвый выход: без признаков не считаем размеры (они вызывают перерасчёт раскладки)
    var r = el.getBoundingClientRect();
    var w = r.width, h = r.height;
    if (w < 40 || h < 20) return 0;
    var iab = iabSize(w, h);

    if (tag === 'iframe') {
      var src = el.getAttribute('src') || '';
      if (isEmbedOk(src)) return 0;
      var sh = srcHost(src);
      if (sh && hostIn(sh, AD_HOSTS)) s += 6;
      if (sh && sh !== host && !hostIn(sh, [host])) s += 2;
      if (iab) s += 3;
      if (!src || src === 'about:blank') { if (iab) s += 1; }
    } else if (tag === 'img' || tag === 'picture' || tag === 'canvas') {
      var a = el.closest && el.closest('a[href]');
      if (a) {
        var href = a.getAttribute('href') || '';
        var ah = srcHost(href);
        var external = ah && ah !== host;
        if (external && (a.target === '_blank' || /sponsored|nofollow/i.test(a.rel || ''))) s += 2;
        if (external && TRACK.test(href)) s += 3;
        if (external && hostIn(ah, AD_HOSTS)) s += 6;
        if (iab) s += 2;
      }
    } else {
      if (iab && s >= 1) s += 2;
    }
    return s;
  }

  // ---------------------------------------------------------------- 3. размытие

  var blurred = [];
  var guard = null;

  function protectedEl(el) {
    var t = el.tagName;
    if (!t) return true;
    t = t.toLowerCase();
    if (t === 'html' || t === 'body' || t === 'head' || t === 'main' || t === 'article' || t === 'video' || t === 'audio') return true;
    if (el === document.documentElement || el === document.body) return true;
    if (inVideoPlayer(el)) return true;
    var r = el.getBoundingClientRect();
    var vw = Math.max(window.innerWidth, 1), vh = Math.max(window.innerHeight, 1);
    // Огромный контейнер — скорее всего ошибка (обёртка всей страницы); реклама бывает «на весь экран» только в iframe/ins.
    if (t !== 'iframe' && t !== 'ins' && r.width * r.height > 0.55 * vw * vh && r.height > vh * 0.7) return true;
    try { if (t !== 'iframe' && t !== 'ins' && el.querySelector('input[type="text"],input[type="search"],input[type="password"],textarea')) return true; } catch (e) { }
    return false;
  }

  function blur(el) {
    if (!el || el.__ssb || protectedEl(el)) return;
    el.__ssb = true;
    blurred.push(el);
    el.setAttribute('data-ss-blur', '1');
    var st = el.style;
    st.setProperty('filter', 'blur(22px) grayscale(1) brightness(.85)', 'important');
    st.setProperty('opacity', '.45', 'important');
    st.setProperty('pointer-events', 'none', 'important');
    st.setProperty('user-select', 'none', 'important');
    if (guard) guard.observe(el, { attributes: true, attributeFilter: ['style', 'class', 'hidden', 'data-ss-blur'] });
  }

  // Картинка-баннер внутри ссылки: размываем и ссылку, иначе клик «проваливается» на неё.
  function blurMedia(el) {
    blur(el);
    var tg = el.tagName.toLowerCase();
    if (tg !== 'img' && tg !== 'picture' && tg !== 'canvas' && tg !== 'iframe') return;
    var a = el.closest && el.closest('a[href]');
    if (!a || a.__ssb) return;
    // Ссылка только из картинки (без текста) — это и есть баннер: размываем и её. Карточки «картинка + заголовок» не трогаем.
    if ((a.textContent || '').replace(/\s+/g, '').length === 0 && a.querySelectorAll('img, picture, canvas, iframe, svg').length <= 2) blur(a);
  }

  // Если сайт или пользователь пытается вернуть как было, возвращаем блюр.
  function enforce() {
    for (var i = 0; i < blurred.length; i++) {
      var el = blurred[i];
      if (!el.isConnected) continue;
      if (!el.hasAttribute('data-ss-blur')) el.setAttribute('data-ss-blur', '1');
      var st = el.style;
      if (st.getPropertyValue('filter').indexOf('blur') < 0 || st.getPropertyPriority('filter') !== 'important')
        st.setProperty('filter', 'blur(22px) grayscale(1) brightness(.85)', 'important');
      if (st.getPropertyValue('pointer-events') !== 'none') st.setProperty('pointer-events', 'none', 'important');
      if (parseFloat(st.getPropertyValue('opacity') || '1') > 0.5) st.setProperty('opacity', '.45', 'important');
    }
  }

  // ---------------------------------------------------------------- 4. метки «Реклама» → карточка вокруг

  function cardAround(node) {
    var el = node.parentElement;
    var best = null;
    for (var depth = 0; el && depth < 7; depth++, el = el.parentElement) {
      var tn = el.tagName.toLowerCase();
      if (tn === 'body' || tn === 'html' || tn === 'main') break;
      var r = el.getBoundingClientRect();
      if (r.width < 90 || r.height < 36) continue;
      var vh = Math.max(window.innerHeight, 1);
      if (r.height > Math.max(900, vh * 1.2)) break;
      var hasContent = !!el.querySelector('a[href], img, iframe, picture, svg');
      if (!hasContent) continue;
      best = el;
      // подходящий «карточный» тег и достаточный размер — берём его
      if ((tn === 'li' || tn === 'article' || tn === 'aside' || tn === 'section' || /^(card|item|post|block|result|serp|snippet|teaser|unit)/i.test((el.getAttribute('class') || '').split(/\s+/)[0] || '')) && r.height >= 60) break;
      if (r.height >= 110 && r.width >= 180) break;
    }
    return best;
  }

  function scanLabels(root) {
    var walker;
    try { walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null); } catch (e) { return; }
    var n, count = 0;
    while ((n = walker.nextNode()) && count < 4000) {
      count++;
      var t = n.nodeValue;
      if (!t) continue;
      t = t.trim();
      if (t.length < 2 || t.length > 400) continue;
      var hit = (t.length <= 90 && LABEL.test(t)) || (t.length <= 320 && ERID.test(t)) || ADULT.test(t);
      if (!hit) continue;
      var p = n.parentElement;
      if (!p || p.__ssLabel) continue;
      var tg = p.tagName.toLowerCase();
      if (tg === 'script' || tg === 'style' || tg === 'title' || tg === 'option' || tg === 'textarea') continue;
      p.__ssLabel = true;
      var card = cardAround(n);
      if (card) blur(card);
    }
  }

  // ---------------------------------------------------------------- 5. проходы

  function scanElements(root) {
    var list;
    try { list = root.querySelectorAll(SELECTORS); } catch (e) { list = []; }
    for (var i = 0; i < list.length; i++) {
      var el = list[i];
      // iframe с встроенным видео и карты не трогаем
      if (el.tagName === 'IFRAME' && isEmbedOk(el.getAttribute('src'))) continue;
      blurMedia(el);
    }
    var cands;
    try { cands = root.querySelectorAll('iframe, ins, img, picture, canvas, [id], [class]'); } catch (e) { cands = []; }
    var limit = Math.min(cands.length, 6000);
    for (var j = 0; j < limit; j++) {
      var c = cands[j];
      if (c.__ssb || c.__ssSeen === 2) continue;
      var sc = score(c);
      if (sc >= 5) blurMedia(c);
      else c.__ssSeen = 1;
    }
  }

  var pending = [];
  var scheduled = false;
  function schedule(node) {
    pending.push(node);
    if (scheduled) return;
    scheduled = true;
    (window.requestAnimationFrame || setTimeout)(function () {
      scheduled = false;
      var batch = pending.splice(0, pending.length);
      for (var i = 0; i < batch.length; i++) {
        var n = batch[i];
        if (n.nodeType !== 1 || !n.isConnected) continue;
        scanElements(n);
        scanLabels(n);
        if (n.__ssb !== true) { var s = score(n); if (s >= 5) blur(n); }
      }
    });
  }

  function fullScan() {
    var root = document.body || document.documentElement;
    if (!root) return;
    scanElements(document);
    scanLabels(root);
    enforce();
  }

  function init() {
    if (!installStyle()) { setTimeout(init, 10); return; }
    guard = new MutationObserver(function () { enforce(); });
    new MutationObserver(function (muts) {
      for (var i = 0; i < muts.length; i++) {
        var m = muts[i];
        if (m.type === 'childList') {
          for (var j = 0; j < m.addedNodes.length; j++) if (m.addedNodes[j].nodeType === 1) schedule(m.addedNodes[j]);
        } else if (m.type === 'attributes' && m.target.nodeType === 1) schedule(m.target);
      }
      // стиль блюра могли удалить — вернуть
      if (!document.getElementById('__ss_blur_css')) installStyle();
    }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'id', 'src', 'style'] });
    fullScan();
    // ленивая реклама появляется после прокрутки/загрузки: первые 40 секунд часто, потом редко
    var ticks = 0;
    (function loop() {
      fullScan();
      ticks++;
      setTimeout(loop, ticks < 30 ? 1300 : 4000);
    })();
    window.addEventListener('load', fullScan);
    window.addEventListener('scroll', function () { schedule(document.body || document.documentElement); }, { passive: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
