// ── app.js ──────────────────────────────────────────────────────────────────
// OpenLayers map with draw-polygon tool and SSE result layers.
// Called from Blazor via JS interop.
// Coordinate strategy: client draws in EPSG:3857, converts to EPSG:4326 (WGS84)
// before sending. Server accepts any SRID via the Srid field in the POST body.
// Server returns result geometry in EPSG:4326 for display.

window.MapApp = (() => {

    const SERVER_URL = 'http://localhost:5080/search';
    const BEARER_TOKEN = 'dev-token';

    let map, drawSource, drawLayer, drawInteraction;
    let resultLayers = [];
    let dotNetRef = null;

    // ── colour palette for result layers ────────────────────────────────────
    const COLOURS = [
        'rgba(255,99,71,0.35)',
        'rgba(30,144,255,0.35)',
        'rgba(50,205,50,0.35)',
        'rgba(255,165,0,0.35)',
        'rgba(148,0,211,0.35)',
    ];
    const STROKE_COLOURS = ['#ff6347', '#1e90ff', '#32cd32', '#ffa500', '#9400d3'];

    function makeStyle(index) {
        const i = index % COLOURS.length;
        return new ol.style.Style({
            fill: new ol.style.Fill({ color: COLOURS[i] }),
            stroke: new ol.style.Stroke({ color: STROKE_COLOURS[i], width: 2 }),
        });
    }

    // ── initialise map ───────────────────────────────────────────────────────
    function initMap(containerId) {
        drawSource = new ol.source.Vector();
        drawLayer = new ol.layer.Vector({
            source: drawSource,
            style: new ol.style.Style({
                fill: new ol.style.Fill({ color: 'rgba(255,255,0,0.15)' }),
                stroke: new ol.style.Stroke({ color: '#ffcc00', width: 2 }),
            })
        });

        map = new ol.Map({
            target: containerId,
            layers: [
                new ol.layer.Tile({ source: new ol.source.OSM() }),
                drawLayer,
            ],
            view: new ol.View({
                center: ol.proj.fromLonLat([18.3, 57.5]),
                zoom: 9,
            }),
        });

        activateDrawTool();
    }

    // ── draw tool ────────────────────────────────────────────────────────────
    function activateDrawTool() {
        if (drawInteraction) map.removeInteraction(drawInteraction);

        drawInteraction = new ol.interaction.Draw({
            source: drawSource,
            type: 'Polygon',
        });

        drawInteraction.on('drawend', async (evt) => {
            drawSource.clear();

            // Map is EPSG:3857 — convert to WGS84 lon/lat (EPSG:4326) for the server
            const coords3857 = evt.feature.getGeometry().getCoordinates();
            const coords4326 = coords3857.map(ring =>
                ring.map(pt => ol.proj.toLonLat(pt))
            );

            const geoJson = JSON.stringify({ type: 'Polygon', coordinates: coords4326 });

            log(`=== GeoJSON sent to server (SRID 4326) ===`);
            log(geoJson);
            log(`==========================================`);

            clearResultLayers();
            await sendPolygon(geoJson, 4326);
        });

        map.addInteraction(drawInteraction);
    }

    // ── clear old result layers ──────────────────────────────────────────────
    function clearResultLayers() {
        resultLayers.forEach(l => map.removeLayer(l));
        resultLayers = [];
    }

    // ── send polygon and read SSE ────────────────────────────────────────────
    async function sendPolygon(geoJson, srid = 4326) {
        const body = JSON.stringify({ GeoJson: geoJson, Srid: srid });
        let response;
        try {
            response = await fetch(SERVER_URL, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${BEARER_TOKEN}`,
                    'Accept': 'text/event-stream',
                },
                body,
            });
        } catch (err) {
            log(`ERROR: Cannot reach server — ${err.message}`);
            return;
        }

        if (!response.ok) {
            log(`ERROR: Server returned ${response.status}`);
            return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop();

            for (const line of lines) {
                const trimmed = line.trim();
                if (!trimmed) continue;
                if (trimmed === 'event: done') { log('Search complete.'); return; }
                if (trimmed.startsWith('data: ')) {
                    handleSseEvent(trimmed.slice('data: '.length));
                } else {
                    log(`[sse] ${trimmed}`);
                }
            }
        }
    }

    // ── handle one SSE data event ────────────────────────────────────────────
    function handleSseEvent(json) {
        let obj;
        try { obj = JSON.parse(json); } catch { log(`[parse error] ${json}`); return; }

        const title = obj.title ?? 'Unknown';
        const fc = obj.features;
        const count = fc?.features?.length ?? 0;

        log(`[${title}] — ${count} feature(s) found`);
        if (count === 0) return;

        const layerIndex = resultLayers.length;
        const olFeatures = [];

        for (const f of fc.features) {
            if (!f.geometry) continue;
            try {
                // Server returns EPSG:4326 (lon/lat) — convert to EPSG:3857 for display
                const coords3857 = toMercator(f.geometry.coordinates, f.geometry.type);
                const geom = buildOlGeometry(f.geometry.type, coords3857);
                if (!geom) continue;
                const olFeature = new ol.Feature({ geometry: geom });
                olFeature.setProperties(f.properties ?? {});
                olFeatures.push(olFeature);
            } catch (e) {
                log(`  [geom error] ${e.message}`);
            }
        }

        if (olFeatures.length === 0) return;

        const source = new ol.source.Vector({ features: olFeatures });
        const layer = new ol.layer.Vector({ source, style: makeStyle(layerIndex) });
        map.addLayer(layer);
        resultLayers.push(layer);

        if (resultLayers.length === 1) {
            map.getView().fit(source.getExtent(), { padding: [40, 40, 40, 40], duration: 500 });
        }

        for (const f of fc.features) {
            if (f.properties) log(`  ${JSON.stringify(f.properties)}`);
        }
    }

    // ── EPSG:4326 [lon,lat] → EPSG:3857 using native OpenLayers ─────────────
    function toMercator(coords, type) {
        switch (type) {
            case 'Point':        return ol.proj.fromLonLat(coords);
            case 'LineString':   return coords.map(c => ol.proj.fromLonLat(c));
            case 'Polygon':      return coords.map(ring => ring.map(c => ol.proj.fromLonLat(c)));
            case 'MultiPolygon': return coords.map(poly => poly.map(ring => ring.map(c => ol.proj.fromLonLat(c))));
            default:             return coords;
        }
    }

    function buildOlGeometry(type, coords) {
        switch (type) {
            case 'Point':        return new ol.geom.Point(coords);
            case 'LineString':   return new ol.geom.LineString(coords);
            case 'Polygon':      return new ol.geom.Polygon(coords);
            case 'MultiPolygon': return new ol.geom.MultiPolygon(coords);
            default:             return null;
        }
    }

    // ── logging ──────────────────────────────────────────────────────────────
    function log(msg) {
        console.log(msg);
        if (dotNetRef) dotNetRef.invokeMethodAsync('OnLogMessage', msg);
    }

    // ── public API ───────────────────────────────────────────────────────────
    return {
        init(containerId, dotNetReference) {
            dotNetRef = dotNetReference;
            initMap(containerId);
        },
        clearResults() {
            clearResultLayers();
            drawSource.clear();
        }
    };
})();
