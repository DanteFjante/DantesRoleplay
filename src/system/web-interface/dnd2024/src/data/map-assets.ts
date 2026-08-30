/**
 * Reviewed presentation bytes for bounded map asset keys stored by live World locations.
 *
 * This registry deliberately contains no location IDs, hierarchy, audience selection, visibility,
 * or fallback rules. Live components decide whether a map exists and which exact key is available
 * to the current audience; this file only translates a reviewed key into a local public asset.
 */
const MAP_ASSET_URLS: Readonly<Record<string, string>> = Object.freeze({
  "thalos.player": "/components/maps/thalos-world.png",
  "thalos.dm": "/components/maps/thalos-world.png",
  "thalos.region.aldros.player": "/components/maps/region-aldros.png",
  "thalos.region.aldros.dm": "/components/maps/region-aldros.png",
  "thalos.region.evandos.player": "/components/maps/region-evandos.png",
  "thalos.region.evandos.dm": "/components/maps/region-evandos.png",
  "thalos.region.merceros.player": "/components/maps/region-merceros.png",
  "thalos.region.merceros.dm": "/components/maps/region-merceros.png",
  "thalos.region.minevros.player": "/components/maps/region-minevros.png",
  "thalos.region.minevros.dm": "/components/maps/region-minevros.png",
  "thalos.region.rhiannos.player": "/components/maps/region-rhiannos.png",
  "thalos.region.rhiannos.dm": "/components/maps/region-rhiannos.png",
  "thalos.region.southwestern-volcanic-region.player": "/components/maps/region-southwestern-volcanic.png",
  "thalos.region.southwestern-volcanic-region.dm": "/components/maps/region-southwestern-volcanic.png",
  "thalos.region.valeros.player": "/components/maps/region-valeros.png",
  "thalos.region.valeros.dm": "/components/maps/region-valeros.png",
  "thalos.region.waylos.player": "/components/maps/region-waylos.png",
  "thalos.region.waylos.dm": "/components/maps/region-waylos.png",
  "thalos.region.world-tree-grounds.player": "/components/maps/region-world-tree-grounds.png",
  "thalos.region.world-tree-grounds.dm": "/components/maps/region-world-tree-grounds.png",
  "thalos.city.crownmere.player": "/city-map-crownmere-v2.png",
  "thalos.city.crownmere.dm": "/city-map-crownmere-v2.png",
  "thalos.city.merrowgate.player": "/city-map-merrowgate-v2.png",
  "thalos.city.merrowgate.dm": "/city-map-merrowgate-v2.png",
});

export function resolveMapAssetUrl(assetKey: string, assetBaseUrl = "/"): string | null {
  const assetUrl = Object.hasOwn(MAP_ASSET_URLS, assetKey) ? MAP_ASSET_URLS[assetKey] ?? null : null;
  if (!assetUrl) return null;
  if (assetBaseUrl === "/" || assetUrl.startsWith("/components/")) return assetUrl;
  return `${assetBaseUrl.replace(/\/?$/u, "/")}${assetUrl.replace(/^\//u, "")}`;
}
