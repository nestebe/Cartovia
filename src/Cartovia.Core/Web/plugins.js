/*
 * Built-in Cartovia plugins. Each plugin is a factory registered with Cartovia.definePlugin(name, factory).
 * factory(ctx, options) returns { commands: { name: fn }, dispose: fn, info?: object }.
 * ctx = { map, lib, engine, accessToken, instanceId, emit(event, data), callHost(method, data), ser, featureJson, log }
 */
(function () {
  'use strict';

  function on(map, list) {
    list.forEach(function (x) { map.on(x[0], x[1]); });
    return function () { list.forEach(function (x) { map.off(x[0], x[1]); }); };
  }

  // ------------------------------------------------------------------ Draw (mapbox-gl-draw)
  Cartovia.definePlugin('draw', function (ctx, o) {
    var Draw = window.MapboxDraw;
    if (!Draw) throw new Error('mapbox-gl-draw is not loaded.');
    if (ctx.engine === 'maplibre' && Draw.constants && Draw.constants.classes) {
      Draw.constants.classes.CONTROL_BASE = 'maplibregl-ctrl';
      Draw.constants.classes.CONTROL_PREFIX = 'maplibregl-ctrl-';
      Draw.constants.classes.CONTROL_GROUP = 'maplibregl-ctrl-group';
    }
    var draw = new Draw(o.drawOptions || {});
    ctx.map.addControl(draw, o.position || undefined);

    var forward = function (name) {
      return [name, function (e) {
        ctx.emit(name.substring(5), {
          features: e.features ? e.features.map(function (f) { return ctx.ser(f); }) : null,
          mode: e.mode || null, action: e.action || null, points: e.points ? e.points.map(function (f) { return ctx.ser(f); }) : null
        });
      }];
    };
    var off = on(ctx.map, ['draw.create', 'draw.delete', 'draw.update', 'draw.selectionchange', 'draw.modechange', 'draw.combine', 'draw.uncombine'].map(forward));

    return {
      commands: {
        getAll: function () { return draw.getAll(); },
        get: function (id) { return draw.get(id) || null; },
        add: function (geojson) { return draw.add(geojson); },
        set: function (fc) { return draw.set(fc); },
        delete: function (ids) { draw.delete(ids); },
        deleteAll: function () { draw.deleteAll(); },
        getSelected: function () { return draw.getSelected(); },
        getSelectedIds: function () { return draw.getSelectedIds(); },
        getSelectedPoints: function () { return draw.getSelectedPoints(); },
        getFeatureIdsAt: function (pt) { return draw.getFeatureIdsAt({ x: pt[0], y: pt[1] }); },
        changeMode: function (mode, options) { draw.changeMode(mode, options || undefined); },
        getMode: function () { return draw.getMode(); },
        trash: function () { draw.trash(); },
        combineFeatures: function () { draw.combineFeatures(); },
        uncombineFeatures: function () { draw.uncombineFeatures(); },
        setFeatureProperty: function (id, prop, value) { draw.setFeatureProperty(id, prop, value); }
      },
      dispose: function () { off(); ctx.map.removeControl(draw); }
    };
  });

  // ------------------------------------------------------------------ Geocoder
  function nominatim(config, o) {
    var params = new URLSearchParams({ q: config.query, format: 'geojson', addressdetails: '1', limit: String(config.limit || 5) });
    if (config.language) params.set('accept-language', Array.isArray(config.language) ? config.language.join(',') : config.language);
    if (config.countries) params.set('countrycodes', Array.isArray(config.countries) ? config.countries.join(',') : config.countries);
    if (config.bbox) params.set('viewbox', config.bbox.join(','));
    var url = (o.nominatimUrl || 'https://nominatim.openstreetmap.org/search') + '?' + params.toString();
    return fetch(url).then(function (r) { return r.json(); }).then(function (g) {
      return {
        type: 'FeatureCollection',
        features: (g.features || []).map(function (f) {
          var c = f.bbox ? [(f.bbox[0] + f.bbox[2]) / 2, (f.bbox[1] + f.bbox[3]) / 2] : f.geometry.coordinates;
          return {
            type: 'Feature', geometry: { type: 'Point', coordinates: c },
            place_name: f.properties.display_name, text: f.properties.name || f.properties.display_name,
            properties: f.properties, place_type: [f.properties.type || 'place'], center: c, bbox: f.bbox
          };
        })
      };
    });
  }

  Cartovia.definePlugin('geocoder', function (ctx, o) {
    var geocoder;
    var opts = Object.assign({}, o.geocoderOptions || {});
    if (ctx.engine === 'mapbox' && !o.useHostProvider) {
      if (!window.MapboxGeocoder) throw new Error('mapbox-gl-geocoder is not loaded.');
      opts.accessToken = opts.accessToken || ctx.accessToken;
      opts.mapboxgl = ctx.lib;
      geocoder = new window.MapboxGeocoder(opts);
    } else {
      var G = window.MaplibreGeocoder;
      if (!G) throw new Error('maplibre-gl-geocoder is not loaded.');
      var api = {
        forwardGeocode: function (config) {
          if (o.useHostProvider) {
            return ctx.callHost('forward', {
              query: config.query, limit: config.limit || null, language: config.language || null,
              bbox: config.bbox || null, proximity: config.proximity || null, countries: config.countries || null
            });
          }
          return nominatim(config, o);
        }
      };
      if (o.useHostProvider && o.reverse) {
        api.reverseGeocode = function (config) {
          return ctx.callHost('reverse', { query: config.query, limit: config.limit || null, language: config.language || null });
        };
      }
      opts.maplibregl = ctx.lib;
      geocoder = new G(api, opts);
    }
    ctx.map.addControl(geocoder, o.position || undefined);

    geocoder.on('result', function (e) { ctx.emit('result', { result: ctx.ser(e.result) }); });
    geocoder.on('results', function (e) { ctx.emit('results', { features: ctx.ser((e && e.features) || []) }); });
    geocoder.on('clear', function () { ctx.emit('clear', {}); });
    geocoder.on('error', function (e) { ctx.emit('error', { message: (e && e.error && e.error.message) || 'Geocoding error' }); });

    return {
      commands: {
        query: function (q) { geocoder.query(q); },
        clear: function () { geocoder.clear(); },
        setInput: function (text) { geocoder.setInput(text); },
        setProximity: function (p) { geocoder.setProximity(p ? { longitude: p[0], latitude: p[1] } : null); },
        setLanguage: function (lang) { if (geocoder.setLanguage) geocoder.setLanguage(lang); },
        setPlaceholder: function (text) { if (geocoder.setPlaceholder) geocoder.setPlaceholder(text); },
        setFlyTo: function (v) { if (geocoder.setFlyTo) geocoder.setFlyTo(v); }
      },
      dispose: function () { ctx.map.removeControl(geocoder); }
    };
  });

  // ------------------------------------------------------------------ Directions (Mapbox only)
  Cartovia.definePlugin('directions', function (ctx, o) {
    if (ctx.engine !== 'mapbox') throw new Error('The directions plugin requires the Mapbox engine.');
    if (!window.MapboxDirections) throw new Error('mapbox-gl-directions is not loaded.');
    var opts = Object.assign({ accessToken: ctx.accessToken }, o.directionsOptions || {});
    var d = new window.MapboxDirections(opts);
    ctx.map.addControl(d, o.position || undefined);
    d.on('route', function (e) { ctx.emit('route', { routes: ctx.ser(e.route) }); });
    d.on('origin', function (e) { ctx.emit('origin', { feature: ctx.ser(e.feature) }); });
    d.on('destination', function (e) { ctx.emit('destination', { feature: ctx.ser(e.feature) }); });
    d.on('clear', function (e) { ctx.emit('clear', { type: e && e.type }); });
    d.on('profile', function (e) { ctx.emit('profile', { profile: e && e.profile }); });
    d.on('error', function (e) { ctx.emit('error', { message: (e && e.error && (e.error.message || e.error)) || 'Directions error' }); });
    return {
      commands: {
        setOrigin: function (q) { d.setOrigin(q); },
        setDestination: function (q) { d.setDestination(q); },
        getOrigin: function () { return d.getOrigin(); },
        getDestination: function () { return d.getDestination(); },
        addWaypoint: function (index, q) { d.addWaypoint(index, q); },
        setWaypoint: function (index, q) { d.setWaypoint(index, q); },
        removeWaypoint: function (index) { d.removeWaypoint(index); },
        getWaypoints: function () { return d.getWaypoints(); },
        removeRoutes: function () { d.removeRoutes(); },
        reverse: function () { d.reverse(); },
        interactive: function (v) { d.interactive(v); }
      },
      dispose: function () { ctx.map.removeControl(d); }
    };
  });

  // ------------------------------------------------------------------ Turf
  Cartovia.definePlugin('turf', function (ctx) {
    if (!window.turf) throw new Error('turf.js is not loaded.');
    return {
      commands: {
        call: function (fn) {
          var f = window.turf[fn];
          if (typeof f !== 'function') throw new Error("turf has no function '" + fn + "'.");
          return f.apply(null, Array.prototype.slice.call(arguments, 1));
        }
      }
    };
  });
})();
