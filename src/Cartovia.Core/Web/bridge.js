/*
 * Cartovia bridge.
 * Runs inside the WebView, drives Mapbox GL JS or MapLibre GL JS and talks to .NET through a JSON message channel.
 *
 * .NET -> JS : {t:'init', options} | {t:'call', id, cmd, args} | {t:'hostResult', id, ok, value, error}
 * JS -> .NET : {t:'ready'} | {t:'created'} | {t:'fatal', message} | {t:'result', id, ok, value, error}
 *              {t:'event', name, data, sid?, layer?, target?} | {t:'log', level, message} | {t:'hostCall', id, name, data}
 */
(function () {
  'use strict';

  var webview = window.chrome && window.chrome.webview;

  var state = {
    map: null, lib: null, engine: null, options: null,
    markers: new Map(), popups: new Map(), controls: new Map(), subs: new Map(), helpers: new Map(),
    plugins: new Map(), pluginFactories: new Map(), interactions: new Set(),
    userSources: new Map(), userLayers: new Set(), userImages: new Map(),
    hostCalls: new Map(), hostCallId: 0, assets: new Map(), animId: 0
  };

  // ------------------------------------------------------------------ messaging

  function safeStringify(msg) {
    try { return JSON.stringify(msg); }
    catch (err) {
      if (msg && msg.t === 'result') {
        return JSON.stringify({ t: 'result', id: msg.id, ok: false, error: { message: 'Result is not serializable: ' + err.message } });
      }
      return JSON.stringify({ t: 'log', level: 'error', message: 'Unserializable message: ' + err.message });
    }
  }

  function post(msg) {
    var text = safeStringify(msg);
    if (webview) webview.postMessage(text); else console.log('[Cartovia]', text);
  }

  function emit(name, data, extra) {
    var msg = { t: 'event', name: name, data: data === undefined ? null : data };
    if (extra) { for (var k in extra) { if (extra[k] !== undefined && extra[k] !== null) msg[k] = extra[k]; } }
    post(msg);
  }

  function log(level, message) { post({ t: 'log', level: level, message: String(message) }); }

  function fatal(message) {
    post({ t: 'fatal', message: message });
    var el = document.createElement('div');
    el.className = 'cartovia-fatal';
    el.textContent = message;
    document.body.appendChild(el);
  }

  window.addEventListener('error', function (e) {
    log('error', (e.message || 'Script error') + (e.filename ? ' (' + e.filename + ':' + e.lineno + ')' : ''));
  });
  window.addEventListener('unhandledrejection', function (e) {
    var r = e.reason;
    log('error', 'Unhandled promise rejection: ' + (r && (r.message || r)));
  });

  function callHost(name, data) {
    var id = ++state.hostCallId;
    return new Promise(function (resolve, reject) {
      state.hostCalls.set(id, { resolve: resolve, reject: reject });
      post({ t: 'hostCall', id: id, name: name, data: data === undefined ? null : ser(data) });
    });
  }

  // ------------------------------------------------------------------ assets

  function loadScript(url, isModule) {
    if (state.assets.has(url)) return state.assets.get(url);
    var p = new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = url;
      if (isModule) s.type = 'module';
      s.crossOrigin = 'anonymous';
      s.onload = function () { resolve(); };
      s.onerror = function () { state.assets.delete(url); reject(new Error('Unable to load script ' + url)); };
      document.head.appendChild(s);
    });
    state.assets.set(url, p);
    return p;
  }

  function loadCss(url) {
    if (state.assets.has(url)) return state.assets.get(url);
    var p = new Promise(function (resolve) {
      var l = document.createElement('link');
      l.rel = 'stylesheet';
      l.href = url;
      l.onload = function () { resolve(); };
      l.onerror = function () { log('warn', 'Unable to load stylesheet ' + url); resolve(); };
      document.head.appendChild(l);
    });
    state.assets.set(url, p);
    return p;
  }

  // ------------------------------------------------------------------ serialization

  function isPlainObject(v) {
    var proto = Object.getPrototypeOf(v);
    return proto === Object.prototype || proto === null;
  }

  function ser(v, depth) {
    depth = depth || 0;
    if (v === undefined || v === null) return null;
    var t = typeof v;
    if (t === 'number') return isFinite(v) ? v : null;
    if (t === 'string' || t === 'boolean') return v;
    if (t === 'function' || t === 'symbol') return null;
    if (v === state.map) return null;
    var L = state.lib;
    if (L) {
      if (L.LngLatBounds && v instanceof L.LngLatBounds) return v.toArray();
      if (L.LngLat && v instanceof L.LngLat) return [v.lng, v.lat];
      if (L.Point && v instanceof L.Point) return [v.x, v.y];
      if (L.Marker && v instanceof L.Marker) return null;
      if (L.Popup && v instanceof L.Popup) return null;
    }
    if (typeof Element !== 'undefined' && v instanceof Element) return null;
    if (depth > 32) return null;
    if (Array.isArray(v) || ArrayBuffer.isView(v)) {
      var arr = [];
      for (var i = 0; i < v.length; i++) arr.push(ser(v[i], depth + 1));
      return arr;
    }
    if (typeof v.toJSON === 'function') { try { return ser(v.toJSON(), depth + 1); } catch (e) { return null; } }
    if (!isPlainObject(v)) {
      // Class instances (features, sources…) are reduced to their own enumerable data members.
      if ('x' in v && 'y' in v && typeof v.x === 'number' && Object.keys(v).length <= 3) return [v.x, v.y];
      if ('lng' in v && 'lat' in v) return [v.lng, v.lat];
      if (depth > 4) return null;
    }
    var o = {};
    var keys = Object.keys(v);
    for (var j = 0; j < keys.length; j++) {
      var k = keys[j];
      if (k.charAt(0) === '_') continue;
      var val = ser(v[k], depth + 1);
      if (val !== null) o[k] = val;
    }
    return o;
  }

  function featureJson(f) {
    if (!f) return null;
    try {
      var j = typeof f.toJSON === 'function' ? f.toJSON() : f;
      var out = {
        type: 'Feature',
        id: j.id === undefined ? (f.id === undefined ? null : f.id) : j.id,
        geometry: ser(j.geometry || f.geometry),
        properties: ser(j.properties || f.properties) || {}
      };
      var layer = j.layer || f.layer;
      if (layer) out.layer = { id: layer.id, type: layer.type, source: layer.source, 'source-layer': layer['source-layer'] };
      var source = j.source || f.source; if (source) out.source = source;
      var sourceLayer = j.sourceLayer || f.sourceLayer; if (sourceLayer) out.sourceLayer = sourceLayer;
      var st = j.state || f.state; if (st) out.state = ser(st);
      if (f.namespace) out.namespace = f.namespace;
      if (f.target) out.target = ser(f.target);
      return out;
    } catch (e) {
      return null;
    }
  }

  // ------------------------------------------------------------------ events

  var MOUSE_EVENTS = { click: 1, dblclick: 1, contextmenu: 1, mousedown: 1, mouseup: 1, mousemove: 1, mouseover: 1, mouseout: 1, mouseenter: 1, mouseleave: 1, wheel: 1, touchstart: 1, touchend: 1, touchmove: 1, touchcancel: 1 };
  var CAMERA_EVENT = /^(move|zoom|rotate|pitch|drag|boxzoom)(start|end|cancel)?$/;
  var HIGH_FREQUENCY = { move: 1, zoom: 1, rotate: 1, pitch: 1, drag: 1, mousemove: 1, render: 1, wheel: 1, touchmove: 1 };

  function throttle(fn, ms) {
    var last = 0, timer = null, pending = null;
    return function () {
      var now = performance.now();
      pending = arguments;
      if (now - last >= ms) {
        last = now; fn.apply(null, pending); pending = null;
      } else if (!timer) {
        timer = setTimeout(function () {
          timer = null; last = performance.now();
          if (pending) { fn.apply(null, pending); pending = null; }
        }, ms - (now - last));
      }
    };
  }

  function camera() {
    var m = state.map;
    var bounds = null;
    try { bounds = ser(m.getBounds()); } catch (e) { bounds = null; }
    return { center: ser(m.getCenter()), zoom: m.getZoom(), bearing: m.getBearing(), pitch: m.getPitch(), bounds: bounds };
  }

  function mouse(e) {
    var o = (e && e.originalEvent) || {};
    var data = {
      lngLat: e && e.lngLat ? [e.lngLat.lng, e.lngLat.lat] : null,
      point: e && e.point ? [e.point.x, e.point.y] : null,
      button: typeof o.button === 'number' ? o.button : null,
      buttons: typeof o.buttons === 'number' ? o.buttons : null,
      altKey: !!o.altKey, ctrlKey: !!o.ctrlKey, shiftKey: !!o.shiftKey, metaKey: !!o.metaKey
    };
    if (typeof o.deltaY === 'number') data.deltaY = o.deltaY;
    if (e && e.features) data.features = e.features.map(featureJson);
    return data;
  }

  function payload(name, e) {
    if (MOUSE_EVENTS[name]) return mouse(e);
    if (CAMERA_EVENT.test(name)) {
      var c = camera();
      c.userInitiated = !!(e && e.originalEvent);
      if (e && e.cartoviaAnim) c.animationId = e.cartoviaAnim;
      return c;
    }
    switch (name) {
      case 'load': return camera();
      case 'error': {
        var err = (e && e.error) || {};
        return { message: err.message || String(err) || 'Unknown error', status: err.status || null, url: err.url || null, sourceId: (e && e.sourceId) || null };
      }
      case 'styleimagemissing': return { id: e && e.id };
      case 'data': case 'dataloading': case 'sourcedata': case 'sourcedataloading': case 'styledata': case 'styledataloading':
        return {
          dataType: e && e.dataType, sourceId: (e && e.sourceId) || null,
          isSourceLoaded: e && typeof e.isSourceLoaded === 'boolean' ? e.isSourceLoaded : null,
          sourceDataType: (e && e.sourceDataType) || null,
          tile: e && e.tile && e.tile.tileID ? ser(e.tile.tileID.canonical) : null
        };
      default: return {};
    }
  }

  function subscribe(sub) {
    if (state.subs.has(sub.sid)) return;
    var name = sub.name;
    var handler = function (e) { emit(name, payload(name, e), { sid: sub.sid, layer: sub.layer }); };
    if (HIGH_FREQUENCY[name]) handler = throttle(handler, state.options.eventThrottleMs || 16);
    if (sub.layer) state.map.on(name, sub.layer, handler); else state.map.on(name, handler);
    state.subs.set(sub.sid, { sub: sub, handler: handler });
  }

  function unsubscribe(sid) {
    var s = state.subs.get(sid);
    if (!s) return;
    if (s.sub.layer) state.map.off(s.sub.name, s.sub.layer, s.handler); else state.map.off(s.sub.name, s.handler);
    state.subs.delete(sid);
  }

  // ------------------------------------------------------------------ style helpers

  function styleReady() {
    var m = state.map;
    if (m.style && m.style._loaded) return Promise.resolve();
    return new Promise(function (resolve) { m.once('style.load', function () { resolve(); }); });
  }

  var STYLE_MUTATORS = {
    addSource: 1, removeSource: 1, addLayer: 1, removeLayer: 1, moveLayer: 1, setPaintProperty: 1, setLayoutProperty: 1,
    setFilter: 1, setLayerZoomRange: 1, addImage: 1, removeImage: 1, updateImage: 1, setTerrain: 1, setFog: 1, setLight: 1,
    setLights: 1, setSky: 1, setSnow: 1, setRain: 1, setFeatureState: 1, removeFeatureState: 1, getFeatureState: 1,
    setConfigProperty: 1, setConfig: 1, addImport: 1, removeImport: 1, setProjection: 1, querySourceFeatures: 1,
    getLayer: 1, getSource: 1, getPaintProperty: 1, getLayoutProperty: 1, getFilter: 1, hasImage: 1, getConfigProperty: 1,
    setGlobalStateProperty: 1, setColorTheme: 1, addModel: 1, removeModel: 1
  };

  function nextNonUserLayer(layers, index) {
    for (var i = index + 1; i < layers.length; i++) {
      if (!state.userLayers.has(layers[i].id)) return layers[i].id;
    }
    return undefined;
  }

  function setStyleWithPreservation(style, options) {
    var m = state.map;
    var snapshot = null;
    if (state.options.preserveUserLayers && m.style && m.style._loaded) {
      var current = m.getStyle();
      var layers = [];
      (current.layers || []).forEach(function (l, i) {
        if (state.userLayers.has(l.id)) layers.push({ spec: l, before: nextNonUserLayer(current.layers, i) });
      });
      snapshot = {
        sources: Array.from(state.userSources.entries()),
        layers: layers,
        images: Array.from(state.userImages.entries()),
        terrain: current.terrain || null,
        fog: current.fog || null
      };
    }
    m.setStyle(style, options || undefined);
    if (snapshot) {
      m.once('style.load', function () {
        snapshot.sources.forEach(function (kv) {
          try { if (!m.getSource(kv[0])) m.addSource(kv[0], kv[1]); } catch (e) { log('warn', 'Unable to restore source ' + kv[0] + ': ' + e.message); }
        });
        snapshot.images.forEach(function (kv) {
          try { if (!m.hasImage(kv[0])) m.addImage(kv[0], kv[1].image, kv[1].options); } catch (e) { log('warn', 'Unable to restore image ' + kv[0] + ': ' + e.message); }
        });
        snapshot.layers.forEach(function (l) {
          try {
            if (m.getLayer(l.spec.id)) return;
            var before = l.before && m.getLayer(l.before) ? l.before : undefined;
            m.addLayer(l.spec, before);
          } catch (e) { log('warn', 'Unable to restore layer ' + l.spec.id + ': ' + e.message); }
        });
        try { if (snapshot.terrain && m.setTerrain && !m.getStyle().terrain) m.setTerrain(snapshot.terrain); } catch (e) { /* ignore */ }
        emit('cartovia.stylerestored', { layers: snapshot.layers.length, sources: snapshot.sources.length });
      });
    }
  }

  var INTERCEPTED = {
    addSource: function (id, spec) {
      state.map.addSource(id, spec);
      // Keep the spec only when it is needed to restore the source after a style change.
      state.userSources.set(id, state.options.preserveUserLayers ? JSON.parse(JSON.stringify(spec)) : { type: spec.type });
    },
    removeSource: function (id) {
      state.map.removeSource(id);
      state.userSources.delete(id);
    },
    addLayer: function (spec, before) {
      state.map.addLayer(spec, before || undefined);
      state.userLayers.add(spec.id);
    },
    removeLayer: function (id) {
      state.map.removeLayer(id);
      state.userLayers.delete(id);
    },
    removeImage: function (id) {
      state.map.removeImage(id);
      state.userImages.delete(id);
    },
    setStyle: function (style, options) { setStyleWithPreservation(style, options); }
  };

  function loadImageElement(url, width, height) {
    return new Promise(function (resolve, reject) {
      var img = new Image(width || undefined, height || undefined);
      img.crossOrigin = 'anonymous';
      img.onload = function () { resolve(img); };
      img.onerror = function () { reject(new Error('Unable to load image ' + (url.length > 80 ? url.substring(0, 80) + '…' : url))); };
      img.src = url;
    });
  }

  function imageToData(img, width, height) {
    // Rasterizes the image (needed for SVG and to keep a copy for style restoration).
    var w = width || img.naturalWidth || img.width, h = height || img.naturalHeight || img.height;
    var canvas = document.createElement('canvas');
    canvas.width = w; canvas.height = h;
    var ctx = canvas.getContext('2d');
    ctx.drawImage(img, 0, 0, w, h);
    var data = ctx.getImageData(0, 0, w, h);
    return { width: w, height: h, data: new Uint8Array(data.data.buffer) };
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function applyTemplate(template, props) {
    return template.replace(/\{\{\{\s*([^}\s]+)\s*\}\}\}|\{\s*([^}\s]+)\s*\}/g, function (_, raw, escaped) {
      var key = raw || escaped;
      var v = props && props[key];
      if (v === undefined || v === null) return '';
      return raw ? String(v) : escapeHtml(v);
    });
  }

  function cbOrPromise(fn, self, args) {
    return new Promise(function (resolve, reject) {
      var settled = false;
      var r = fn.apply(self, args.concat([function (err, val) {
        if (settled) return; settled = true;
        if (err) reject(err); else resolve(val);
      }]));
      if (r && typeof r.then === 'function') {
        r.then(function (v) { if (!settled) { settled = true; resolve(v); } }, function (e) { if (!settled) { settled = true; reject(e); } });
      }
    });
  }

  // ------------------------------------------------------------------ controls

  function htmlControl(id, opts) {
    var container;
    return {
      onAdd: function () {
        container = document.createElement('div');
        container.className = 'mapboxgl-ctrl maplibregl-ctrl cartovia-html-control ' + (opts.group === false ? '' : 'mapboxgl-ctrl-group maplibregl-ctrl-group ') + (opts.className || '');
        container.innerHTML = opts.html || '';
        container.addEventListener('click', function (e) {
          var el = e.target && e.target.closest ? e.target.closest('[data-command]') : null;
          if (el) { e.stopPropagation(); emit('control.command', { command: el.getAttribute('data-command'), value: el.value || null }, { target: id }); }
        });
        container.addEventListener('change', function (e) {
          var el = e.target;
          if (el && el.getAttribute && el.getAttribute('data-command')) {
            emit('control.command', { command: el.getAttribute('data-command'), value: el.type === 'checkbox' ? String(el.checked) : el.value }, { target: id });
          }
        });
        return container;
      },
      onRemove: function () { if (container && container.parentNode) container.parentNode.removeChild(container); },
      setHtml: function (html) { if (container) container.innerHTML = html; }
    };
  }

  function createControl(id, kind, options) {
    var L = state.lib, o = options || {};
    switch (kind) {
      case 'navigation': return new L.NavigationControl(o);
      case 'scale': return new L.ScaleControl(o);
      case 'fullscreen': return new L.FullscreenControl(o);
      case 'attribution': return new L.AttributionControl(o);
      case 'geolocate': {
        var g = new L.GeolocateControl(o);
        g.on('geolocate', function (p) {
          emit('geolocate', { lngLat: [p.coords.longitude, p.coords.latitude], accuracy: p.coords.accuracy, heading: p.coords.heading, altitude: p.coords.altitude }, { target: id });
        });
        g.on('error', function (err) { emit('geolocate.error', { message: (err && err.message) || 'Geolocation error', code: err && err.code }, { target: id }); });
        return g;
      }
      case 'terrain':
        if (!L.TerrainControl) throw new Error('TerrainControl is only available with MapLibre GL JS.');
        return new L.TerrainControl(o);
      case 'globe':
        if (!L.GlobeControl) throw new Error('GlobeControl is only available with MapLibre GL JS.');
        return new L.GlobeControl(o);
      case 'logo':
        if (!L.LogoControl) throw new Error('LogoControl is only available with MapLibre GL JS.');
        return new L.LogoControl(o);
      case 'html': return htmlControl(id, o);
      default: throw new Error('Unknown control kind: ' + kind);
    }
  }

  function addControl(c) {
    var ctrl = createControl(c.id, c.kind, c.options);
    state.map.addControl(ctrl, c.position || undefined);
    state.controls.set(c.id, ctrl);
  }

  // ------------------------------------------------------------------ markers & popups

  function buildPopup(p, id) {
    if (!p) return null;
    var popup = new state.lib.Popup(p.options || {});
    if (p.html !== undefined && p.html !== null) popup.setHTML(p.html);
    else if (p.text !== undefined && p.text !== null) popup.setText(p.text);
    if (p.lngLat) popup.setLngLat(p.lngLat);
    if (id) {
      popup.on('open', function () { emit('popup.open', {}, { target: id }); });
      popup.on('close', function () { emit('popup.close', {}, { target: id }); });
    }
    return popup;
  }

  function markerPos(mk) { var p = mk.getLngLat(); return { lngLat: [p.lng, p.lat] }; }

  // ------------------------------------------------------------------ commands

  var commands = {
    'map.invoke': async function (a) {
      var m = state.map, method = a.method, args = a.args || [];
      if (STYLE_MUTATORS[method]) await styleReady();
      if (INTERCEPTED[method]) return ser(INTERCEPTED[method].apply(null, args));
      var fn = m[method];
      if (typeof fn !== 'function') throw new Error("The map has no method '" + method + "' (" + state.engine + ').');
      var r = fn.apply(m, args);
      if (r && typeof r.then === 'function') r = await r;
      return ser(r);
    },

    'map.animate': function (a) {
      var m = state.map, id = ++state.animId;
      var opts = a.options || {};
      return new Promise(function (resolve) {
        var done = false;
        var finish = function () { if (done) return; done = true; m.off('moveend', onEnd); resolve(camera()); };
        var onEnd = function (e) { if (!e || e.cartoviaAnim === id) finish(); };
        if (a.wait) m.on('moveend', onEnd);
        if (a.method === 'fitBounds') m.fitBounds(a.bounds, opts, { cartoviaAnim: id });
        else m[a.method](opts, { cartoviaAnim: id });
        if (!a.wait) { resolve(camera()); return; }
        var duration = typeof opts.duration === 'number' ? opts.duration : 15000;
        setTimeout(finish, duration + 3000);
      });
    },

    'map.camera': function () { return camera(); },

    'map.handler': function (a) {
      var h = state.map[a.name];
      if (!h || typeof h.enable !== 'function') throw new Error("Unknown interaction handler '" + a.name + "'.");
      if (a.enabled) h.enable(a.options || undefined); else h.disable();
      return h.isEnabled();
    },

    'map.eval': async function (a) {
      var fn = new Function('map', 'lib', 'Cartovia', 'args', a.code);
      var r = fn(state.map, state.lib, window.Cartovia, a.args || null);
      if (r && typeof r.then === 'function') r = await r;
      return ser(r);
    },

    'map.projection': async function (a) {
      await styleReady();
      var m = state.map;
      if (state.engine === 'maplibre') {
        m.setProjection(a.projection ? { type: a.projection.name } : { type: 'mercator' });
      } else {
        m.setProjection(a.projection || null);
      }
      return null;
    },

    'map.addImage': async function (a) {
      await styleReady();
      var img = await loadImageElement(a.url, a.width, a.height);
      var data = imageToData(img, a.width, a.height);
      var opts = a.options || {};
      var m = state.map;
      if (m.hasImage(a.id)) {
        if (a.replace === false) return false;
        m.removeImage(a.id);
      }
      m.addImage(a.id, data, opts);
      state.userImages.set(a.id, { image: data, options: opts });
      return true;
    },

    'map.layerIds': function () {
      var style = state.map.getStyle();
      return (style && style.layers ? style.layers : []).map(function (l) { return l.id; });
    },

    'map.userLayer': function (a) {
      return { layers: Array.from(state.userLayers), sources: Array.from(state.userSources.keys()) };
    },

    'source.invoke': async function (a) {
      await styleReady();
      var src = state.map.getSource(a.id);
      if (!src) throw new Error("Source '" + a.id + "' not found.");
      var fn = src[a.method];
      if (typeof fn !== 'function') throw new Error("Source '" + a.id + "' has no method '" + a.method + "'.");
      var r = fn.apply(src, a.args || []);
      if (r && typeof r.then === 'function') r = await r;
      if (a.method === 'setData' && state.userSources.has(a.id)) {
        state.userSources.get(a.id).data = a.args[0];
      }
      return r === src ? null : ser(r);
    },

    'source.get': async function (a) {
      await styleReady();
      var src = state.map.getSource(a.id);
      if (!src) return null;
      var spec = typeof src.serialize === 'function' ? src.serialize() : { type: src.type };
      return ser(spec);
    },

    'source.cluster': async function (a) {
      await styleReady();
      var src = state.map.getSource(a.id);
      if (!src) throw new Error("Source '" + a.id + "' not found.");
      switch (a.op) {
        case 'expansionZoom': return await cbOrPromise(src.getClusterExpansionZoom, src, [a.clusterId]);
        case 'children': return (await cbOrPromise(src.getClusterChildren, src, [a.clusterId]) || []).map(featureJson);
        case 'leaves': return (await cbOrPromise(src.getClusterLeaves, src, [a.clusterId, a.limit || 10, a.offset || 0]) || []).map(featureJson);
        default: throw new Error('Unknown cluster operation ' + a.op);
      }
    },

    'query.rendered': async function (a) {
      var m = state.map;
      var geometry = a.geometry === undefined || a.geometry === null ? undefined : a.geometry;
      var opts = a.options || {};
      var features = geometry === undefined ? m.queryRenderedFeatures(opts) : m.queryRenderedFeatures(geometry, opts);
      return features.map(featureJson);
    },

    'query.source': async function (a) {
      await styleReady();
      return state.map.querySourceFeatures(a.source, a.options || {}).map(featureJson);
    },

    'query.elevation': function (a) {
      var m = state.map;
      if (typeof m.queryTerrainElevation !== 'function') return null;
      var v = m.queryTerrainElevation(a.lngLat, a.options || undefined);
      return v === undefined || v === null || !isFinite(v) ? null : v;
    },

    'marker.add': function (a) {
      var opts = Object.assign({}, a.options || {});
      if (a.html !== undefined && a.html !== null) {
        var el = document.createElement('div');
        el.innerHTML = a.html;
        if (opts.className) { el.className = opts.className; delete opts.className; }
        opts.element = el;
      }
      var mk = new state.lib.Marker(opts).setLngLat(a.lngLat);
      if (a.popup) mk.setPopup(buildPopup(a.popup, null));
      mk.addTo(state.map);
      var id = a.id;
      var root = mk.getElement();
      if (a.cursor) root.style.cursor = a.cursor;
      if (a.tooltip) root.title = a.tooltip;
      root.addEventListener('click', function () { emit('marker.click', markerPos(mk), { target: id }); });
      root.addEventListener('dblclick', function (e) { e.stopPropagation(); emit('marker.dblclick', markerPos(mk), { target: id }); });
      root.addEventListener('contextmenu', function (e) { e.preventDefault(); emit('marker.contextmenu', markerPos(mk), { target: id }); });
      root.addEventListener('mouseenter', function () { emit('marker.mouseenter', markerPos(mk), { target: id }); });
      root.addEventListener('mouseleave', function () { emit('marker.mouseleave', markerPos(mk), { target: id }); });
      mk.on('dragstart', function () { emit('marker.dragstart', markerPos(mk), { target: id }); });
      mk.on('drag', throttle(function () { emit('marker.drag', markerPos(mk), { target: id }); }, state.options.eventThrottleMs || 16));
      mk.on('dragend', function () { emit('marker.dragend', markerPos(mk), { target: id }); });
      state.markers.set(id, mk);
      return null;
    },

    'marker.invoke': async function (a) {
      var mk = state.markers.get(a.id);
      if (!mk) throw new Error("Marker '" + a.id + "' not found.");
      var fn = mk[a.method];
      if (typeof fn !== 'function') throw new Error("Markers have no method '" + a.method + "'.");
      var r = fn.apply(mk, a.args || []);
      return r === mk ? null : ser(r);
    },

    'marker.update': function (a) {
      var mk = state.markers.get(a.id);
      if (!mk) throw new Error("Marker '" + a.id + "' not found.");
      if (a.html !== undefined && a.html !== null) {
        var el = mk.getElement();
        var target = el.firstElementChild && el.childElementCount === 1 && el.firstElementChild.tagName === 'svg' ? null : el;
        if (target) target.innerHTML = a.html;
      }
      if (a.popup !== undefined) mk.setPopup(a.popup ? buildPopup(a.popup, null) : null);
      if (a.tooltip !== undefined) mk.getElement().title = a.tooltip || '';
      return null;
    },

    'marker.remove': function (a) {
      var mk = state.markers.get(a.id);
      if (mk) { mk.remove(); state.markers.delete(a.id); }
      return null;
    },

    'popup.add': function (a) {
      var popup = buildPopup(a, a.id);
      if (a.trackPointer) popup.trackPointer();
      popup.addTo(state.map);
      state.popups.set(a.id, popup);
      return null;
    },

    'popup.invoke': function (a) {
      var p = state.popups.get(a.id);
      if (!p) throw new Error("Popup '" + a.id + "' not found.");
      var fn = p[a.method];
      if (typeof fn !== 'function') throw new Error("Popups have no method '" + a.method + "'.");
      var r = fn.apply(p, a.args || []);
      return r === p ? null : ser(r);
    },

    'popup.remove': function (a) {
      var p = state.popups.get(a.id);
      if (p) { p.remove(); state.popups.delete(a.id); }
      return null;
    },

    'control.add': function (a) { addControl(a); return null; },

    'control.update': function (a) {
      var c = state.controls.get(a.id);
      if (c && typeof c.setHtml === 'function' && a.html !== undefined) c.setHtml(a.html);
      if (c && a.method && typeof c[a.method] === 'function') return ser(c[a.method].apply(c, a.args || []));
      return null;
    },

    'control.remove': function (a) {
      var c = state.controls.get(a.id);
      if (c) { state.map.removeControl(c); state.controls.delete(a.id); }
      return null;
    },

    'events.on': function (a) { subscribe(a); return null; },
    'events.off': function (a) { unsubscribe(a.sid); return null; },

    'helper.hoverCursor': function (a) {
      var m = state.map, cursor = a.cursor || 'pointer';
      var enter = function () { m.getCanvas().style.cursor = cursor; };
      var leave = function () { m.getCanvas().style.cursor = ''; };
      m.on('mouseenter', a.layer, enter);
      m.on('mouseleave', a.layer, leave);
      state.helpers.set(a.sid, function () { m.off('mouseenter', a.layer, enter); m.off('mouseleave', a.layer, leave); leave(); });
      return null;
    },

    'helper.hoverState': function (a) {
      var m = state.map, key = a.stateKey || 'hover', hovered = null;
      var target = function (id) { var t = { source: a.source, id: id }; if (a.sourceLayer) t.sourceLayer = a.sourceLayer; return t; };
      var set = function (id, v) { var s = {}; s[key] = v; try { m.setFeatureState(target(id), s); } catch (e) { /* source removed */ } };
      var move = function (e) {
        if (!e.features || !e.features.length) return;
        var id = e.features[0].id;
        if (id === undefined || id === null) return;
        if (hovered !== null && hovered !== id) set(hovered, false);
        hovered = id; set(id, true);
      };
      var leave = function () { if (hovered !== null) set(hovered, false); hovered = null; };
      m.on('mousemove', a.layer, move);
      m.on('mouseleave', a.layer, leave);
      state.helpers.set(a.sid, function () { m.off('mousemove', a.layer, move); m.off('mouseleave', a.layer, leave); leave(); });
      return null;
    },

    'helper.selectState': function (a) {
      var m = state.map, key = a.stateKey || 'selected', selected = null;
      var target = function (id) { var t = { source: a.source, id: id }; if (a.sourceLayer) t.sourceLayer = a.sourceLayer; return t; };
      var set = function (id, v) { var s = {}; s[key] = v; try { m.setFeatureState(target(id), s); } catch (e) { /* ignore */ } };
      var click = function (e) {
        if (!e.features || !e.features.length) return;
        var id = e.features[0].id;
        if (id === undefined || id === null) return;
        if (selected !== null) set(selected, false);
        if (selected === id && a.toggle !== false) { selected = null; return; }
        selected = id; set(id, true);
      };
      m.on('click', a.layer, click);
      state.helpers.set(a.sid, function () { m.off('click', a.layer, click); if (selected !== null) set(selected, false); });
      return null;
    },

    'helper.clickPopup': function (a) {
      var m = state.map;
      var click = function (e) {
        if (!e.features || !e.features.length) return;
        var f = e.features[0];
        var at = f.geometry && f.geometry.type === 'Point' ? f.geometry.coordinates.slice() : [e.lngLat.lng, e.lngLat.lat];
        if (f.geometry && f.geometry.type === 'Point') {
          while (Math.abs(e.lngLat.lng - at[0]) > 180) at[0] += e.lngLat.lng > at[0] ? 360 : -360;
        }
        new state.lib.Popup(a.options || {}).setLngLat(at).setHTML(applyTemplate(a.template, f.properties)).addTo(m);
      };
      m.on('click', a.layer, click);
      state.helpers.set(a.sid, function () { m.off('click', a.layer, click); });
      return null;
    },

    'helper.clusterZoom': function (a) {
      var m = state.map;
      var click = async function (e) {
        if (!e.features || !e.features.length) return;
        var f = e.features[0];
        var src = m.getSource(a.source);
        if (!src) return;
        try {
          var zoom = await cbOrPromise(src.getClusterExpansionZoom, src, [f.properties.cluster_id]);
          m.easeTo({ center: f.geometry.coordinates, zoom: zoom });
        } catch (err) { log('warn', 'Cluster expansion failed: ' + err.message); }
      };
      m.on('click', a.layer, click);
      state.helpers.set(a.sid, function () { m.off('click', a.layer, click); });
      return null;
    },

    'helper.remove': function (a) {
      var dispose = state.helpers.get(a.sid);
      if (dispose) { dispose(); state.helpers.delete(a.sid); }
      return null;
    },

    'interaction.add': function (a) {
      var m = state.map;
      if (typeof m.addInteraction !== 'function') throw new Error('Interactions require Mapbox GL JS v3.9 or later.');
      var def = { type: a.type, handler: function (e) {
        emit('interaction', {
          type: a.type,
          feature: e.feature ? featureJson(e.feature) : null,
          lngLat: e.lngLat ? [e.lngLat.lng, e.lngLat.lat] : null,
          point: e.point ? [e.point.x, e.point.y] : null
        }, { target: a.id });
        return a.stopPropagation !== false;
      } };
      if (a.target) def.target = a.target;
      if (a.filter) def.filter = a.filter;
      m.addInteraction(a.id, def);
      state.interactions.add(a.id);
      return null;
    },

    'interaction.remove': function (a) {
      if (state.interactions.has(a.id)) { state.map.removeInteraction(a.id); state.interactions.delete(a.id); }
      return null;
    },

    'plugin.create': async function (a) {
      for (var i = 0; i < (a.stylesheets || []).length; i++) loadCss(a.stylesheets[i]);
      for (var j = 0; j < (a.scripts || []).length; j++) await loadScript(a.scripts[j]);
      if (a.source && !state.pluginFactories.has(a.name)) {
        (new Function('Cartovia', a.source))(window.Cartovia);
      }
      var factory = state.pluginFactories.get(a.name);
      if (!factory) throw new Error("Plugin '" + a.name + "' is not defined.");
      var instanceId = a.instanceId;
      var ctx = {
        map: state.map, lib: state.lib, engine: state.engine, accessToken: state.options.accessToken,
        instanceId: instanceId,
        emit: function (event, data) { emit('plugin', { event: event, data: data === undefined ? null : ser(data) }, { target: instanceId }); },
        callHost: function (method, data) { return callHost('plugin:' + instanceId + ':' + method, data); },
        ser: ser, featureJson: featureJson, log: log, loadScript: loadScript, loadCss: loadCss
      };
      var instance = await factory(ctx, a.options || {});
      state.plugins.set(instanceId, instance || {});
      return instance && instance.info ? ser(instance.info) : null;
    },

    'plugin.invoke': async function (a) {
      var inst = state.plugins.get(a.instanceId);
      if (!inst) throw new Error('Plugin instance not found.');
      var fn = inst.commands && inst.commands[a.command];
      if (typeof fn !== 'function') throw new Error("Unknown plugin command '" + a.command + "'.");
      var r = fn.apply(inst, a.args || []);
      if (r && typeof r.then === 'function') r = await r;
      return ser(r);
    },

    'plugin.dispose': function (a) {
      var inst = state.plugins.get(a.instanceId);
      if (inst && typeof inst.dispose === 'function') { try { inst.dispose(); } catch (e) { log('warn', e.message); } }
      state.plugins.delete(a.instanceId);
      return null;
    },

    'assets.load': async function (a) {
      for (var i = 0; i < (a.stylesheets || []).length; i++) await loadCss(a.stylesheets[i]);
      for (var j = 0; j < (a.scripts || []).length; j++) await loadScript(a.scripts[j], a.module);
      return null;
    }
  };

  // ------------------------------------------------------------------ lifecycle

  function destroy() {
    state.plugins.forEach(function (p) { try { if (p.dispose) p.dispose(); } catch (e) { /* ignore */ } });
    state.plugins.clear();
    state.markers.forEach(function (m) { m.remove(); }); state.markers.clear();
    state.popups.forEach(function (p) { p.remove(); }); state.popups.clear();
    state.controls.clear(); state.subs.clear(); state.helpers.clear(); state.interactions.clear();
    state.userSources.clear(); state.userLayers.clear(); state.userImages.clear();
    if (state.map) { try { state.map.remove(); } catch (e) { /* ignore */ } }
    state.map = null;
    var fatalEl = document.querySelector('.cartovia-fatal');
    if (fatalEl) fatalEl.remove();
  }

  async function init(opts) {
    destroy();
    state.options = opts;
    state.engine = opts.engine;
    try {
      (opts.stylesheets || []).forEach(loadCss);
      await loadScript(opts.engineScript, opts.engineScriptIsModule);
      for (var i = 0; i < (opts.scripts || []).length; i++) await loadScript(opts.scripts[i]);
    } catch (err) {
      fatal('Unable to load the map engine: ' + err.message + '. Check the network connection or the asset URLs.');
      return;
    }

    var lib = opts.engine === 'maplibre' ? window.maplibregl : window.mapboxgl;
    if (!lib) { fatal('The ' + opts.engine + ' library was not found after loading ' + opts.engineScript + '.'); return; }
    state.lib = lib;

    if (opts.engine === 'mapbox') {
      if (!opts.accessToken) { fatal('A Mapbox access token is required (MapOptions.AccessToken).'); return; }
      lib.accessToken = opts.accessToken;
    }
    if (opts.workerCount && lib.setWorkerCount) lib.setWorkerCount(opts.workerCount);
    if (opts.rtlTextPluginUrl && lib.setRTLTextPlugin) {
      try { var r = lib.setRTLTextPlugin(opts.rtlTextPluginUrl, null, true); if (r && r.catch) r.catch(function () { }); } catch (e) { /* already set */ }
    }

    var mapOptions = Object.assign({}, opts.mapOptions || {}, { container: 'map' });
    var map;
    try {
      map = new lib.Map(mapOptions);
    } catch (err) {
      var webgl = typeof lib.supported === 'function' && !lib.supported() ? ' WebGL is not available on this machine.' : '';
      fatal('Unable to create the map: ' + err.message + webgl);
      return;
    }
    state.map = map;
    window.cartoviaMap = map;

    if (opts.projection && opts.engine === 'maplibre') {
      map.on('style.load', function () {
        try { map.setProjection({ type: opts.projection }); } catch (e) { log('warn', 'Projection: ' + e.message); }
      });
    }

    (opts.subscriptions || []).forEach(subscribe);
    (opts.controls || []).forEach(function (c) { try { addControl(c); } catch (e) { log('error', e.message); } });

    var resizeObserver = new ResizeObserver(function () { if (state.map === map) map.resize(); });
    resizeObserver.observe(document.getElementById('map'));

    post({ t: 'created', engine: opts.engine, version: lib.version || (typeof lib.getVersion === 'function' ? lib.getVersion() : null) });
  }

  // ------------------------------------------------------------------ public JS API for plugins

  window.Cartovia = {
    definePlugin: function (name, factory) { state.pluginFactories.set(name, factory); },
    get map() { return state.map; },
    get lib() { return state.lib; },
    get engine() { return state.engine; },
    emit: emit, callHost: callHost, ser: ser, featureJson: featureJson, loadScript: loadScript, loadCss: loadCss, log: log
  };

  // ------------------------------------------------------------------ dispatcher

  async function onMessage(raw) {
    var msg;
    try { msg = typeof raw === 'string' ? JSON.parse(raw) : raw; } catch (e) { return; }
    if (!msg || !msg.t) return;

    if (msg.t === 'init') { await init(msg.options || {}); return; }

    if (msg.t === 'hostResult') {
      var pending = state.hostCalls.get(msg.id);
      if (pending) {
        state.hostCalls.delete(msg.id);
        if (msg.ok) pending.resolve(msg.value); else pending.reject(new Error((msg.error && msg.error.message) || 'Host call failed'));
      }
      return;
    }

    if (msg.t === 'call') {
      try {
        var handler = commands[msg.cmd];
        if (!handler) throw new Error("Unknown command '" + msg.cmd + "'.");
        if (!state.map && msg.cmd !== 'assets.load') throw new Error('The map is not initialized.');
        var value = await handler(msg.args || {});
        post({ t: 'result', id: msg.id, ok: true, value: value === undefined ? null : value });
      } catch (err) {
        post({ t: 'result', id: msg.id, ok: false, error: { message: (err && err.message) || String(err), stack: err && err.stack } });
      }
    }
  }

  if (webview) {
    webview.addEventListener('message', function (e) { onMessage(e.data); });
  }
  window.cartoviaReceive = onMessage; // for hosts without chrome.webview (tests, other engines)

  post({ t: 'ready', userAgent: navigator.userAgent });
})();
